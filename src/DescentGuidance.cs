using System;

namespace BoosterWatch.Guidance
{
    // The landing law.
    //
    // One equation decides everything. Every step it commands a NET ACCELERATION vector in the
    // local horizon frame (the vertical component is the exact derivative of the vertical
    // velocity), and thrust and attitude follow from that vector mechanically:
    //
    //     throttle = |a| / (T/m)          the engine axis points along a
    //
    // There is no PID on the throttle. g is commanded directly as a feed-forward term, which is
    // why the same law flies a booster with TWR 1.2 and one with TWR 3. There is no target speed
    // coupled to the flight direction either: the direction comes from the acceleration vector and
    // from world-space basis vectors, both of which stay defined when the velocity goes to zero.
    // The old chain's "88 degrees at 103 m" and its switching between a retrograde and a vertical
    // controller cannot happen here, because there is only one controller and its target moves
    // continuously with the altitude.
    public sealed class DescentGuidance
    {
        private static readonly double[] NoProfile = new double[0];

        private readonly DescentConfig config;
        private readonly DescentPredictor predictor;
        private double[] densityProfile = NoProfile;
        private double profileStep = 100, profileTop;

        // Continuity state: the commanded lateral acceleration, the throttle and the sink target
        // may only move at a bounded rate from one step to the next, so nothing can jump.
        private bool hasLast;
        private double lastLateralAcceleration, lastThrottle, lastTargetSink;
        private DescentPhase phase = DescentPhase.Idle;
        // Set once the forecast can no longer stop the descent. A crash is not un-decided.
        private bool aborted;
        // Set once the burn has been ordered. One-way, so the phase cannot oscillate at ignition.
        private bool ordered;
        // Ordering a future burn is not applying thrust. Stay retrograde through the
        // initial zero-throttle interval; retain the existing attitude law after ignition.
        private bool hasBurnCommand;
        private double firstBurnCommandTime = double.NaN;
        internal double BurnAge(double now) { return double.IsNaN(firstBurnCommandTime) ? 0 : Math.Max(0, now - firstBurnCommandTime); }

        public DescentGuidance(DescentConfig config, DescentPredictor predictor)
        {
            this.config = config;
            this.predictor = predictor;
        }

        internal void BeginPredictionBurn()
        {
            Reset();
            ordered = true;
            phase = DescentPhase.Burn;
        }

        internal DescentGuidance CopyForPrediction(DescentConfig snapshot = null)
        {
            return new DescentGuidance(snapshot ?? config, null) {
                hasLast = hasLast, lastLateralAcceleration = lastLateralAcceleration,
                lastThrottle = lastThrottle, lastTargetSink = lastTargetSink,
                phase = phase, aborted = aborted, ordered = ordered, hasBurnCommand = hasBurnCommand,
                firstBurnCommandTime = firstBurnCommandTime, LastPrediction = LastPrediction
            };
        }

        public DescentPhase Phase { get { return phase; } }
        // The configuration in force, for tests that need to check what the law is actually using.
        public DescentConfig ConfigForTest { get { return config; } }
        public DescentPrediction LastPrediction { get; private set; }
        // The whole last decision, for the log line. Handing back the struct rather than a handful
        // of properties keeps the diagnostics and the flight on exactly the same numbers.
        public GuidanceStep? LastStep { get; private set; }
        public bool Aborted { get { return aborted; } }
        // The one-way latch: once the burn has been ordered it stays ordered. In the flight file this
        // is the difference between "the law wanted to burn here" (IgnitionDue) and "the burn was
        // already running", which is the distinction a short burn followed by silence turns on.
        public bool IgnitionOrdered { get { return ordered; } }
        // WHY the burn was ordered, captured at the one tick it happens, with the numbers that were on
        // the table. Three different triggers can light the engines and four flights were spent
        // guessing which one fired; this is the column that ends that.
        private string ignitionReason = "";
        public string IgnitionReason { get { return ignitionReason; } }
        // Speed the descent would arrive with now, for the abort log line.
        public double AbortSpeed { get; private set; }

        public void Reset()
        {
            CancelPrediction();
            hasLast = false;
            lastLateralAcceleration = 0;
            lastThrottle = 0;
            lastTargetSink = 0;
            phase = DescentPhase.Idle;
            aborted = false;
            ordered = false;
            hasBurnCommand = false;
            firstBurnCommandTime = double.NaN;
            ignitionReason = "";
            AbortSpeed = 0;
            LastPrediction = null;
        }

        public void CancelPrediction() { if (predictor != null) predictor.Cancel(); }

