using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BoosterWatch.Guidance
{
    public interface IDescentModel
    {
        double AirDensity(double altitudeAsl);
        double Gravity(double altitudeAsl);
        double ThrustAccelerationVacuum { get; }
        double ThrustAccelerationSeaLevel { get; }
    }

    public interface IDescentEngineModel
    {
        double ThrustAccelerationAt(double altitudeAsl);
        double FullMassFlow { get; } // kg/s, at full throttle
        double BodyRadius { get; }
    }

    public sealed class DescentPrediction
    {
        public bool Valid, IgnitionNeeded, Unstoppable;
        public double IgnitionAltitude, IgnitionTime;
        public double TouchdownSpeed, SpeedMargin, BurnSeconds, RequiredDeltaV;
        public double CaptureSpeed, CaptureLateralSpeed;
        public double CalculatedAt, CalculationMilliseconds;
        public int Candidates;
        public bool UsedBestEffort;
        // Mit Zurueckdrehen aus dem Gleiten gerechnet (DescentState.PreBurnSeconds).
        public double PreBurnSeconds;
        public string FailureReason;
    }

    // Integrate the unpowered entry, then search candidate burns along that trajectory.
    // Trial burns run the real controller with planning disabled. A failed burn NOW does
    // not rule out coasting into denser air and completing a shorter burn later.
    public sealed class DescentPredictor
    {
        private readonly DescentConfig config;
        private readonly IDescentModel model;
        private readonly IDescentEngineModel engines;
        // In flight a forecast belongs on a worker thread: measured on this machine one costs
        // between one and forty milliseconds, and doing that inside a physics tick is the stutter
        // this predictor was moved out of the tick for. With `deferred` the law flies on the last
        // completed forecast and never waits for the next one.
        //
        // A caller on a virtual clock - the test harness, which flies 400 seconds of descent in
        // milliseconds of wall time - must turn it off instead: a worker result driven by wall time
        // would make the same flight depend on machine speed, and would in practice never arrive.
        // Off, the forecast is computed inline and is deterministic; on, the code below is the one
        // the game runs, and the flight keeps using the unchanged law either way.
        private readonly bool deferred;
        private const double TableStep = 100;
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(2);
        private Task<DescentPrediction> pending;
        private CancellationTokenSource cancellation;
        private CancellationToken token;
        private DescentPrediction latest;
        private double requestedAt = double.NegativeInfinity;
        private bool poweredRequest;
        private int candidates;
        public bool Pending { get { return pending != null; } }
        public DescentPredictor(DescentConfig config, IDescentModel model, bool deferred = true)
        { this.config = config; this.model = model; engines = model as IDescentEngineModel; this.deferred = deferred; }

        public void Cancel()
        {
            if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); cancellation = null; }
            pending = null; latest = null; requestedAt = double.NegativeInfinity;
        }

        // Sample KSP on the main thread, then integrate only copied numbers off-thread - or inline
        // when the caller turned the worker off (see `deferred`). In flight this never waits:
        // steering and throttle continue every physics tick.
        public DescentPrediction Update(DescentState state, double mass, DescentGuidance active, double cadence)
        {
            bool powered = active != null;
            if (state.Time < requestedAt || powered != poweredRequest) Cancel();
            poweredRequest = powered;
            if (pending != null && pending.IsCompleted)
            {
                latest = pending.Status == TaskStatus.RanToCompletion ? pending.Result : new DescentPrediction();
                pending = null;
                if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
            }
            // Old plans must not survive a time jump or a stalled background calculation.
            double maxAge = state.Clearance < 1000 ? 2 : 5;
            if (latest != null && state.Time - latest.CalculatedAt > maxAge)
                latest = new DescentPrediction { CalculatedAt = state.Time };
            if (pending == null && state.Time - requestedAt >= cadence)
            {
                requestedAt = state.Time;
                Corridor corridor = ValidInput(state, mass) ? Prepare(state, mass) : null;
                if (corridor == null) return latest = new DescentPrediction { CalculatedAt = state.Time };
                // Both paths compute on the same copy of the settings, so a background result and
                // an inline one are the same arithmetic and the settings cannot change underneath.
                DescentConfig snapshot = config.Snapshot();
                DescentGuidance controller = active == null ? null : active.CopyForPrediction(snapshot);
                if (!deferred) return latest = Calculate(state, mass, corridor, controller, snapshot, default(CancellationToken));
                cancellation = new CancellationTokenSource();
                CancellationToken requestToken = cancellation.Token;
                pending = Task.Run(() =>
                {
                    bool entered = false;
                    try
                    {
                        Workers.Wait(requestToken); entered = true;
                        return Calculate(state, mass, corridor, controller, snapshot, requestToken);
                    }
                    catch (OperationCanceledException) { return new DescentPrediction { CalculatedAt = state.Time }; }
                    catch (Exception error)
                    { return new DescentPrediction { CalculatedAt = state.Time, FailureReason = error.GetType().Name + ": " + error.Message }; }
                    finally { if (entered) Workers.Release(); }
                });
            }
            return latest;
        }

        // One forecast, from the tables the main thread sampled. Runs on a worker in flight and
        // inline for a caller that asked for a deterministic one.
        private DescentPrediction Calculate(DescentState state, double mass, Corridor corridor,
            DescentGuidance controller, DescentConfig snapshot, CancellationToken workToken)
        {
            var watch = Stopwatch.StartNew();
            var worker = new DescentPredictor(snapshot, null) { token = workToken };
            DescentPrediction result = worker.PredictPrepared(state, mass, corridor, controller);
            result.CalculatedAt = state.Time;
            result.CalculationMilliseconds = watch.Elapsed.TotalMilliseconds;
            result.Candidates = worker.candidates;
            return result;
        }

        private sealed class Corridor
        {
            public DescentState Initial;
            public double Mass, DryMass, Flow, Datum, Radius, LiftRatio, BurnDrag, BurnLiftRatio, PreBurnSeconds;
            public double[] Density, Gravity, Thrust;
        }
        private struct Point { public double Height, Sink, Lateral, Time; }
        private struct Trial
        {
            public bool Complete, Safe, Dry;
            public double Speed, CaptureSpeed, CaptureLateral, Seconds, DeltaV;
            // Wo die Zuendung wirklich beginnt (nach dem Zurueckdrehen aus dem Gleiten).
            public double StartHeight, StartTime;
        }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        private static double Clamp(double x, double low, double high) { return Math.Max(low, Math.Min(high, x)); }

        private Corridor Prepare(DescentState state, double mass)
        {
            var c = new Corridor { Initial = state, Mass = mass, Datum = state.AltitudeAsl - state.Clearance,
                Radius = engines == null ? 0 : engines.BodyRadius, LiftRatio = PlannedLiftRatio(state),
                BurnDrag = PlannedBurnDrag(state),
                PreBurnSeconds = Finite(state.PreBurnSeconds) ? Math.Max(0, state.PreBurnSeconds) : 0 };
            // Der Abtrieb kommt vom selben schiefen Anstellwinkel wie der Mehrwiderstand und geht mit
            // ihm zurueck, wenn sich die Stufe unter Schub aufrichtet (Flug vom 25.09.2026: -0,45 im
            // Fallen, -0,27 in der Zuendung bei 0,7-fachem Cd*A).
            double coastDrag = Math.Max(0, state.DragCoefficient);
            c.BurnLiftRatio = coastDrag > 1e-9 ? c.LiftRatio * Clamp(c.BurnDrag / coastDrag, 0, 1) : c.LiftRatio;
            // Beim Gleiten traegt der gemessene Auftrieb bis zum Zurueckdrehen auch nach oben: genau
            // dafuer wird geglitten, und ohne ihn hielt die Vorhersage jede Landung fuer aussichtslos,
            // weil die Stufe laenger in der dichten Luft bleibt, als sie ohne Auftrieb rechnete.
            // Fuer das Drehen und die Zuendung bleibt es beim Abtrieb allein (BurnLiftRatio).
            if (c.PreBurnSeconds > 0 && state.LiftKnown && Finite(state.LiftRatio))
                c.LiftRatio = Clamp(state.LiftRatio, -1, 1);
            double vacuum = Math.Max(0, model.ThrustAccelerationVacuum);
            c.Flow = engines != null ? engines.FullMassFlow
                : mass * vacuum / Math.Max(1, config.Predictor.SpecificImpulse * 9.80665);
            double fuel = engines != null || state.AvailableBurnTime > 0
                ? c.Flow * Math.Max(0, state.AvailableBurnTime) : mass * 0.9;
            c.DryMass = Math.Max(1, mass - Math.Min(mass - 1, fuel));
            int count = Math.Max(2, (int)(Math.Max(0, state.Clearance) / TableStep) + 52);
            c.Density = new double[count]; c.Gravity = new double[count]; c.Thrust = new double[count];
            double rhoSea = Math.Max(1e-9, model.AirDensity(0));
            for (int i = 0; i < count; i++)
            {
                // Ray/terrain clearance is AGL; the atmosphere and engine curves require ASL.
                double asl = c.Datum + i * TableStep;
                c.Density[i] = Math.Max(0, model.AirDensity(asl));
                c.Gravity[i] = model.Gravity(asl);
                c.Thrust[i] = engines != null ? engines.ThrustAccelerationAt(asl)
                    : vacuum + (model.ThrustAccelerationSeaLevel - vacuum) * Clamp(c.Density[i] / rhoSea, 0, 1);
                if (!Finite(c.Density[i]) || !Finite(c.Gravity[i]) || c.Gravity[i] <= 0
                    || !Finite(c.Thrust[i]) || c.Thrust[i] < 0) return null;
            }
            return c;
        }
        private static double Sample(double[] table, double height)
        {
            double x = Clamp(height / TableStep, 0, table.Length - 1);
            int i = Math.Min((int)x, table.Length - 2);
            return table[i] + (table[i + 1] - table[i]) * (x - i);
        }
        private double Drag(Corridor c, Point p, double mass, bool burn = false)
        {
            return 0.5 * Sample(c.Density, p.Height) * (p.Sink * p.Sink + p.Lateral * p.Lateral)
                * Math.Max(0, burn ? c.BurnDrag : c.Initial.DragCoefficient) / Math.Max(1, mass)
                * Clamp(config.Predictor.DragEffectiveness, 0, 1);
        }

        // Cd*A fuer die Zuendung. Unter Schub hat die Stufe ihre Schwenkduese und richtet sich auf
        // (Flug vom 25.09.2026: 6,5 vor der Zuendung, 4,9 / 4,5 / 3,2 in 7 / 5 / 1 km) - mit dem
        // Wert aus dem Gleit- oder Schieflagenflug plante die Vorhersage zu viel Luftbremse.
        internal static double PlannedBurnDrag(DescentState state)
        {
            double now = Math.Max(0, state.DragCoefficient);
            if (!Finite(state.BurnDragCoefficient) || state.BurnDragCoefficient <= 0) return now;
            return Math.Min(now, state.BurnDragCoefficient);
        }
        // Gemessener Rumpfauftrieb, nur der nach UNTEN: ein Auftrieb nach oben kann verschwinden,
        // sobald die Stufe fuer die Zuendung zurueckdreht, und darf die Zuendung nie nach hinten
        // schieben. Einer nach unten kommt von einer Stufe, die schief im Luftstrom haengt und das
        // nicht aendern kann - der bleibt, bis die Luft nachlaesst.
        internal static double PlannedLiftRatio(DescentState state)
        {
            if (!state.LiftKnown || !Finite(state.LiftRatio)) return 0;
            return Clamp(state.LiftRatio, -1, 0);
        }

        // Spherical local components include curvature. Drag opposes the whole velocity,
        // but only its vertical component supports weight. dt is limited by the drag timescale.
        // Lift acts along the upward normal of the path, (sink, lateral)/speed in (lateral, up).
        private Point Integrate(Corridor c, Point p, double drag, double upThrust, double lateralThrust, double dt,
            bool burn = false)
        {
            double speed = Math.Sqrt(p.Sink * p.Sink + p.Lateral * p.Lateral);
            double radius = c.Radius > 0 ? Math.Max(1, c.Radius + c.Datum + p.Height) : double.PositiveInfinity;
            double lift = (burn ? c.BurnLiftRatio : c.LiftRatio) * drag;
            double aSink = Sample(c.Gravity, p.Height) - p.Lateral * p.Lateral / radius - upThrust
                - (speed > 1e-9 ? drag * p.Sink / speed + lift * Math.Abs(p.Lateral) / speed : 0);
            double aLateral = lateralThrust + p.Sink * p.Lateral / radius
                - (speed > 1e-9 ? drag * p.Lateral / speed : 0)
                + (speed > 1e-9 ? lift * p.Sink / speed * Math.Sign(p.Lateral) : 0);
            double sink = p.Sink + aSink * dt;
            return new Point { Height = p.Height - 0.5 * (p.Sink + sink) * dt,
                Sink = sink, Lateral = p.Lateral + aLateral * dt, Time = p.Time + dt };
        }
        private static double StepSize(Point p, double drag, double requested)
        {
            double speed = Math.Sqrt(p.Sink * p.Sink + p.Lateral * p.Lateral);
            return Math.Max(0.001, Math.Min(requested, drag > 0 ? 0.2 * Math.Max(1, speed) / drag : requested));
        }
        private List<Point> Coast(Corridor c)
        {
            var points = new List<Point>();
            Point p = new Point { Height = c.Initial.Clearance, Sink = c.Initial.Sink,
                Lateral = c.Initial.LateralSpeed, Time = c.Initial.Time };
            double end = Math.Max(config.Terminal.EngineCutoffAltitude, config.Predictor.CaptureAltitude);
            for (int i = 0; i < 30000; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                points.Add(p);
                if (p.Height <= end) return points;
                double drag = Drag(c, p, c.Mass);
                double dt = StepSize(p, drag, Math.Min(0.5, Math.Max(0.05, (p.Height - end) / Math.Max(1, p.Sink) * 0.25)));
                Point next = Integrate(c, p, drag, 0, 0, dt);
                if (!Finite(next.Height) || !Finite(next.Sink) || !Finite(next.Lateral)) return null;
                if (next.Time - c.Initial.Time > config.Predictor.MaxSeconds) return null;
                if (next.Height <= end + 0.1) { points.Add(next); return points; }
                p = next;
            }
            return null;
        }
        private Trial Burn(Corridor c, Point start, DescentGuidance active = null)
        {
            candidates++;
            // Gleitet die Stufe gerade, ist jeder Kandidat das Ende des Gleitens: sie dreht zurueck und
            // faellt die Vorlaufzeit schon mit dem Rueckwaerts-Widerstand, erst dann zuendet sie.
            if (active == null && c.PreBurnSeconds > 0)
            {
                double end = start.Time + c.PreBurnSeconds;
                for (int i = 0; i < 20000 && start.Time < end && start.Height > 0; i++)
                {
                    // Waehrend des Drehens liegt der Widerstand zwischen Gleit- und Rueckwaerts-Wert.
                    double d = 0.5 * (Drag(c, start, c.Mass, true) + Drag(c, start, c.Mass));
                    double step = Math.Min(end - start.Time, StepSize(start, d, 0.1));
                    Point next = Integrate(c, start, d, 0, 0, step, true);
                    if (!Finite(next.Height) || !Finite(next.Sink) || !Finite(next.Lateral)) break;
                    start = next;
                }
            }
            var law = active == null ? new DescentGuidance(config, null) : active.CopyForPrediction();
            if (active == null) law.BeginPredictionBurn();
            Point p = start;
            double mass = c.Mass, elapsed = 0, deltaV = 0, burnSeconds = 0;
            double sinceCommand = active == null ? -1 : active.BurnAge(start.Time);
            double capture = config.Predictor.CaptureAltitude;
            bool captured = start.Height <= capture;
            Trial result = new Trial { CaptureSpeed = double.PositiveInfinity, CaptureLateral = double.PositiveInfinity,
                StartHeight = start.Height, StartTime = start.Time };
            if (captured) { result.CaptureSpeed = Math.Sqrt(p.Sink * p.Sink + p.Lateral * p.Lateral); result.CaptureLateral = Math.Abs(p.Lateral); }
            for (int i = 0; i < 40000 && elapsed <= config.Predictor.MaxSeconds; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                double drag = Drag(c, p, mass, true);
                double dt = StepSize(p, drag, Clamp(config.Predictor.TimeStep, 0.01, 0.2));
                double available = Sample(c.Thrust, p.Height) * c.Mass / mass;
                DescentState state = c.Initial;
                state.Time = p.Time; state.Clearance = p.Height; state.AltitudeAsl = c.Datum + p.Height;
                state.UpX = 0; state.UpY = 1; state.UpZ = 0;
                state.EastX = 1; state.EastY = 0; state.EastZ = 0;
                state.NorthX = 0; state.NorthY = 0; state.NorthZ = 1;
                state.VelocityUp = -p.Sink; state.VelocityEast = p.Lateral; state.VelocityNorth = 0;
                state.Gravity = Sample(c.Gravity, p.Height); state.ThrustAcceleration = available;
                state.AirDensity = Sample(c.Density, p.Height); state.DragValid = true; state.DragAcceleration = drag;
                GuidanceStep command = law.Step(state, mass, dt, true);
                if (!command.Valid || !Finite(command.Throttle)) return result;
                if (sinceCommand < 0 && command.Throttle > 1e-6) sinceCommand = 0;
                double spool = Clamp((sinceCommand - config.Predictor.IgnitionDelay)
                    / Math.Max(0.01, config.Predictor.ThrustRampSeconds), 0, 1);
                double throttle = command.Throttle * spool;
                if (mass <= c.DryMass + 1e-6) { throttle = 0; result.Dry = true; }
                double flow = c.Flow * throttle;
                if (flow > 0 && flow * dt > mass - c.DryMass)
                    throttle *= Math.Max(0, mass - c.DryMass) / (flow * dt);
                double acceleration = available * throttle * config.Predictor.BurnSteeringLoss;
                // Where the hull points is where the thrust goes, and above the pinning pressure the
                // airstream decides that, not the controller: the booster flies the relative wind, so
                // the engine brakes along the flight path instead of holding the descent up. The
                // flight logs of 24.09.2026 are unambiguous about it - the logged attitude error
                // equalled the angle between the command and the flight path (74 deg at 15 km,
                // 63 deg at 3.8 km) and fell to nothing only once the air had lost its momentum
                // (0.3 deg at 738 m, 21 kPa). Blending back to the commanded axis as the pressure
                // falls is what keeps the low, slow part of a trial - the part that lands -
                // unchanged, and it is why this forecast no longer promises a soft arrival to a
                // booster that is still being flown by the air.
                double up = command.Up, east = command.East;
                double speed = Math.Sqrt(p.Sink * p.Sink + p.Lateral * p.Lateral);
                double pinning = config.Predictor.AeroPinningPressure;
                if (pinning > 1 && speed > 1e-6)
                {
                    double pressure = 0.5 * Sample(c.Density, p.Height) * speed * speed;
                    double control = Clamp(1 - pressure / pinning, 0, 1);
                    if (control < 1)
                    {
                        double windUp = p.Sink / speed, windEast = -p.Lateral / speed;
                        up = (1 - control) * windUp + control * command.Up;
                        east = (1 - control) * windEast + control * command.East;
                        double norm = Math.Sqrt(up * up + east * east);
                        if (norm > 1e-9) { up /= norm; east /= norm; }
                        else { up = command.Up; east = command.East; }
                    }
                }
                Point next = Integrate(c, p, drag, acceleration * up, acceleration * east, dt, true);
                if (!Finite(next.Height) || !Finite(next.Sink) || !Finite(next.Lateral)) return result;
                if (!captured && next.Height <= capture)
                {
                    double f = Clamp((p.Height - capture) / Math.Max(1e-9, p.Height - next.Height), 0, 1);
                    double sink = p.Sink + f * (next.Sink - p.Sink);
                    double lateral = p.Lateral + f * (next.Lateral - p.Lateral);
                    result.CaptureSpeed = Math.Sqrt(sink * sink + lateral * lateral);
                    result.CaptureLateral = Math.Abs(lateral); captured = true;
                }
                mass = Math.Max(c.DryMass, mass - c.Flow * throttle * dt);
                deltaV += acceleration * dt;
                if (throttle > 0) burnSeconds += dt;
                elapsed += dt;
                if (sinceCommand >= 0) sinceCommand += dt;
                if (next.Height <= 0)
                {
                    result.Complete = true;
                    double fraction = Clamp(p.Height / Math.Max(1e-9, p.Height - next.Height), 0, 1);
                    double impactSink = p.Sink + fraction * (next.Sink - p.Sink);
                    double impactLateral = p.Lateral + fraction * (next.Lateral - p.Lateral);
                    result.Speed = Math.Sqrt(impactSink * impactSink + impactLateral * impactLateral);
                    result.Seconds = burnSeconds; result.DeltaV = deltaV;
                    // The existing final controller shuts down above contact. Include that
                    // final unpowered drop rather than calling speed at cutoff "touchdown".
                    double allowedImpact = Math.Sqrt(config.Terminal.TouchdownSpeed * config.Terminal.TouchdownSpeed
                        + 2 * Sample(c.Gravity, 0) * command.CutoffAltitude) + 2;
                    result.Safe = captured && !result.Dry
                        && result.CaptureSpeed <= config.Predictor.CaptureSpeed + 3
                        && result.CaptureLateral <= config.Predictor.CaptureLateralSpeed + config.Predictor.CaptureLateralTolerance
                        && result.Speed <= allowedImpact;
                    return result;
                }
                p = next;
            }
            return result;
        }
        public DescentPrediction Predict(DescentState state, double mass, DescentGuidance active = null)
        {
            if (!ValidInput(state, mass)) return new DescentPrediction();
            Corridor c = Prepare(state, mass);
            return PredictPrepared(state, mass, c, active);
        }
        private static bool ValidInput(DescentState state, double mass)
        {
            return state.Valid && Finite(state.Clearance) && state.Clearance >= 0 && Finite(mass) && mass > 1
                && Finite(state.Sink) && Finite(state.LateralSpeed) && Finite(state.AltitudeAsl)
                && Finite(state.DragCoefficient) && Finite(state.ThrustAcceleration) && state.ThrustAcceleration > 0;
        }
        private DescentPrediction PredictPrepared(DescentState state, double mass, Corridor c, DescentGuidance active)
        {
            var result = new DescentPrediction { CalculatedAt = state.Time };
            if (c == null) return result;
            if (active != null)
            {
                // Update the actual ongoing burn from current mass, drag and controller history.
                // Do not restart ignition delay, or pretend an already burning vessel can coast first.
                Trial continuation = Burn(c, new Point { Height = state.Clearance, Sink = state.Sink,
                    Lateral = state.LateralSpeed, Time = state.Time }, active);
                if (!continuation.Complete) return result;
                result.Valid = true;
                result.IgnitionTime = state.Time;
                result.IgnitionAltitude = active.LastPrediction != null ? active.LastPrediction.IgnitionAltitude : state.Clearance;
                result.TouchdownSpeed = continuation.Speed;
                result.CaptureSpeed = continuation.CaptureSpeed;
                result.CaptureLateralSpeed = continuation.CaptureLateral;
                result.SpeedMargin = config.Predictor.CaptureSpeed + 3 - continuation.CaptureSpeed;
                result.BurnSeconds = continuation.Seconds; result.RequiredDeltaV = continuation.DeltaV;
                double allowedImpact = Math.Sqrt(config.Terminal.TouchdownSpeed * config.Terminal.TouchdownSpeed
                    + 2 * Sample(c.Gravity, 0) * active.CutoffAltitude(state.SlopeDegrees, 0)) + 2;
                result.Unstoppable = continuation.Speed > allowedImpact;
                return result;
            }
            List<Point> coast = Coast(c);
            if (coast == null || coast.Count == 0) return result;
            int samples = Math.Max(4, config.Predictor.CoarseSamples);
            int best = -1, nextUnsafe = coast.Count - 1;
            int bestEffort = -1;
            double bestScore = double.PositiveInfinity;
            Trial chosen = new Trial(), effort = new Trial();
            // Search from late to early: the first safe candidate bounds the latest safe
            // burn. Early, long burns need not all be integrated on every update.
            int lastIndex = -1;
            for (int i = samples; i >= 0; i--)
            {
                int index = i * (coast.Count - 1) / samples;
                // A short coast maps several candidates onto the same point; integrate each once.
                if (index == lastIndex) continue;
                lastIndex = index;
                Trial trial = Burn(c, coast[index]);
                // If no candidate reaches every target, retain the least-bad complete plan.
                // "No perfect plan" must not mean "burn now" if a later burn is clearly better.
                double allowedImpact = Math.Sqrt(config.Terminal.TouchdownSpeed * config.Terminal.TouchdownSpeed
                    + 2 * Sample(c.Gravity, 0) * config.Terminal.EngineCutoffAltitude) + 2;
                double score = trial.Complete ? Math.Max(0, trial.Speed - allowedImpact)
                    + Math.Max(0, trial.CaptureSpeed - config.Predictor.CaptureSpeed - 3)
                    + 2 * Math.Max(0, trial.CaptureLateral - config.Predictor.CaptureLateralSpeed - config.Predictor.CaptureLateralTolerance)
                    + (trial.Dry ? 100 : 0) : double.PositiveInfinity;
                // Sub-tolerance differences at touchdown do not justify a much longer burn.

                if (Finite(score) && (score < bestScore - 0.25
                    || (Math.Abs(score - bestScore) <= 0.25 && trial.DeltaV < effort.DeltaV)))
                { bestScore = score; bestEffort = index; effort = trial; }
                if (trial.Safe) { best = index; chosen = trial; break; }
                nextUnsafe = index;
            }
            if (best >= 0)
            {
                for (int i = 0; i < config.Predictor.BisectionSteps && nextUnsafe - best > 1; i++)
                {
                    int middle = (best + nextUnsafe) / 2;
                    Trial trial = Burn(c, coast[middle]);
                    if (trial.Safe) { best = middle; chosen = trial; } else nextUnsafe = middle;
                }
            }
            else
            {
                if (bestEffort < 0) return result;
                best = bestEffort; chosen = effort;
                result.UsedBestEffort = true;
                result.Unstoppable = chosen.Speed > Math.Sqrt(config.Terminal.TouchdownSpeed * config.Terminal.TouchdownSpeed
                    + 2 * Sample(c.Gravity, 0) * config.Terminal.EngineCutoffAltitude) + 2;
            }
            result.Valid = true;
            result.PreBurnSeconds = c.PreBurnSeconds;
            result.IgnitionAltitude = chosen.StartHeight; result.IgnitionTime = chosen.StartTime;
            result.IgnitionNeeded = (best == 0 && c.PreBurnSeconds <= 0)
                || result.IgnitionTime - state.Time <= config.Predictor.UpdateInterval;
            result.TouchdownSpeed = chosen.Speed;
            result.CaptureSpeed = chosen.CaptureSpeed; result.CaptureLateralSpeed = chosen.CaptureLateral;
            result.SpeedMargin = config.Predictor.CaptureSpeed + 3 - chosen.CaptureSpeed;
            result.BurnSeconds = chosen.Seconds; result.RequiredDeltaV = chosen.DeltaV;
            return result;
        }
    }
}