        // The flight adapter hands over the atmospheric profile around the booster once, so the
        // forecast works on KSP's own pressure curve instead of an exponential guess. Without a
        // profile the config default is sampled per altitude.
        public void SetDensityProfile(float[] samples, double step)
        {
            if (samples == null || samples.Length < 2 || step <= 0) { densityProfile = NoProfile; return; }
            densityProfile = new double[samples.Length];
            for (int i = 0; i < samples.Length; i++) densityProfile[i] = Math.Max(0, samples[i]);
            profileStep = step;
            profileTop = step * (samples.Length - 1);
        }

        public double DensityAt(double altitude)
        {
            if (densityProfile.Length == 0) return config.SampleAirDensity(altitude);
            if (altitude >= profileTop) return densityProfile[densityProfile.Length - 1];
            if (altitude <= 0) return densityProfile[0];
            double x = altitude / profileStep;
            int i = (int)x;
            if (i + 1 >= densityProfile.Length) return densityProfile[densityProfile.Length - 1];
            return densityProfile[i] + (densityProfile[i + 1] - densityProfile[i]) * (x - i);
        }

        // The sink-rate profile, as a function of the height alone. One continuous expression for
        // the whole descent, and its shape is what decides which kind of landing this is:
        //
        //   strong engines -> a large braking budget, a steep profile, a fast fall and a late, hard
        //                     brake: a hover-slam.
        //   weak engines   -> a small budget, a shallow profile, a long gentle approach.
        //
        // Nothing switches between the two and no ignition altitude is configured anywhere.
        //
        // The profile is a ladder anchored at the touchdown speed whose coefficient is the SQUARE of
        // the achievable descent rate - not the descent rate. That is what makes it flyable: the
        // ladder v^2 = v_td^2 + 2*a*d decays at exactly the deceleration a as the booster follows
        // it, so a profile built from `free` is one the engines can hold with full thrust and no
        // more. Built from `free` itself the ladder decays far too slowly and the booster would
        // have to fly faster than free fall to stay on it.
        //
        // For the same reason the coefficient is clamped at 1. Above it the ladder rises faster than
        // 2g, so at altitude it can demand a descent rate steeper than the ballistic fall - a rate
        // the booster can only reach by climbing first, which is exactly what it then does: hundreds
        // of metres up, engines lit, because a profile it cannot match reads to it as a descent that
        // is too slow. Measured: with the coefficient at 0.35 a booster that separates high up aims
        // at 200 m/s of sink from 40 km and ends up climbing instead of landing.
        public double TargetSink(double clearance, double freeAcceleration, double touchdownSpeed)
        {
            return TargetSink(clearance, freeAcceleration, touchdownSpeed, config.Terminal.EngineCutoffAltitude);
        }

        public double TargetSink(double clearance, double freeAcceleration, double touchdownSpeed,
            double cutoffAltitude)
        {
            double remaining = clearance - cutoffAltitude;
            if (remaining <= 0) return touchdownSpeed;
            double slope = Math.Min(1.0, config.Control.ProfileFraction)
                * 2 * Math.Max(0.05, freeAcceleration);
            double capture = Math.Max(cutoffAltitude + 1, config.Predictor.CaptureAltitude);
            double captureSpeed = Math.Max(touchdownSpeed, Math.Min(config.Predictor.CaptureSpeed,
                Math.Sqrt(touchdownSpeed * touchdownSpeed + slope * (capture - cutoffAltitude))));
            if (clearance > capture)
                return Math.Sqrt(captureSpeed * captureSpeed
                    + 1.6 * Math.Max(0.05, freeAcceleration) * (clearance - capture));
            // Preserve the soft-flare shape but join the 100 m capture continuously.
            slope = Math.Min(slope, Math.Max(0, captureSpeed * captureSpeed - touchdownSpeed * touchdownSpeed)
                / (capture - cutoffAltitude));
            return Math.Sqrt(touchdownSpeed * touchdownSpeed + slope * remaining);
        }

        // Where the engines are cut, given the slope under the touchdown point. On a slope the
        // booster touches down on one edge of its base first, so cutting at a fixed height drives
        // that edge into the ground. The allowance is the slope plus whatever heel the attitude
        // still has, over the hull's horizontal reach.
        public double CutoffAltitude(double slopeDegrees, double heelDegrees)
        {
            return GroundScan.CutoffHeight(config.Terminal.EngineCutoffAltitude, slopeDegrees,
                config.Terminal.HullRadius, heelDegrees);
        }

        public GuidanceStep Step(DescentState state, double mass, double deltaTime, bool aligned)
        {
            GuidanceStep output = new GuidanceStep { Phase = phase, Valid = false };
            if (!state.Valid || mass <= 0 || deltaTime <= 0) return output;
            double clearance = state.Clearance;
            double gravity = state.Gravity > 0 ? state.Gravity : config.SampleGravity(state.AltitudeAsl);
            double thrustAcceleration = Math.Max(0, state.ThrustAcceleration);
            // The height the engines are cut at, raised for the slope the booster will land on.
            double cutoff = CutoffAltitude(state.SlopeDegrees, 0);
            output.CutoffAltitude = cutoff;
            // What the engines have above local gravity: the braking budget the profile is built
            // from, and the number the tilt limit is scaled from. Negative means the booster cannot
            // hold itself up at all, and the whole descent becomes best effort.
            double free = thrustAcceleration * (1 - config.Predictor.ThrustReserve) - gravity;
            // A booster whose thrust barely beats gravity gets the whole engine, not the reserved
            // part of it. The reserve pays for attitude control and ignition lag, and a vehicle at
            // TWR 1.3 has nothing to pay it with: keeping 20 % back left it with 0.19 m/s^2 of
            // braking authority and it arrived at 105 m/s. Strong boosters are untouched - their
            // reserve is small change against a large surplus.
            if (free < 1) free = Math.Max(0.05, thrustAcceleration - gravity);
            double sink = state.Sink;
            double lateral = state.LateralSpeed;
            // A scheduled but still dark burn can be replanned as new aerodynamic data arrives.
            if (predictor != null && !hasBurnCommand && (phase == DescentPhase.Burn || phase == DescentPhase.Terminal))
            { ordered = false; phase = DescentPhase.Entry; }
            Advance(state.Time, clearance, aligned, cutoff);
            if (phase == DescentPhase.Idle || phase == DescentPhase.Align)
            {
                // Nothing is predicted yet: the booster is still accelerating upwards with its
                // upper stage and every reading would be meaningless. Point it backwards and wait.
                output.Phase = phase;
                output.Valid = true;
                AimCoast(ref output, state, sink, lateral);
                Remember(0, 0, 0);
                return output;
            }
            if (phase == DescentPhase.Done)
            {
                output.Phase = phase;
                output.Valid = true;
                AimCoast(ref output, state, sink, lateral);
                return output;
            }

            // --- target sink rate ------------------------------------------------------------
            // The profile carries the descent; below the soft-flare height its anchor is lowered
            // towards the soft touchdown speed, so the last couple of metres are gentler than the
            // approach without a step in the target.
            double anchor = config.Terminal.TouchdownSpeed;
            if (clearance < config.Terminal.SoftFlareAltitude && config.Terminal.SoftFlareAltitude > 0)
                anchor = config.Terminal.SoftTouchdownSpeed
                    + (config.Terminal.TouchdownSpeed - config.Terminal.SoftTouchdownSpeed)
                    * (clearance / config.Terminal.SoftFlareAltitude);
            double target = TargetSink(clearance, free, anchor, cutoff);

            // One coast-then-burn forecast owns ignition. Its trial burns execute this same
            // guidance law with planning disabled; they cannot recursively call the predictor.
            bool burning = phase == DescentPhase.Burn || phase == DescentPhase.Terminal;
            double cadence = clearance < 1000 ? 0.1 : config.Predictor.UpdateInterval;
            if (predictor != null && phase != DescentPhase.Touchdown)
            {
                LastPrediction = predictor.Update(state, mass, hasBurnCommand ? this : null, cadence);
            }
            DescentPrediction prediction = LastPrediction;
            bool forecastDue = prediction != null && prediction.Valid
                && (prediction.IgnitionNeeded || state.Time + cadence >= prediction.IgnitionTime);
            // If input/model data fail, retain a conservative braking fallback, with no
            // arbitrary atmosphere-density or 20 km veto. A valid forecast always takes precedence.
            bool fallback = predictor != null && (prediction == null || !prediction.Valid)
                && (prediction != null || !predictor.Pending
                    || clearance / Math.Max(1, sink) < 5)
                && sink > 0 && (sink * sink + lateral * lateral)
                    / (2 * Math.Max(0.1, free)) + sink * 2 >= Math.Max(0, clearance - config.Predictor.CaptureAltitude);
            bool needsIgnition = !ordered && !burning && (forecastDue || fallback);
            if (needsIgnition)
            {
                ordered = true;
                phase = DescentPhase.Burn;
                ignitionReason = (fallback ? "fallback" : "coast-burn-plan")
                    + " h=" + clearance.ToString("0")
                    + " plan=" + (prediction != null ? prediction.IgnitionAltitude.ToString("0") : "--")
                    + " capture=" + config.Predictor.CaptureAltitude.ToString("0");
            }
            burning = phase == DescentPhase.Burn || phase == DescentPhase.Terminal;
            bool coasting = !ordered && !burning && phase == DescentPhase.Entry;
            output.IgnitionDue = needsIgnition;
            output.CoastSpeed = Math.Sqrt(sink * sink + lateral * lateral);
            output.CoastBudget = 0; // Retired speed-budget heuristic; the log reports the planned height.
            output.CoastDrag = state.DragValid ? Math.Max(0, state.DragAcceleration) : 0;
            // A descent the booster can no longer be saved from is a crash in the making. Two ways
            // that happens, and either is enough to say so out loud: the engines have no thrust left
            // above local gravity at all, or the booster is already in the last hundred metres still
            // coming down far too fast. The engines still go to full and the attitude stays upright
            // - that is worth a few m/s - but the status line reports a crash instead of a landing.
            // Once set, it is not un-set.
            if (!aborted && burning && free < 0) aborted = true;
            if (!aborted && burning && clearance < 100 && sink > 15) aborted = true;
            // And it is allowed to change its mind. The flag means "this descent is a crash in the
            // making", and the recorded flight showed the law pulling one back from 57 m/s at 94 m
            // and touching down at 2 m/s - with a latch on the flag the window called that a crash
            // all the way down. What happened stays in AbortSpeed; the state clears once the descent
            // is under control again with height to spare.
            if (aborted && sink <= target + 2 && clearance > 50) aborted = false;
            if (aborted && AbortSpeed <= 0) AbortSpeed = sink;
            output.Abort = aborted;

            // --- the acceleration command ---------------------------------------------------
            // Two expressions, and the larger one flies the descent:
            //
            //   the profile's own deceleration is what holding the ladder costs. Riding a ladder of
            //   v^2 = v_td^2 + 2*a*d means descending at exactly that rate of change, so this is the
            //   nominal throttle for the whole approach;
            //   the deceleration that removes the speed still left over the distance that remains,
            //   0.5 * (sink^2 - target^2) / distance, is the definition of "still stoppable". It is
            //   what closes a gap the profile alone would leave open.
            //
            // The larger of the two is the safe one: the profile term alone lets a booster that fell
            // past the profile stay above it for the whole rest of the descent and arrive short,
            // while the requirement term alone is far too aggressive high up and would slam the
            // engines open hundreds of metres early. Both saturate at the budget the engines have
            // above gravity, which is what makes a fast entry take everything they have.
            //
            // What the air is already doing is credited on top. It is not a small correction: a
            // booster reentering at 2 km/s meets about 20 m/s^2 of drag in the upper atmosphere -
            // twice local gravity - and a law that does not know this reads its own rising altitude
            // as "descending too slowly" and opens the engines to push itself down, which makes it
            // climb all the harder. The whole entry then ends tens of kilometres too high.
            double verticalCommand = 0;
            if (burning)
            {
                double budget = Math.Max(0.05, free);
                double distance = Math.Max(0.5, clearance - cutoff);
                double drag = state.DragValid ? Math.Max(0, state.DragAcceleration) : 0;
                // Only the vertical component supports weight. Sideways drag is not lift.
                double speed = Math.Sqrt(sink * sink + lateral * lateral);
                drag *= speed > 1e-6 ? sink / speed : 0;
                // A measurement, so it can be wrong. Crediting a booster with more drag than five
                // times its own weight is never right, and the cost of believing it is the engines
                // staying dark: the term is subtracted from gravity, so a bogus reading of a few
                // hundred m/s^2 zeroes the command outright.
                if (drag > 5 * gravity) drag = 5 * gravity;
                // The profile is the FASTEST descent the law allows, not a rate to be tracked from
                // underneath. Braking to get down to the ladder is what it is for; adding thrust while
                // the booster is already slower than the ladder holds it up exactly where it is. That
                // is not a small error either: lit below the ladder, the law hovers - measured in the
                // entry from the second flight, sink rate zero for 600 s at 31 km with 20 t of
                // propellant going overboard - and the same thing one step lower down is a booster
                // that lights up "far too early" and then has nothing left for the landing.
                //
                // Below the ladder the engines stay dark and gravity does the work of closing the gap;
                // the moment the sink rate reaches the ladder, the deceleration term takes over.
                if (sink >= target)
                {
                    double requirement = 0.5 * (sink * sink - target * target) / distance;
                    // And a term that pays for the deviation itself. `requirement` above only knows
                    // how much longer the ground is, so a booster two and a half times above its
                    // ladder is asked for a few m/s^2 and drifts down to meet it at the ground - the
                    // recorded flight touched down at 29 m/s that way, with the law commanding 24 %
                    // and the air supplying the rest. On the ladder this term is zero, so a descent
                    // that is already tracking it flies exactly as before.
                    double tracking = config.Control.LadderGain * (sink - target);
                    // The steep braking profile itself requires deceleration even at zero
                    // tracking error. Without feed-forward the burn lags the profile by
                    // tens of m/s and every trial incorrectly rejects the 100 m handover.
                    if (clearance > config.Predictor.CaptureAltitude)
                        tracking += 0.8 * budget;
                    if (tracking > requirement) requirement = tracking;
                    verticalCommand = Math.Max(0, gravity - drag + Math.Min(budget, requirement));
                }
            }

            // A booster that has stopped descending is not to be held up. `deceleration` above is
            // always positive - it is the profile's own deceleration - so the command keeps adding
            // gravity even once the sink rate is gone, and a booster that the burn pushed upwards
            // goes on climbing until the tanks are dry. Measured in the second flight: sink
            // -1498 m/s and gaining altitude, engines finally shut off with nothing left to burn.
            // Gravity is what brings it back, so the engines stay dark until the booster is
            // descending again; the attitude falls back to upright and the law picks the descent up
            // where it left it.
            bool rising = burning && sink <= 0;
            if (rising) verticalCommand = 0;

            // While the booster is still crossing the sky, the descent does not own the engines. A
            // booster arriving from a high separation carries its speed sideways, and the burn that
            // decides the landing is the one at the end - so for as long as the whole of the
            // remaining speed can still be removed, the engines stay dark and the air does the work.
            // Rushing that is what turns an entry into a hundred-second burn 39 km up with the
            // propellant for the landing already gone.
            if (coasting) verticalCommand = 0;
            verticalCommand = Clamp(verticalCommand, 0, thrustAcceleration * (1 - config.Predictor.ThrustReserve));
            output.DescentRateLimited = sink > target + 1 && free < 1;

            // Lateral: slow the drift down to the speed that is still allowed at contact. The
            // proportional term is the whole controller - there is no separate "kill horizontal
            // velocity" mode to switch into, which is what the old chain kept getting wrong.
            //
            // The lateral command is capped by the tilt limit, and the tilt limit in turn is what
            // decides how much of the engine's authority may point sideways. Without a floor under
            // that cap the command collapses to nearly nothing at high descent rates, and a booster
            // that arrived with a strong crosswind keeps every metre per second of it. The floor is
            // a fixed small fraction of local gravity, so the attitude has something to work with at
            // every point of the descent.
            //
            // While the booster is still crossing the sky, the sideways speed is not a fault to be
            // corrected - it is what the air is removing, for free. Fighting it with the engines
            // there spends the propellant the landing needs, and a rocket that points sideways at
            // entry speed presents its broadside to the airstream as well. So the sideways authority
            // is held shut until the descent owns the vehicle.
            double lateralError = lateral - config.Terminal.LateralSpeed;
            double gain = config.Control.LateralDampingGain / config.Control.LateralTimeConstant;
            double lateralCommand = lateralError * gain;
            // One half of v^2/a still ahead: the drift is removed before the terminal altitude
            // instead of being carried down to the ground. The deadline sits at twice the terminal
            // altitude, not at the terminal altitude itself, because the correction needs the height
            // in which to work and a booster that arrives at the deadline with drift left has no way
            // to finish the job in the last few metres - measured: 3.7 m/s of drift at contact.
            double stopLimit = lateral * lateral
                / (2 * Math.Max(1, clearance - 2 * config.Terminal.Altitude));
            if (Math.Abs(lateralCommand) > stopLimit) lateralCommand = Math.Sign(lateralCommand) * stopLimit;
            double tiltLimit = TiltLimit(clearance, free);
            double leanLimit = Math.Max(config.Control.MinimumLateralAcceleration,
                Math.Max(0, free) * Math.Tan(tiltLimit));
            lateralCommand = Clamp(lateralCommand, -leanLimit, leanLimit);
            // The engine axis is the one direction the law commands, so a lateral command with no
            // vertical command under it does not mean "lean a little" - it means "point the booster
            // horizontally". Below the ladder, where the vertical command is zero on purpose, the
            // engines stay dark in both axes.
            if (verticalCommand <= 1e-9) lateralCommand = 0;
            // And in the last metres nothing is spent on the drift at all: the booster is brought
            // upright and set down on its legs. Anything still drifting at this height is better
            // landed on a straight booster than on a leaning one - the whole load of a lean goes into
            // one edge of the base.
            //
            // Faded, not switched. Cutting the lateral command dead at 20 m steps the commanded
            // attitude in one tick, and a 38 t booster with 15 kNm of torque cannot follow a step: the
            // recorded flight swung its aim through 37, 59 and 71 degrees in the last twenty metres
            // while the attitude error sat between 20 and 60 degrees - visible from outside as a
            // booster tumbling onto its legs. The fade starts three terminal altitudes up, so the
            // vehicle has the whole final approach to straighten out.
            double fadeTop = Math.Max(config.Terminal.UprightAltitude + 1, 3 * config.Terminal.Altitude);
            if (clearance < fadeTop)
            {
                double fade = (clearance - config.Terminal.UprightAltitude) / (fadeTop - config.Terminal.UprightAltitude);
                lateralCommand *= Math.Max(0, Math.Min(1, fade));
            }
            // Rising: no thrust in either axis. Killing the drift with a sideways burn while the
            // booster is climbing away is not an improvement on letting gravity have it back.
            if (rising) lateralCommand = 0;
            if (coasting)
            {
                // Point backwards and leave the sideways speed alone.
                output.IgnitionDue = false;
                output.Coasting = true;
                output.Phase = phase;
                output.Valid = true;
                output.Throttle = 0;
                output.TargetSink = target;
                output.FreeAcceleration = free;
                output.TerminalTiltLimit = tiltLimit * 180 / Math.PI;
                output.CutoffAltitude = cutoff;
                AimCoast(ref output, state, sink, lateral);
                Remember(0, 0, target);
                LastStep = output;
                return output;
            }
            if (!hasLast) lateralCommand = 0;
            else
            {
                // Near the ground the command may only creep: a heavy booster needs seconds to swing,
                // and a command that jumps is a command it answers with a tumble.
                double allowed = config.Control.MaxLateralAccelerationChange * Math.Max(0.01, deltaTime / 0.02)
                    * Math.Max(0.15, Math.Min(1, clearance / 200));
                lateralCommand = Clamp(lateralCommand, lastLateralAcceleration - allowed, lastLateralAcceleration + allowed);
            }

            // There used to stand here: "if the lateral controller is working at all, raise the
            // vertical command to local gravity". The thought behind it is that a leaning booster
            // spends part of its thrust sideways and must not lose height over it. What it actually
            // says is that a booster with any drift left to kill may never descend - and a returning
            // booster has 2000 m/s of drift, so the law held it at altitude and burned the landing
            // propellant hovering. The ladder above already answers the question: it commands gravity
            // when the sink rate is on the ladder, more when the booster is too fast, and nothing at
            // all when it is too slow. Nothing here needs to add to that.

            // --- how far open the engines go ------------------------------------------------
            // The throttle is the magnitude of the command over what the engines deliver, which is
            // why the command carries its magnitude: scaling the unit direction by the throttle does
            // NOT reproduce it, because the throttle divides by the whole vector and not by its
            // vertical part alone. That mismatch used to leave every lateral command applied at a
            // fraction of its size.
            double throttle = 0;
            double captureVertical = 0, captureLateral = 0;
            bool emergency = false;
            if (burning && !rising)
                CaptureBraking.Command(state, config, cutoff, out captureVertical, out captureLateral, out emergency);
            // Keep the proven slow terminal controller. Fast arrivals, including ones already
            // below the terminal altitude, must first shed BOTH components of their velocity.
            bool vectorBraking = burning && !rising && (clearance > config.Predictor.CaptureAltitude
                || sink > config.Predictor.CaptureSpeed + 3
                || lateral > config.Predictor.CaptureLateralSpeed || emergency);
            if (vectorBraking)
            {
                verticalCommand = captureVertical; lateralCommand = captureLateral;
                // Smooth ordinary steering; emergency stopping authority is not delayed by UI/tilt fades.
                if (hasLast && !emergency)
                {
                    double change = config.Control.MaxLateralAccelerationChange * Math.Max(0.01, deltaTime / 0.02);
                    lateralCommand = Clamp(lateralCommand, Math.Max(0, lastLateralAcceleration - change), lastLateralAcceleration + change);
                }
                // Commands and throttle must describe the same physically available vector.
                double magnitude = Math.Sqrt(verticalCommand * verticalCommand + lateralCommand * lateralCommand);
                double budget = thrustAcceleration * (emergency ? 1 : 1 - config.Predictor.ThrustReserve);
                if (magnitude > budget && magnitude > 1e-9)
                { verticalCommand *= budget / magnitude; lateralCommand *= budget / magnitude; }
                output.EmergencyBraking = emergency;
                output.VectorBraking = true;
            }
            if (burning)
                throttle = Math.Sqrt(verticalCommand * verticalCommand + lateralCommand * lateralCommand)
                    / Math.Max(1e-6, thrustAcceleration);
            throttle = Clamp(throttle, 0, 1);
            Remember(burning ? lateralCommand : 0, throttle, target);

            // --- where the engine has to point ----------------------------------------------
            // Burning, the thrust axis is the acceleration command itself. Because the lateral
            // command is capped by the tilt limit and the vertical command by the engines, the
            // axis the law asks for is always one the vehicle can fly - a demanded attitude the
            // booster cannot reach is excluded by construction, which is what the old chain kept
            // running into.
            if (burning && throttle > 1e-6)
            {
                if (!hasBurnCommand) firstBurnCommandTime = state.Time;
                hasBurnCommand = true;
            }
            if (burning && hasBurnCommand) AimBurn(ref output, state, verticalCommand, lateralCommand);
            else AimCoast(ref output, state, sink, lateral);

            output.Phase = phase;
            output.Valid = true;
            output.Throttle = burning ? throttle : 0;
            output.VerticalAcceleration = burning ? verticalCommand : 0;
            output.LateralAcceleration = burning ? lateralCommand : 0;
            output.TargetSink = target;
            output.FreeAcceleration = free;
            output.TiltDegrees = Math.Atan2(Math.Abs(lateralCommand), Math.Max(1e-9, verticalCommand)) * 180 / Math.PI;
            output.TerminalTiltLimit = tiltLimit * 180 / Math.PI;
            output.DampingGain = gain;
            if (prediction != null && prediction.Valid)
            {
                output.IgnitionAltitude = prediction.IgnitionAltitude;
                output.IgnitionMargin = prediction.SpeedMargin;
                output.PredictedTouchdownSpeed = prediction.TouchdownSpeed;
                output.RequiredDeltaV = prediction.RequiredDeltaV;
                output.BurnSeconds = prediction.BurnSeconds;
                output.Unstoppable = prediction.Unstoppable;
            }
            LastStep = output;
            return output;
        }

        // The allowed lean, as an angle from local vertical. It fades to nothing inside the
        // terminal band, which is what produces the "last 50 m straight down" of the profile
        // without needing a second controller for it.
        private double TiltLimit(double clearance, double free)
        {
            double degrees = clearance > config.Terminal.Altitude
                ? config.Terminal.HighAltitudeTiltDegrees : config.Terminal.MaxTiltDegrees;
            if (clearance < config.Terminal.Altitude && config.Terminal.Altitude > 0)
                degrees *= Math.Max(0.05, clearance / config.Terminal.Altitude);
            // A vehicle with no thrust above gravity cannot afford to lean at all.
            if (free <= 0.01) degrees = Math.Min(degrees, 2);
            return degrees * Math.PI / 180;
        }

        // Coasting: retrograde, because a booster presents its smallest cross-section backwards and
        // every degree of angle of attack costs drag and heating. Below the minimum steering speed
        // the velocity direction stops meaning anything - a 2 m/s residual wanders by tens of
        // degrees per tick - so the guidance asks for straight up instead of chasing it.
        private void AimCoast(ref GuidanceStep output, DescentState state, double sink, double lateral)
        {
            if (phase == DescentPhase.Terminal || phase == DescentPhase.Touchdown
                || state.Clearance <= config.Terminal.Altitude)
            {
                output.Up = 1; output.East = 0; output.North = 0;
                return;
            }
            double speed = Math.Sqrt(sink * sink + lateral * lateral);
            // Only a real climb counts. A booster entering shallowly - falling at a few m/s while
            // crossing the sky at hundreds - is descending, and pointing it "up" would aim it along
            // its own velocity instead of against it: the airstream then hits the broadside, the
            // drag the alignment was meant to win is lost, and the thrust points the wrong way. The
            // threshold is the touchdown speed, so this only reacts to a genuine bounce.
            bool climbing = sink < -config.Terminal.TouchdownSpeed
                && (phase == DescentPhase.Align || phase == DescentPhase.Idle);
            if (climbing || speed < config.Control.MinimumSteeringSpeed)
            {
                output.Up = 1; output.East = 0; output.North = 0;
                return;
            }
            // Retrograde, component by component, and that means the NEGATIVE of the velocity
            // vector: the nose - and with it the engine axis - points backwards along the path, so
            // the engine section flies into the airstream. The velocity in this frame is
            // (-sink, +East, +North), so the aim is (+sink, -East, -North).
            //
            // This had every sign the wrong way round and so aimed the booster exactly PROGRADE. It
            // is worth spelling out why that was not visible in the numbers: the attitude controller
            // did its job perfectly and reported an attitude error of zero degrees, because the
            // booster really was pointing where the law asked. What it asked for was the one
            // direction it must not fly, and the alignment gate - which wants retrograde or upright
            // - then never opened, so the entry phase never began at all.
            output.Up = sink / speed;
            output.East = -state.VelocityEast / speed;
            output.North = -state.VelocityNorth / speed;
        }

        // Burning: along the net acceleration command, expressed in the local horizon frame.
        //
        // The vertical command is already "up", so its sign is the direction. The lateral command is
        // not: it is the DECELERATION of the drift, and `LateralDirection` hands back the direction
        // the booster is drifting TOWARDS. Using the lateral command as it stands therefore aimed the
        // engine along the drift and added to it. That is not a subtle sign: the first flight with a
        // fast entry burned 1900 m/s of sideways speed up to 3679 m/s while climbing away from the
        // planet, with the booster dutifully pointing at the attitude it was given. Hence the minus,
        // on both the aim and the acceleration it reports.
        private void AimBurn(ref GuidanceStep output, DescentState state, double vertical, double lateral)
        {
            if (Math.Abs(lateral) < 1e-9)
            {
                output.Up = 1; output.East = 0; output.North = 0;
                output.AccelerationUp = vertical;
                output.AccelerationEast = 0; output.AccelerationNorth = 0;
                return;
            }
            double fx, fy, fz;
            state.LateralDirection(out fx, out fy, out fz);
            double east = -lateral * (state.EastX * fx + state.EastY * fy + state.EastZ * fz);
            double north = -lateral * (state.NorthX * fx + state.NorthY * fy + state.NorthZ * fz);
            double scale = Math.Sqrt(vertical * vertical + east * east + north * north);
            output.AccelerationUp = vertical;
            output.AccelerationEast = east;
            output.AccelerationNorth = north;
            if (scale < 1e-9)
            {
                output.Up = 1; output.East = 0; output.North = 0;
                return;
            }
            output.Up = vertical / scale;
            output.East = east / scale;
            output.North = north / scale;
        }

        private void Advance(double time, double clearance, bool aligned, double cutoff)
        {
            switch (phase)
            {
                case DescentPhase.Idle:
                    // A booster that is still accelerating upwards with its upper stage is not a
                    // descender yet, and one whose readings are younger than a few seconds belongs
                    // to a vessel the game has only just created.
                    if (time >= config.Control.MinimumFlightSeconds) phase = DescentPhase.Align;
                    break;
                case DescentPhase.Align:
                    if (aligned) phase = DescentPhase.Entry;
                    break;
                case DescentPhase.Entry:
                    // The burn is not entered here. Ignition is decided by the profile check in
                    // Step, which is the only place that knows how much height is left; letting a
                    // second rule also start the burn is how the two ended up disagreeing.
                    break;
                case DescentPhase.Burn:
                    if (clearance <= config.Terminal.Altitude) phase = DescentPhase.Terminal;
                    if (clearance <= cutoff) phase = DescentPhase.Touchdown;
                    break;
                case DescentPhase.Terminal:
                    if (clearance <= cutoff) phase = DescentPhase.Touchdown;
                    break;
            }
        }

        // Called by the adapter when KSP reports the vessel down, so the phase matches the game.
        public void MarkLanded(bool crashed)
        {
            phase = crashed ? DescentPhase.Abort : DescentPhase.Done;
            aborted = crashed;
            lastThrottle = 0;
            lastTargetSink = 0;
        }

        private void Remember(double lateralAcceleration, double throttle, double targetSink)
        {
            lastLateralAcceleration = lateralAcceleration;
            lastThrottle = throttle;
            lastTargetSink = targetSink;
            hasLast = true;
        }

        private static double Clamp(double value, double low, double high)
        {
            return value < low ? low : value > high ? high : value;
        }
    }
}
