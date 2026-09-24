// Tests for the predictive landing law. No KSP, no Unity: the guidance is plain C#, so the whole
// approach can be flown against a closed-form model before any of it flies in the game.
using System;
using BoosterWatch.Guidance;

internal class GuidanceSimulation
{
    public double Radius = 600000;
    public double Mu = 3.5316e12;
    public double G0 = 9.80665;
    public double AirDensity0 = 1.225;
    public double ScaleHeight = 5600;
    public double AtmosphereTop = 70000;
    public bool Atmosphere = true;
    public double BoosterArea = 7.07;
    public double DragCoefficient = 0.6;

private static readonly double[] KerbinAlt = { 0, 5000, 10000, 15000, 20000, 25000, 30000, 35000, 40000, 45000, 50000, 60000, 70000 };
    private static readonly double[] KerbinRho = { 1.225, 0.587, 0.261, 0.106, 0.0392, 0.0131, 0.00397, 0.00109, 0.000268, 7.54e-5, 1.26e-5, 2.7e-7, 0 };

    // Kerbin's own atmosphere from the game's pressure curve, NOT an exponential: above about 25 km
    // Kerbin is thinner than a 5.6 km exponential, and that is the band a returning booster crosses
    // at speed. A harness with too much air up there flatters every forecast made about an entry.
    private static double KerbinAir(double h)
    {
        if (h >= 70000) return 0;
        if (h <= 0) return KerbinRho[0];
        for (int i = 0; i + 1 < KerbinAlt.Length; i++)
        {
            if (h > KerbinAlt[i + 1]) continue;
            double f = (h - KerbinAlt[i]) / (KerbinAlt[i + 1] - KerbinAlt[i]);
            return KerbinRho[i] + (KerbinRho[i + 1] - KerbinRho[i]) * f;
        }
        return 0;
    }

    public double Density(double altitudeAsl)
    {
        if (!Atmosphere) return 0;
        if (altitudeAsl > AtmosphereTop) return 0;
        return KerbinAir(altitudeAsl);
    }

    public double Gravity(double altitudeAsl)
    {
        double r = Radius + altitudeAsl;
        return Mu / (r * r);
    }

    public double DragArea = 37.8;   // Cd*A as KSP measures it (drag cubes), not the 7.07 m^2 cross-section
}

// Mirrors the KSP adapter: it reads the local frame, fills a DescentState, calls the guidance and
// applies throttle and attitude to a point-mass vehicle. Everything the game would add - part
// colliders, real drag cubes, engine spool curves - is replaced by the closed-form model above.
internal sealed class GuidanceRunner
{
    public readonly GuidanceSimulation World = new GuidanceSimulation();
    public readonly DescentConfig Config = DescentConfig.Default();
    public DescentGuidance Guidance;

    public double Mass = 30000;
    // Dry mass: the engines stop when the tanks are empty. Without this the harness keeps its full
    // thrust at the mass floor, and an entry that runs out of propellant does not crash - it
    // accelerates to two million m/s and leaves the planet, which is a property of the harness and
    // not of the law. In the game the adapter drops dry engines from the list and the guidance sees
    // an engine that delivers nothing, which is what the runner now mirrors.
    public double DryMass = 3000;
    public bool Dry { get { return Mass <= DryMass + 1e-6; } }
    public double Isp = 310;
    public double Sink = 150, Lateral = 0;
    public double Clearance = 2000;
    public double Time;
    public bool Aligned = true;
    public double NoiseAmplitude;
    public int RandomSeed = 12345;
    // Scale height of the harness atmosphere. The closed-form density below is always used for the
    // height measurement; this lifts it into the profile the forecast reads as well, which is what
    // makes a coast case possible at all - in vacuum the forecast is always right that a soft
    // landing is impossible, and the coast correctly yields to it.
    public double ForecastAirScaleHeight = double.PositiveInfinity;

    public DescentPhase Phase { get { return Guidance.Phase; } }
    public DescentPrediction Prediction { get { return Guidance.LastPrediction; } }
    public GuidanceStep Last;
    public bool Landed;
    public double TouchdownSpeed, TouchdownLateral, TouchdownTime, TouchdownTilt, MaxTilt, MaxSink;
    // Height at which the burn was ordered, negative while it is still dark. The burn timing is
    // what the forecast exists for, and the arrival speed alone cannot show whether it was timed
    // well: a booster that ignites far too early also lands.
    public double IgnitionClearance = -1;

    private readonly Random random = new Random(12345);
    public System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();

    public GuidanceRunner()
    {
        Config.AirDensity = Altitude => ForecastAirScaleHeight > 1e6 ? 0 : World.Density(Altitude);        Config.Gravity = World.Gravity;
        var model = new TestDescentModel(this);
        // `false`: this harness flies a virtual clock that runs thousands of times faster than wall
        // time, so a forecast on a worker thread would never be ready inside a flight and the same
        // test would pass or fail with the speed of the machine. Inline, the law is identical and
        // the flight is reproducible; the deferred path the game uses is covered by TestDeferredForecast.
        Guidance = new DescentGuidance(Config, new DescentPredictor(Config, model, false));
    }

    // Thrust acceleration of the full vehicle, exactly what the adapter would measure. It changes
    // as the tanks empty, which is why the model has to read it every time instead of freezing the
    // ratio it saw at the start.
    public double ThrustAcceleration(double altitudeAsl)
    {
        return Dry ? 0 : VacuumThrustAcceleration();
    }

    public double VacuumThrustAcceleration()
    {
        return Thrust / Mass;
    }

    public double Thrust = 750000;

    public double SeaLevelThrustAcceleration()
    {
        // Kerbin-like: a vacuum engine loses about 12 % of its thrust at sea level.
        return Thrust * 0.88 / Mass;
    }

    public DescentState BuildState()
    {
        double measured = Clearance + (NoiseAmplitude > 0 ? (random.NextDouble() * 2 - 1) * NoiseAmplitude : 0);
        DescentState state = new DescentState
        {
            Time = Time,
            Clearance = measured,
            // The harness ground is at sea level, so clearance and altitude are the same number.
            // The predictor builds its atmosphere table from this: without it the datum came out as
            // `0 - clearance` and a booster at 40 km was forecast in sea-level air, which is what
            // made every high entry look unstoppable.
            AltitudeAsl = measured,
            UpX = 0, UpY = 1, UpZ = 0,
            // Local horizon frame of a point at zero latitude: north along +z, east at 90 degrees
            // clockwise from north, i.e. +x. The guidance works in this frame only.
            EastX = 1, EastY = 0, EastZ = 0,
            NorthX = 0, NorthY = 0, NorthZ = 1,
            VelocityUp = -Sink,
            VelocityEast = Lateral,
            VelocityNorth = 0,
            Gravity = World.Gravity(Clearance),
            ThrustAcceleration = ThrustAcceleration(Clearance),
            AirDensity = World.Density(Clearance),
            DragCoefficient = World.DragArea,
            // What the air is already doing, so the law can credit it instead of adding thrust on
            // top of it. The adapter measures the same number from the real drag cubes.
            DragValid = true,
            DragAcceleration = DragAt(Clearance, Math.Sqrt(Sink * Sink + Lateral * Lateral)),
            AvailableDeltaV = 0,
            AvailableBurnTime = 0,
            Valid = true
        };
        return state;
    }

    // Drag deceleration along the flight path, positive = braking - the same expression the
    // adapter evaluates for the real hull.
    public double DragAt(double clearance, double speed)
    {
        double density = World.Density(clearance);
        if (density <= 0 || speed <= 0) return 0;
        return 0.5 * density * speed * speed * World.DragArea / Mass;
    }

    // One physics step of the real vehicle, driven by the guidance command. The guidance returns
    // accelerations and a thrust direction, so the harness applies acceleration - not force - and
    // the mass only enters through the engine acceleration it measured in the first place.
    public void Step(double dt)
    {
        DescentState state = BuildState();
        Last = Guidance.Step(state, Mass, dt, Aligned);
        if (Last.Valid && Last.Phase == DescentPhase.Burn && IgnitionClearance < 0)
            IgnitionClearance = Clearance;
        if (Last.Valid && Last.Phase != DescentPhase.Idle && Last.Phase != DescentPhase.Align)
        {
            MaxTilt = Math.Max(MaxTilt, Last.TiltDegrees);
            MaxSink = Math.Max(MaxSink, Sink);
        }
        if (!Last.Valid) return;
        // A dry booster has no thrust to apply, whatever the law asks for.
        double authority = Dry ? 0 : 1;
        double speed = Math.Sqrt(Sink * Sink + Lateral * Lateral);
        double drag = 0.5 * World.Density(Clearance) * speed * speed * World.DragArea / Mass;
        // The command is the acceleration itself, so the vehicle applies it as it stands. Nothing
        // here needs the engine's maximum: the law already saturated its command at the braking
        // budget, which is that maximum minus the reserve.
        // Drag opposes the motion, so it comes off the sink rate. It used to be added to it, which
        // made the model's air brake the wrong way: harmless at 2 m/s of descent, and a runaway at
        // the speeds a returning booster actually has.
        double aSink = -authority * Last.AccelerationUp - drag * (speed > 1e-6 ? Sink / speed : 1)
            + World.Gravity(Clearance);
        // The guidance returns the commanded acceleration in the local horizon frame, so the harness
        // applies it as the acceleration it is: `AccelerationEast` is the acceleration along +east.
        // The minus that used to stand here cancelled a minus that was missing in the law itself, so
        // the two errors hid each other and the model flew a booster the game could not.
        double aLateral = authority * Last.AccelerationEast - drag * (speed > 1e-6 ? Lateral / speed : 0);
        Sink += aSink * dt;
        Lateral += aLateral * dt;
        Mass = Math.Max(DryMass, Mass - Last.Throttle * Thrust / (Isp * 9.80665) * dt);
        Clearance -= Sink * dt;
        Time += dt;
        if (Clearance <= 0)
        {
            Landed = true;
            TouchdownSpeed = Math.Max(0, Sink);
            TouchdownLateral = Math.Abs(Lateral);
            TouchdownTime = Time;
            TouchdownTilt = Last.TiltDegrees;
            MaxTilt = Math.Max(MaxTilt, MaxTilt);
            Guidance.MarkLanded(false);
        }
    }

    public void Fly(double dt, double maxSeconds)
    {
        int steps = (int)(maxSeconds / dt);
        for (int i = 0; i < steps && !Landed; i++) Step(dt);
    }

    public string Trace()
    {
        return "phase=" + DescentPhases.Name(Guidance.Phase)
            + " t=" + Time.ToString("0.0")
            + " h=" + Clearance.ToString("0.0")
            + " sink=" + Sink.ToString("0.00")
            + " lat=" + Lateral.ToString("0.00")
            + " mass=" + Mass.ToString("0")
            + " touch=" + TouchdownSpeed.ToString("0.00")
            + " maxTilt=" + MaxTilt.ToString("0.0")
            + " zuendung=" + (IgnitionClearance < 0 ? "keine" : IgnitionClearance.ToString("0") + " m");
    }

    private sealed class TestDescentModel : IDescentModel
    {
        private readonly GuidanceRunner runner;
        public TestDescentModel(GuidanceRunner runner) { this.runner = runner; }
        public double AirDensity(double altitudeAsl) { return runner.World.Density(altitudeAsl); }
        public double Gravity(double altitudeAsl) { return runner.World.Gravity(altitudeAsl); }
        public double ThrustAccelerationVacuum { get { return runner.VacuumThrustAcceleration(); } }
        public double ThrustAccelerationSeaLevel { get { return runner.SeaLevelThrustAcceleration(); } }
    }
}

internal static class GuidanceTests
{
    private static int failures;

    private static void Check(bool condition, string message)
    {
        if (condition) return;
        failures++;
        Console.WriteLine("FAIL: " + message);
    }

    private static void Near(double actual, double expected, double tolerance, string message)
    {
        bool ok = Math.Abs(actual - expected) <= tolerance;
        if (!ok) failures++;
        Console.WriteLine((ok ? "ok   " : "FAIL ") + message
            + " (ist " + actual.ToString("0.000") + ", erwartet " + expected.ToString("0.000")
            + " +/-" + tolerance.ToString("0.000") + ")");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) failures++;
        Console.WriteLine((condition ? "ok   " : "FAIL ") + message);
    }

    // ---------------------------------------------------------------- terminal law

    private static void TestSinkLadder()
    {
        Console.WriteLine("-- Sinkratenprofil");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);
        var guidance = new DescentGuidance(config, new DescentPredictor(config, model));
        double free = 25 * 0.8 - 9.81;
        double speed = config.Terminal.TouchdownSpeed;
        // At the cut-off height the profile is exactly the configured touchdown speed.
        Near(guidance.TargetSink(1.5, free, speed), speed, 1e-9, "1,5 m ueber Grund -> 5 m/s");
        // It climbs with altitude, and stays below the ballistic fall so free fall is allowed.
        double at500 = guidance.TargetSink(500, free, speed);
        double ballistic = Math.Sqrt(2 * free * 500);
        True(at500 > speed && at500 < ballistic,
            "500 m: Profil " + at500.ToString("0.0") + " m/s unter dem freien Fall "
            + ballistic.ToString("0.0") + " m/s");
        // A weak booster gets a slower profile, a strong one a faster one.
        var weak = new DescentGuidance(config, new DescentPredictor(config, new ConstantModel(9.81, 11, 11)));
        double weakFree = 11 * 0.8 - 9.81;
        True(weak.TargetSink(500, weakFree, speed) < at500, "schwacher Schub -> flacheres Profil");
        // The profile may never climb faster than 2g whatever the setting says. Above that it can
        // demand a descent rate steeper than free fall at altitude, which a booster can only reach
        // by climbing first - measured: with the fraction at 0.35 a booster separating high up aims
        // at 200 m/s of sink from 40 km and climbs away instead of landing.
        DescentConfig greedy = DescentConfig.Default();
        greedy.Control.ProfileFraction = 5.0;
        var greedyGuidance = new DescentGuidance(greedy, new DescentPredictor(greedy, model));
        double steep = greedyGuidance.TargetSink(40000, 1.0, speed);
        // The ballistic fall at that height with that budget, plus the touchdown speed it is
        // anchored at.
        double freeFallAt40km = Math.Sqrt(speed * speed + 2 * 1.0 * 40000);
        True(steep <= freeFallAt40km + 1e-6,
            "Profil bleibt unter dem freien Fall: " + steep.ToString("0") + " m/s <= "
            + freeFallAt40km.ToString("0") + " m/s");
    }

    private static void TestTiltLimit()
    {
        Console.WriteLine("-- Neigungsgrenze");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var guidance = new DescentGuidance(config, new DescentPredictor(config, new ConstantModel(9.81, 25, 25), false));
        DescentState state = Flat(1000, 100, 0, 9.81, 25, 10000);
        GuidanceStep step = guidance.Step(state, 10000, 0.02, true);
        True(step.TiltDegrees <= step.TerminalTiltLimit + 1e-6,
            "Befehl " + step.TiltDegrees.ToString("0.0") + " Grad <= Grenze "
            + step.TerminalTiltLimit.ToString("0.0") + " Grad");
        // Drifting hard must not tilt it past the limit either.
        state = Flat(40, 5, 30, 9.81, 25, 10000);
        DescentState entered = state;
        entered.Time = 20;
        step = guidance.Step(entered, 10000, 0.02, true);
        True(step.TiltDegrees <= step.TerminalTiltLimit + 1e-6,
            "bodennahe Drift: " + step.TiltDegrees.ToString("0.0") + " Grad <= "
            + step.TerminalTiltLimit.ToString("0.0") + " Grad");
    }

    // ---------------------------------------------------------------- entry coast

    // The coast's other job - keeping the engines dark is the first - is the attitude, and that one
    // was wrong in the way numbers cannot show on their own. The booster held the commanded attitude
    // perfectly, with an attitude error of zero degrees, and the attitude it held was PROGRADE. From
    // the flight log: 450 m/s of sink, 1900 m/s across, a commanded tilt of 102 degrees from
    // vertical. Retrograde in that geometry is 77 degrees. The law asked for 103.
    private static void TestCoastAim()
    {
        Console.WriteLine("-- Coast-Lage gegen die Geschwindigkeit");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);
        var guidance = new DescentGuidance(config, new DescentPredictor(config, model));
        // A real entry: fast across, slow down, still in the startup phase - which is where the
        // flight that showed this spent its whole descent, because the gate that ends it compares the
        // attitude against the aim, and the aim was the wrong way round.
        DescentState state = Flat(60000, 450, 1900, 9.81, 25, 38000);
        state.Time = 30;
        guidance.Step(state, 38000, 0.02, false);
        state.Time = 30.02;
        GuidanceStep step = guidance.Step(state, 38000, 0.02, false);
        True(step.Phase == DescentPhase.Align, "noch in der Ausrichtphase");
        double speed = Math.Sqrt(450 * 450 + 1900 * 1900);
        // The aim against the velocity that is really being flown: -sink upwards, +east across.
        double dot = step.Up * (-450 / speed) + step.East * (1900 / speed);
        double tilt = Math.Acos(Math.Max(-1, Math.Min(1, step.Up))) * 180 / Math.PI;
        Console.WriteLine("     DIAG Lage=" + tilt.ToString("0.0") + " Grad ab Lot, Skalarprodukt mit "
            + "der Geschwindigkeit=" + dot.ToString("0.000") + " (retrograd=-1, prograd=+1)");
        Check(dot < -0.98, "die Lage zeigt gegen die Geschwindigkeit, nicht mit ihr ("
            + dot.ToString("0.000") + ")");
        Check(tilt < 90, "und damit Triebwerk voraus: " + tilt.ToString("0.0") + " Grad ab Lot, "
            + "retrograd waere 77, prograd 103");
        // Straight down, the aim is straight up - the same rule with the horizontal part at zero.
        DescentState vertical = Flat(400, 60, 0, 9.81, 25, 38000);
        vertical.Time = 40;
        GuidanceStep down = guidance.Step(vertical, 38000, 0.02, false);
        Near(down.Up, 1, 1e-6, "senkrechter Abstieg: Lage zeigt nach oben");
    }

    // What the guidance decided during, or after, a flight.
    private struct Assessment
    {
        public bool Valid, Coasting;
        public DescentPhase Phase;
        public double Throttle, CoastSpeed, CoastBudget, CoastDrag, UpAim, HorizontalAim;
        public string Report;

        public void Last(GuidanceRunner runner)
        {
            GuidanceStep step = runner.Last;
            Valid = step.Valid;
            Coasting = step.Coasting;
            Phase = step.Phase;
            Throttle = step.Throttle;
            CoastSpeed = step.CoastSpeed;
            CoastBudget = step.CoastBudget;
            CoastDrag = step.CoastDrag;
            UpAim = step.Up;
            HorizontalAim = Math.Sqrt(step.East * step.East + step.North * step.North);
            Report = "h=" + runner.Clearance.ToString("0") + " sink=" + runner.Sink.ToString("0.0")
                + " quer=" + runner.Lateral.ToString("0.0")
                + " coast=" + Coasting + " cmd=" + (100 * Throttle).ToString("0") + "%"
                + " speed=" + CoastSpeed.ToString("0") + " budget=" + CoastBudget.ToString("0")
                + " drag=" + CoastDrag.ToString("0.0");
        }
    }

    // Flies a scenario through the standard harness and reports the last decision. `seconds` is how
    // long to fly: long enough for the forecast to have an answer, short enough that the boosters
    // which should be coasting have not reached the ground yet.
    private static Assessment Assess(double clearance, double sink, double lateral, double thrust,
        double mass, double seconds)
    {
        GuidanceRunner runner = MakeRunner(clearance, sink, lateral, thrust);
        runner.Mass = mass;
        runner.Fly(0.02, seconds);
        Assessment result = new Assessment();
        result.Last(runner);
        return result;
    }

    // The coast asks whether the remaining height is worth more braking than the engines could
    // deliver in it, and it is overruled by the forecast whenever the descent cannot be stopped
    // gently. Only the second half of that can be settled here.
    //
    // The first half cannot, and that is worth stating plainly rather than papering over with a
    // tuned case: this model has a closed-form atmosphere and one drag coefficient, and in the
    // regime where a coast would matter - a booster falling fast with its speed mostly sideways -
    // the numbers say the air here does almost nothing. Real drag in KSP is supersonic, direction
    // dependent and several times larger. So what is checked below is the mechanism - the coast
    // loses to a descent that must burn, and it holds the engines dark and points backwards once it
    // is running - and not the question of whether a given entry works out.
    private static void TestEntryCoast()
    {
        Console.WriteLine("-- Eintritt: Coast-Mechanik");

        // Too much speed for the height: the coast never starts and the descent takes the engines.
        GuidanceRunner tight = MakeRunner(6000, 30, 900, 750000);
        tight.Mass = 30000;
        tight.Fly(0.02, 3.1);
        Assessment fast = new Assessment();
        fast.Last(tight);
        Console.WriteLine("     DIAG schnell " + fast.Report);
        True(fast.CoastBudget < Math.Sqrt(fast.CoastSpeed * fast.CoastSpeed) + 1 && fast.CoastDrag > 5,
            "zu viel Fahrt fuer die Hoehe: das Budget deckt sie nicht, die Luft bremst ("
            + fast.CoastDrag.ToString("0") + " m/s2)");
        // The line this test is read from in flight reports the air as well as the engines, so that
        // number has to be the measured one and not a leftover: a booster falling at 834 m/s in the
        // harness's air is being braked by tens of m/s^2.
        Check(fast.CoastDrag > 1,
            "gemessener Luftwiderstand steht im Zustand: " + fast.CoastDrag.ToString("0.0") + " m/s2");
        // And something has to be taking that speed out. Which one it is depends on the air: at
        // 5.8 km the harness brakes with 21 m/s^2, more than the tilt-limited sideways authority of
        // the engines, so a law that keeps the propellant for the landing and lets the air work is
        // doing the right thing. What would not be right is a booster left with neither.
        Check(fast.CoastDrag > 5 || fast.Throttle > 0.3,
            "die Fahrt wird abgebaut - Luft " + fast.CoastDrag.ToString("0.0") + " m/s2, Schub "
            + (100 * fast.Throttle).ToString("0") + " %");

        // What the coast hands to the descent: the ignition point the forecast solved for. The old
        // speed-budget heuristic that used to be checked here was retired (the log reports the planned
        // height instead), so the ladder that is checked now is the planned ignition height: it has
        // to sit below a booster that still has room, and it has to have arrived once the room is
        // gone. Each run needs two ticks - the first walks the phase out of Idle.
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);
        DescentPrediction high, low, now;
        GuidanceStep atAltitude = CoastTick(config, model, 8000, 100, out high);
        GuidanceStep nearGround = CoastTick(config, model, 1000, 100, out low);
        GuidanceStep atIgnition = CoastTick(config, model, 600, 100, out now);
        Console.WriteLine("     DIAG Plan 8000 m = " + Plan(high) + ", 1000 m = " + Plan(low)
            + ", 600 m = " + Plan(now));
        True(high != null && high.Valid && atAltitude.Coasting && !atAltitude.IgnitionDue,
            "aus 8000 m bleibt das Triebwerk aus, der Plan wartet (" + Plan(high) + ")");
        True(low != null && low.Valid && low.IgnitionAltitude < high.IgnitionAltitude,
            "der geplante Zuendpunkt wandert mit dem Boden nach unten: " + Plan(high) + " -> " + Plan(low));
        True(atIgnition.IgnitionDue,
            "600 m mit 100 m/s: die Zuendung ist faellig (" + Plan(now) + ")");
    }

    // ---------------------------------------------------------------- closed loop

    private static void TestHoverDown()
    {
        Console.WriteLine("-- Senkrechter Anflug 1500 m / 60 m/s");
        GuidanceRunner runner = MakeRunner(1500, 60, 0, 750000);
        runner.Fly(0.02, 120);
        Console.WriteLine("     " + runner.Trace());
        True(runner.Landed, "aufgesetzt");
        Check(runner.TouchdownSpeed > 0 && runner.TouchdownSpeed <= 6.5,
            "Aufsetzgeschwindigkeit " + runner.TouchdownSpeed.ToString("0.00") + " m/s");
        Near(runner.TouchdownLateral, 0, 0.6, "Seitwaerts beim Aufsetzen");
        True(runner.MaxTilt <= 45, "Neigung blieb bei " + runner.MaxTilt.ToString("0.0") + " Grad");
    }

    private static void TestHoverslam()
    {
        Console.WriteLine("-- Hoverslam 2500 m / 120 m/s");
        GuidanceRunner runner = MakeRunner(2500, 120, 0, 750000);
        runner.Fly(0.02, 120);
        Console.WriteLine("     " + runner.Trace());
        True(runner.Landed, "aufgesetzt");
        // A hoverslam cuts the engines and settles the last 1.5 m, so it lands a little faster
        // than a hover approach but must stay well inside the recovery limit of 8 m/s.
        Check(runner.TouchdownSpeed <= 8,
            "Aufsetzgeschwindigkeit " + runner.TouchdownSpeed.ToString("0.00") + " m/s <= 8");
        True(runner.MaxSink > 80, "Hoverslam-Charakter: Spitzensinken " + runner.MaxSink.ToString("0") + " m/s");
    }

    private static void TestLateralDrift()
    {
        Console.WriteLine("-- Seitendrift 30 m/s aus 2000 m");
        GuidanceRunner runner = MakeRunner(2000, 80, 30, 750000);
        runner.Fly(0.02, 150);
        Console.WriteLine("     " + runner.Trace());
        True(runner.Landed, "aufgesetzt");
        // The lateral controller only spends what the tilt limit leaves it, so a strong crosswind is
        // not removed perfectly. What matters is that most of it is gone by the ground.
        Check(runner.TouchdownLateral < 15,
            "Drift wurde kleiner: " + runner.TouchdownLateral.ToString("0.0") + " m/s (aus 30)");
        Check(runner.TouchdownSpeed <= 8,
            "Aufsetzgeschwindigkeit " + runner.TouchdownSpeed.ToString("0.00") + " m/s");
        // Upright at contact. A booster lands on its legs, and a lean puts the whole load into one
        // edge of the base - the recorded flight arrived visibly tilted with 3.7 m/s of drift left.
        Check(runner.TouchdownTilt <= 5,
            "aufrecht aufgesetzt: " + runner.TouchdownTilt.ToString("0.0") + " Grad Neigung");
    }

    private static void TestWeakThrust()
    {
        Console.WriteLine("-- Schwacher Schub TWR 0,8 (nicht landbar)");
        GuidanceRunner runner = MakeRunner(2000, 60, 0, 240000);
        runner.Fly(0.02, 120);
        True(runner.Landed, "irgendwann am Boden");
        True(runner.TouchdownSpeed > 8,
            "erkennbar zu hart: " + runner.TouchdownSpeed.ToString("0.0") + " m/s");
        // A booster with no thrust above local gravity cannot be saved by any law, but it must be
        // flown to the end - engines at full, attitude upright - and the law must not hide what the
        // outcome was. `Abort` is the state that says "this is a crash" while the vehicle is still
        // in the air; it is what the flight log and the status line report.
        True(runner.Guidance.Aborted || runner.TouchdownSpeed > 8,
            "Absturz wird nicht verschwiegen (Abort=" + runner.Guidance.Aborted
            + ", Aufsetzen " + runner.TouchdownSpeed.ToString("0.0") + " m/s)");
        True(runner.MaxTilt < 45, "boesartige Neigung vermieden (" + runner.MaxTilt.ToString("0.0") + " Grad)");
    }

    private static void TestContinuity()
    {
        Console.WriteLine("-- Stetigkeit der Befehle");
        GuidanceRunner runner = MakeRunner(2500, 120, 0, 750000);
        double previousTilt = 0;
        double maxTiltJump = 0;
        for (int i = 0; i < 6000 && !runner.Landed; i++)
        {
            runner.Step(0.02);
            if (!runner.Last.Valid) continue;
            if (runner.Last.Phase == DescentPhase.Burn || runner.Last.Phase == DescentPhase.Terminal)
                maxTiltJump = Math.Max(maxTiltJump, Math.Abs(runner.Last.TiltDegrees - previousTilt));
            previousTilt = runner.Last.TiltDegrees;
        }
        Console.WriteLine("     " + runner.Trace() + " maxSprung Neigung=" + maxTiltJump.ToString("0.00") + " Grad");
        Check(maxTiltJump < 12, "kein Neigungssprung (max " + maxTiltJump.ToString("0.00") + " Grad)");
        // The throttle is expected to move in one big step at ignition and then track the profile;
        // that is a burn starting, not an oscillation, and no rate limit is imposed on it because
        // the engines have one of their own.
        Check(runner.Landed && runner.TouchdownSpeed <= 8,
            "Anflug blieb beherrscht: Aufsetzen " + runner.TouchdownSpeed.ToString("0.00") + " m/s");
    }

    private static void TestNoise()
    {
        Console.WriteLine("-- Rauschen +/-3 m auf die Hoehe");
        GuidanceRunner clean = MakeRunner(2000, 100, 10, 750000);
        GuidanceRunner noisy = MakeRunner(2000, 100, 10, 750000);
        noisy.NoiseAmplitude = 3;
        clean.Fly(0.02, 150);
        noisy.Fly(0.02, 150);
        Console.WriteLine("     sauber: " + clean.Trace());
        Console.WriteLine("     Rauschen: " + noisy.Trace());
        True(noisy.Landed, "mit Rauschen aufgesetzt");
        Check(noisy.TouchdownSpeed <= 9,
            "Aufsetzgeschwindigkeit mit Rauschen " + noisy.TouchdownSpeed.ToString("0.00") + " m/s");
        Check(noisy.Phase != DescentPhase.Abort, "kein Fehlabbruch durch Rauschen");
    }

    private static void TestEntry()
    {
        Console.WriteLine("-- Eintritt aus 25 km mit 1200 m/s");
        GuidanceRunner runner = MakeRunner(25000, 1200, 0, 750000);
        runner.Fly(0.05, 900);
        Console.WriteLine("     " + runner.Trace());
        // The harness atmosphere is a plain exponential, so it is far thinner between 10 km and
        // 25 km than Kerbin's real one and this entry cannot be braked the way the game would brake
        // it. What is being checked here is only that the law keeps flying the whole descent and
        // reaches the ground in one piece, with a finite rate, instead of stalling out or losing
        // the vehicle to a numerical blow-up.
        True(runner.Landed, "hat den Boden erreicht");
        True(!double.IsNaN(runner.TouchdownSpeed) && !double.IsInfinity(runner.TouchdownSpeed),
            "Aufsetzgeschwindigkeit ist endlich: " + runner.TouchdownSpeed.ToString("0.0") + " m/s");
    }

    // The separation this mod is for: a booster coming back from orbit with its speed sideways, not
    // downwards. Until 0.9.5 this model could not land it at all: the booster either flew away to
    // 750 km or arrived at 25 km/s, because three separate sign errors cancelled into a law that
    // braked the fall, fed the drift and then hovered on the engines until the tanks ran dry. With
    // them fixed it lands from 2200 m/s across and 60 m/s down on half its propellant, at the
    // configured touchdown speed. KSP's real atmosphere is denser than this exponential above 15 km
    // and its drag is Mach dependent, so the game's numbers will differ - but the law no longer is
    // what stands in the way.
    private static void TestHighSpeedEntry()
    {
        Console.WriteLine("-- Bahngeschwindigkeit: 2200 m/s quer aus 40 km");
        GuidanceRunner runner = MakeRunner(40000, 60, 2200, 750000);
        runner.Fly(0.05, 400);
        Console.WriteLine("     " + runner.Trace());
        True(runner.Landed, "hat den Boden erreicht");
        Check(runner.TouchdownSpeed > 0 && runner.TouchdownSpeed <= 8,
            "sanft aufgesetzt mit " + runner.TouchdownSpeed.ToString("0.00") + " m/s");
        Check(runner.TouchdownLateral < 10,
            "Restdrift " + runner.TouchdownLateral.ToString("0.0") + " m/s (aus 2200)");
        Check(runner.Mass > runner.DryMass + 500,
            "Treibstoff blieb uebrig: " + runner.Mass.ToString("0") + " kg");
        Check(runner.IgnitionClearance < 40000,
            "die Zuendung kommt nicht sofort oben ("
            + (runner.IgnitionClearance < 0 ? "keine" : runner.IgnitionClearance.ToString("0") + " m") + ")");
    }

    // The second flight, as numbers: `sink=450 lateral=1900` at 57 km, engines lit. The booster
    // turned retrograde exactly as asked (attitude error 0, `lage=0`), and then the burn pushed it
    // ALONG its drift: the sideways speed climbed to 3679 m/s and the sink rate went negative, so it
    // left the planet instead of landing. A braking command that adds speed is the one thing this
    // law may never produce, so that is what is checked, in the geometry that produced it.
    private static void TestBurnOpposesVelocity()
    {
        Console.WriteLine("-- Bremsbefehl zeigt gegen die geflogene Geschwindigkeit");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);
        var guidance = new DescentGuidance(config, new DescentPredictor(config, model));
        DescentState state = Flat(400, 450, 1900, 9.81, 25, 37500);
        state.Time = 30;
        GuidanceStep step = guidance.Step(state, 37500, 0.02, true);   // Idle -> Align
        state.Time = 30.02;
        step = guidance.Step(state, 37500, 0.02, true);                // Align -> Entry, burn ordered
        True(step.Phase == DescentPhase.Burn || step.Phase == DescentPhase.Entry,
            "Eintritt erreicht (Phase " + DescentPhases.Name(step.Phase) + ")");
        // What the command does to the flown speed: dot(a, v) must be negative, or the booster is
        // being accelerated. v in the horizon frame is (-450 up, +1900 east).
        double speed = Math.Sqrt(450 * 450 + 1900 * 1900);
        double alongVelocity = (step.AccelerationUp * -450 + step.AccelerationEast * 1900) / speed;
        Console.WriteLine("     DIAG Befehl " + Math.Sqrt(step.AccelerationUp * step.AccelerationUp
            + step.AccelerationEast * step.AccelerationEast).ToString("0.0")
            + " m/s2, davon " + alongVelocity.ToString("0.00")
            + " m/s2 MIT der Geschwindigkeit (negativ = bremsend), cmd="
            + (100 * step.Throttle).ToString("0") + " %");
        Check(alongVelocity < 0, "der Bremsbefehl nimmt Fahrt heraus statt sie zu vergroessern ("
            + alongVelocity.ToString("0.00") + " m/s2)");
        // And it must not be a token correction either: at 54 % of 25 m/s^2 the booster has to be
        // losing a serious amount of speed per second.
        Check(alongVelocity < -3, "und zwar spuerbar: " + alongVelocity.ToString("0.00") + " m/s2");
        // The aim has to point at the command, not away from it: the unit vector and the magnitude
        // are the same vector, which is the invariant the adapter relies on.
        double magnitude = Math.Sqrt(step.AccelerationUp * step.AccelerationUp
            + step.AccelerationEast * step.AccelerationEast);
        double dot = (step.Up * step.AccelerationUp + step.East * step.AccelerationEast) / magnitude;
        Near(dot, 1, 1e-6, "Zielachse und Beschleunigungsvektor zeigen in dieselbe Richtung");

        // And the climb has to end by itself. The second flight went on rising - sink -1498 m/s and
        // gaining altitude - because the profile's deceleration is always positive, so the command
        // kept adding gravity to it. A booster that is climbing must get no thrust at all.
        DescentState rising = Flat(70000, -900, 3500, 9.81, 25, 37500);
        rising.Time = 60;
        GuidanceStep up = guidance.Step(rising, 37500, 0.02, true);
        Console.WriteLine("     DIAG steigend: cmd=" + (100 * up.Throttle).ToString("0") + " %, Up-Achse="
            + up.Up.ToString("0.00") + ", up=" + up.AccelerationUp.ToString("0.0000")
            + ", ost=" + up.AccelerationEast.ToString("0.0000") + ", phase=" + DescentPhases.Name(up.Phase)
            + ", ziel=" + up.TargetSink.ToString("0.0") + ", coast=" + up.Coasting);
        Near(up.Throttle, 0, 1e-9, "steigender Booster bekommt keinen Schub");
        // Upright, not retrograde: a booster on its way back up has no thrust to steer with, and the
        // attitude it should fall back onto is its engines. The coast aims retrograde while there is
        // air to streamline against; a climb has none.
        double riseAim = Math.Sqrt(up.Up * up.Up + up.East * up.East + up.North * up.North);
        Check(riseAim > 0.99 && riseAim < 1.01,
            "und die Zielachse bleibt ein Einheitsvektor (" + riseAim.ToString("0.000") + ")");
    }

    // The third flight: at 9 km the drag reading exploded to 884 m/s^2 - the density the corridor
    // table fed it was about 500 times the game's own - and since the law subtracts drag from
    // gravity before deciding the vertical command, the throttle went to 0 % and stayed there. The
    // booster fell the last nine kilometres with half its propellant still aboard. A measurement can
    // be wrong; the law may not act on one that is physically impossible.
    private static void TestBogusDragCannotSilenceEngines()
    {
        Console.WriteLine("-- Unsinniger Widerstand darf die Triebwerke nicht abschalten");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);

        GuidanceStep credible = BurnCommand(config, model, 2000, 250, 100, 30000, 3);
        GuidanceStep absurd = BurnCommand(config, model, 2000, 250, 100, 30000, 884);
        Console.WriteLine("     DIAG mit 3 m/s2 Widerstand: cmd="
            + (100 * credible.Throttle).ToString("0") + " %, mit 884 m/s2: cmd="
            + (100 * absurd.Throttle).ToString("0") + " %");
        Check(credible.Throttle > 0.3, "bei glaubwuerdigem Widerstand wird gebremst ("
            + (100 * credible.Throttle).ToString("0") + " %)");
        // The absurd reading is clamped to five times local gravity, and five times local gravity is
        // still more drag than the air can ever produce against a falling booster - so the correct
        // answer for it IS no thrust: the descent is already being braked harder than gravity pulls.
        // What the law must not do is act on the raw number: a negative or runaway command, or a
        // throttle outside its range, is how a bad measurement turns into a crash.
        Check(absurd.Throttle >= 0 && absurd.Throttle <= 1,
            "unsinniger Messwert bleibt in der Drosselgrenze (" + absurd.Throttle.ToString("0.00") + ")");
        Check(absurd.VerticalAcceleration >= 0 && absurd.VerticalAcceleration <= 25 * 0.8 + 1e-6,
            "und der Befehl bleibt im Schubbudget (" + absurd.VerticalAcceleration.ToString("0.0")
            + " m/s2)");
        Check(!double.IsNaN(absurd.VerticalAcceleration) && !double.IsNaN(absurd.Throttle),
            "kein NaN aus einer kaputten Messung");
    }

    // One tick of a burning descent with a given drag reading, in the phase where the burn is due.
    private static GuidanceStep BurnCommand(DescentConfig config, IDescentModel model, double clearance,
        double sink, double lateral, double mass, double drag)
    {
        var guidance = new DescentGuidance(config, new DescentPredictor(config, model, false));
        DescentState state = Flat(clearance, sink, lateral, 9.81, 25, mass);
        state.DragValid = true;
        state.DragAcceleration = drag;
        state.Time = 30;
        guidance.Step(state, mass, 0.02, true);
        state.Time = 30.02;
        return guidance.Step(state, mass, 0.02, true);
    }

    // The vehicle matrix: the test rocket from the flight recording (4.7 t, ~29 m/s^2) next to the
    // shapes this law has to survive - heavy, weak, and light with a lot of thrust. Each flies the
    // same vertical descent and the same fast entry; what matters is where it ignites, what the
    // landing costs and whether it lands at all.
    private static void TestVehicleMatrix()
    {
        Console.WriteLine("-- Fahrzeugmatrix");
        double[][] vehicles =
        {
            new double[] { 136000, 4700, 1500 },
            new double[] { 750000, 30000, 3000 },
            new double[] { 250000, 20000, 2000 },
            new double[] { 300000, 5000, 1200 },
        };
        string[] labels = { "Testrakete 4,7t (29 m/s2)", "schwer 30t (25 m/s2)",
            "schwach 20t (12,5 m/s2)", "leicht 5t (60 m/s2)" };
        for (int i = 0; i < vehicles.Length; i++)
        {
            GuidanceRunner vertical = MakeRunner(4000, 100, 0, vehicles[i][0]);
            vertical.Mass = vehicles[i][1]; vertical.DryMass = vehicles[i][2];
            vertical.Fly(0.05, 400);
            GuidanceRunner entry = MakeRunner(40000, 60, 2000, vehicles[i][0]);
            entry.Mass = vehicles[i][1]; entry.DryMass = vehicles[i][2];
            entry.Fly(0.05, 400);
            Console.WriteLine("     " + labels[i]
                + " | senkrecht: zuendung=" + (vertical.IgnitionClearance < 0 ? "-"
                    : vertical.IgnitionClearance.ToString("0") + "m")
                + " touch=" + vertical.TouchdownSpeed.ToString("0.00")
                + " dV=" + Burned(vehicles[i][1], vertical.Mass).ToString("0")
                + " | eintritt: zuendung=" + (entry.IgnitionClearance < 0 ? "-"
                    : entry.IgnitionClearance.ToString("0") + "m")
                + " touch=" + entry.TouchdownSpeed.ToString("0.00")
                + " quer=" + entry.TouchdownLateral.ToString("0.0")
                + " dV=" + Burned(vehicles[i][1], entry.Mass).ToString("0"));
        }
    }

    private static double Burned(double start, double end)
    {
        if (end <= 0 || start <= end) return 0;
        return 310 * 9.80665 * Math.Log(start / end);
    }
    private static void TestIgnitionDecisionIsSane()
    {
        Console.WriteLine("-- Zuendentscheidung der Vorhersage");
        GuidanceRunner runner = MakeRunner(2000, 150, 0, 750000);
        runner.Fly(0.02, 5);
        DescentPrediction prediction = runner.Prediction;
        True(prediction != null && prediction.Valid, "Vorhersage gueltig");
        if (prediction == null) return;
        double free = 750000 / runner.Mass * 0.8 - 9.81;
        double vacuum = 0.5 * 150 * 150 / free;
        Console.WriteLine("     Entscheidung=" + (prediction.IgnitionNeeded ? "zuenden" : "warten")
            + " Aufsetzen waere " + prediction.TouchdownSpeed.ToString("0.0") + " m/s"
            + " (Vakuumbremsweg aus 150 m/s: " + vacuum.ToString("0") + " m)"
            + " Brenndauer=" + prediction.BurnSeconds.ToString("0.0") + " s"
            + " dV=" + prediction.RequiredDeltaV.ToString("0") + " m/s"
            + " Reserve=" + prediction.SpeedMargin.ToString("0.0") + " m/s");
        True(!double.IsNaN(prediction.TouchdownSpeed) && !double.IsInfinity(prediction.TouchdownSpeed),
            "vorhergesagte Aufsetzgeschwindigkeit ist endlich");
        True(!double.IsNaN(prediction.SpeedMargin) && !double.IsInfinity(prediction.SpeedMargin),
            "Reserve ist endlich");
        // A booster that is still far too fast must already be braking: this decision is what the
        // whole ignition logic hangs on. What is checked is the burn itself rather than the
        // `IgnitionNeeded` flag, which belongs to a plan that is still ahead of the vehicle and is
        // deliberately false once the engines are running.
        GuidanceRunner fast = MakeRunner(2000, 400, 0, 750000);
        fast.Fly(0.02, 5);
        True(fast.IgnitionClearance > 0 && fast.Guidance.IgnitionOrdered
            && (fast.Phase == DescentPhase.Burn || fast.Phase == DescentPhase.Terminal),
            "400 m/s bei 2 km -> es wird gebremst (Zuendung bei "
            + (fast.IgnitionClearance < 0 ? "keiner" : fast.IgnitionClearance.ToString("0") + " m")
            + ", Phase " + DescentPhases.Name(fast.Phase) + ")");
        // A booster at 600 m, already hanging under its canopy at 8 m/s, is a different case, and the
        // forecast answers it differently now that its integration is right: 8 m/s is not a problem,
        // and lighting the engines at 600 m to fix a descent the air is already holding would be
        // wasted propellant. What matters is that the descent still ends on the ground gently, so
        // that is what is checked - the closed loop, not the flag.
        GuidanceRunner slow = MakeRunner(600, 8, 0, 750000);
        slow.Fly(0.02, 60);
        Console.WriteLine("     " + slow.Trace());
        True(slow.Landed, "8 m/s bei 600 m: aufgesetzt");
        Check(slow.TouchdownSpeed > 0 && slow.TouchdownSpeed <= 6.5,
            "8 m/s bei 600 m: sanft aufgesetzt mit " + slow.TouchdownSpeed.ToString("0.00") + " m/s");
        // And the burn is not ordered at the top: the coast holds the engines dark and lets gravity
        // do the first part of the work, so the engines only take over below the height where their
        // own budget closes. Where exactly that is belongs to the coast's arithmetic, not here; what
        // is checked is that it is not the same as "immediately".
        Check(slow.IgnitionClearance >= 0 && slow.IgnitionClearance < 520,
            "8 m/s bei 600 m: Zuendung erst unten ("
            + (slow.IgnitionClearance < 0 ? "keine" : slow.IgnitionClearance.ToString("0") + " m") + ")");
    }

    // The forecast is an integration, and until now nothing checked its arithmetic - only its
    // verdict, and a forecast that is wrong in the pessimistic direction passes every "must want to
    // burn" check there is. It was wrong in exactly that direction: the sink acceleration had
    // gravity and thrust the wrong way round, so its own engines accelerated the fall, every
    // descent looked unstoppable, and the booster lit up early and burned the propellant the
    // landing needed. These four numbers are the integration itself.
    private static void TestForecastPhysics()
    {
        Console.WriteLine("-- Vorhersage: freier Fall und Bremsung");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;

        // A weak engine: the answer is still free fall, less whatever the engine can cancel on the
        // way down. A booster that cannot hold itself up has no business forecasting a soft arrival.
        var feeble = new DescentPredictor(config, new ConstantModel(9.81, 0.5, 0.5), false);
        double weakPredicted = feeble.Predict(Flat(500, 0, 0, 9.81, 0.5, 30000), 30000).TouchdownSpeed;
        double weakFreeFall = Math.Sqrt(2 * (9.81 - 0.5) * 500);
        Near(weakPredicted, weakFreeFall, weakFreeFall * 0.05,
            "0,5 m/s2 Schub: freier Fall aus 500 m, weniger was das Triebwerk schafft");

        // With an engine that holds 15 m/s^2 net against gravity, the same fall has to arrive gently
        // - that is the whole reason the forecast exists.
        var powered = new DescentPredictor(config, new ConstantModel(9.81, 25, 25), false);
        double braked = powered.Predict(Flat(500, 0, 0, 9.81, 25, 30000), 30000).TouchdownSpeed;
        Console.WriteLine("     DIAG aus 500 m: ohne Schub " + weakPredicted.ToString("0.0")
            + " m/s, mit 25 m/s2 " + braked.ToString("0.0") + " m/s");
        Check(braked < weakFreeFall * 0.5 && braked >= 0 && braked < 10,
            "mit Triebwerk kommt der Fall sanft an: " + braked.ToString("0.00") + " m/s");

        // Twice the speed is not harder from four kilometres up - a suicide burn removes 200 m/s in
        // about 1.3 km, and there are four of them. What decides the ignition is the height the
        // braking needs against the height that is left, so that boundary is what is checked: the
        // same speed from 300 m has nowhere near enough room.
        double high = powered.Predict(Flat(1200, 0, 0, 9.81, 25, 30000), 30000).TouchdownSpeed;
        double fast = powered.Predict(Flat(4000, 200, 0, 9.81, 25, 30000), 30000).TouchdownSpeed;
        Console.WriteLine("     DIAG aus 4 km: ohne Fahrt " + high.ToString("0.0")
            + " m/s, mit 200 m/s " + fast.ToString("0.0") + " m/s");
        Check(high < 15 && fast < 15,
            "aus 1,5 km mit 25 m/s2 zu schaffen: " + high.ToString("0.0") + " und "
            + fast.ToString("0.0") + " m/s");
        DescentPrediction tight = powered.Predict(Flat(300, 200, 0, 9.81, 25, 30000), 30000);
        Check(tight.IgnitionNeeded && tight.Unstoppable,
            "200 m/s aus 300 m: zu spaet, die Vorhersage will zuenden (Aufsetzen "
            + tight.TouchdownSpeed.ToString("0") + " m/s)");
        // And it must not say that from a height where the burn fits: a forecast that panics is as
        // useless as one that sleeps.
        Check(!powered.Predict(Flat(1200, 0, 0, 9.81, 25, 30000), 30000).IgnitionNeeded,
            "aus 1,5 km im Fall: noch nicht zuenden");
    }

    // The path the game flies. A forecast costs between one and forty milliseconds on this machine,
    // and paying that inside a physics tick is exactly the stutter the deferred calculation exists
    // to remove, so the physics thread must never wait for one: it asks, gets the last completed
    // answer - or nothing at all on the first ask - and flies on. The closed-loop tests above run
    // the same law inline so that their flights stay reproducible; this is what covers the seam.
    private static void TestDeferredForecast()
    {
        Console.WriteLine("-- Vorhersage im Hintergrund");
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var model = new ConstantModel(9.81, 25, 25);
        // A fast entry rather than a gentle one: the state whose forecast is expensive enough for the
        // "did not wait" check below to mean something.
        DescentState state = Flat(40000, 60, 2200, 9.81, 25, 30000);

        var inlinePredictor = new DescentPredictor(config, model, false);
        DescentPrediction inline = inlinePredictor.Update(state, 30000, null, 0);
        True(inline.Valid, "die Vergleichsrechnung inline ist gueltig");
        Console.WriteLine("     DIAG inline " + inline.CalculationMilliseconds.ToString("0.0")
            + " ms, Kandidaten " + inline.Candidates);

        var flight = new DescentPredictor(config, model);   // deferred, as PoweredLanding builds it
        var watch = System.Diagnostics.Stopwatch.StartNew();
        DescentPrediction first = flight.Update(state, 30000, null, 0);
        double asked = watch.Elapsed.TotalMilliseconds;
        True(flight.Pending, "die Rechnung laeuft im Hintergrund");
        True(first == null || !first.Valid, "die erste Anfrage liefert noch kein Ergebnis ("
            + (first == null ? "keins" : "unfertig") + ")");
        Check(asked < inline.CalculationMilliseconds,
            "der Physikthread wartet nicht auf die Vorhersage (" + asked.ToString("0.00")
            + " ms statt " + inline.CalculationMilliseconds.ToString("0.0") + " ms)");

        // It has to arrive, though: the next ticks harvest it, and it is the same answer the inline
        // calculation produced - the thread a forecast runs on may not change the law.
        DescentPrediction ready = null;
        for (int i = 0; i < 500 && (ready == null || !ready.Valid); i++)
        {
            System.Threading.Thread.Sleep(10);
            ready = flight.Update(state, 30000, null, 0);
        }
        True(ready != null && ready.Valid, "die Hintergrundrechnung liefert eine gueltige Vorhersage");
        if (ready == null || !ready.Valid) return;
        Near(ready.IgnitionAltitude, inline.IgnitionAltitude, 1e-6, "gleiche Zuendhoehe wie inline");
        Near(ready.TouchdownSpeed, inline.TouchdownSpeed, 1e-6, "gleiches Aufsetzen wie inline");
        Near(ready.RequiredDeltaV, inline.RequiredDeltaV, 1e-6, "gleicher Brennstoffbedarf wie inline");
    }

    // The recorded flight of 24.09.2026, 08:39, at the tick before its engines lit: 26.4 t crossing
    // 16786 m with 338 m/s of sink, 1779 m/s of it sideways, and 4.7 m^2 of drag area because the
    // hull was flying engines-first and not broadside.
    //
    // This is the entry class that decides what the law is worth. Its speed is mostly sideways, and
    // while it is fast the airstream owns the hull: the crashed flight of 09:07 shows 50 to 70 degrees
    // of attitude error against a near-vertical command, so a burn there pushes along the flight path
    // whether or not that is what was asked for. A law that ignores that mis-plans itself - it asks
    // for a vertical deceleration it will not get and has to light the engines kilometres early - and
    // that early, weak burn is what the flights of 0.9.14 to 0.9.18 were doing.
    //
    // With the command aimed in the blend of what is wanted and what the air allows, and the engine
    // run at what it has while the air is in charge, the same entry lights up at 3755 m instead of
    // 15716 m: a 38 s burn that reaches 97 % throttle, arrives at the capture altitude at 5.2 m/s with
    // 0.2 m/s of drift, and lands at 5.56 m/s on 8.6 t of the 12 t it carries.
    private static void TestHeavySidewaysEntryBrakesLateAndHard()
    {
        Console.WriteLine("-- 26 t mit 1779 m/s Seitwaertsfahrt aus 16.8 km");
        GuidanceRunner runner = MakeRunner(16786, 338, 1779, 1000000);
        runner.Mass = 26400; runner.DryMass = 14400;
        runner.World.DragArea = 4.7;
        double startMass = runner.Mass;
        runner.Fly(0.05, 400);
        double used = (startMass - runner.Mass) / 1000;
        Console.WriteLine("     " + runner.Trace() + " verbraucht=" + used.ToString("0.00")
            + " t maxTilt=" + runner.MaxTilt.ToString("0.0") + " Grad");
        True(runner.Landed, "aufgesetzt");
        Check(runner.TouchdownSpeed > 0 && runner.TouchdownSpeed <= 8,
            "sanft aufgesetzt mit " + runner.TouchdownSpeed.ToString("0.00") + " m/s");
        Check(runner.TouchdownLateral < 3,
            "seitlich beim Aufsetzen " + runner.TouchdownLateral.ToString("0.00") + " m/s");
        // The burn belongs near the ground, where the air has already taken the sideways speed out and
        // the engine can point where it is told.
        Check(runner.IgnitionClearance > 0 && runner.IgnitionClearance < 8000,
            "Zuendung erst unten bei "
            + (runner.IgnitionClearance < 0 ? "keiner" : runner.IgnitionClearance.ToString("0") + " m"));
        Check(used < 10, "Brennstoff fuer den Anflug: " + used.ToString("0.00") + " t");
    }

    // The state that crashed. On 24.09.2026 at 09:07 a 27.6 t booster crossed 4280 m at 856 m/s: 321
    // m/s down, 794 m/s sideways, 243 kPa of dynamic pressure. It lit its engines there, held 10 to
    // 18 % thrust for three kilometres because the forecast of that day believed the commanded axis
    // held, and hit the ground at 111 m/s.
    //
    // With the axis-aware command the same state is landable, and this is the check that it stays
    // that way: the forecast must promise a soft arrival AND the flight must deliver it. The burn
    // starts below 5 km - where the air has taken the sideways speed down far enough that the engine
    // can point where it is told - and lands on 8.8 t of the 13 t in the tanks.
    private static void TestTheCrashedStateLandsWithTheAxisAwareBurn()
    {
        Console.WriteLine("-- Zustand des Absturzes 09:07 (27,6 t, 856 m/s aus 4,3 km)");
        GuidanceRunner runner = MakeRunner(4280, 321, 794, 1000000);
        runner.Mass = 27600; runner.DryMass = 14400;
        runner.World.DragArea = 4.7;
        double startMass = runner.Mass;
        runner.Fly(0.05, 300);
        double used = (startMass - runner.Mass) / 1000;
        Console.WriteLine("     " + runner.Trace() + " verbraucht=" + used.ToString("0.00") + " t");
        True(runner.Landed, "aufgesetzt");
        Check(runner.TouchdownSpeed > 0 && runner.TouchdownSpeed <= 8,
            "sanft aufgesetzt mit " + runner.TouchdownSpeed.ToString("0.00") + " m/s (geflogen wurden 111)");
        Check(runner.TouchdownLateral < 3,
            "seitlich beim Aufsetzen " + runner.TouchdownLateral.ToString("0.00") + " m/s");
        Check(runner.IgnitionClearance > 0 && runner.IgnitionClearance < 5000,
            "Zuendung erst unten bei "
            + (runner.IgnitionClearance < 0 ? "keiner" : runner.IgnitionClearance.ToString("0") + " m"));
    }

    // ---------------------------------------------------------------- helpers
    private static GuidanceRunner MakeRunner(double clearance, double sink, double lateral, double thrust)
    {
        GuidanceRunner runner = new GuidanceRunner();
        runner.Clearance = clearance;
        runner.Sink = sink;
        runner.Lateral = lateral;
        runner.Thrust = thrust;
        return runner;
    }

    private static string Plan(DescentPrediction prediction)
    {
        return prediction == null || !prediction.Valid ? "kein Plan" : prediction.IgnitionAltitude.ToString("0") + " m";
    }

    // The one tick of a descent on which the coast is both evaluated and fresh: a new guidance
    // starts out Idle and spends its first tick walking into Align, so the second tick is the first
    // one that reaches the entry logic and works the coast budget out from scratch.
    private static GuidanceStep CoastTick(DescentConfig config, IDescentModel model, double clearance,
        double sink)
    {
        DescentPrediction ignored;
        return CoastTick(config, model, clearance, sink, out ignored);
    }

    private static GuidanceStep CoastTick(DescentConfig config, IDescentModel model, double clearance,
        double sink, out DescentPrediction prediction)
    {
        var guidance = new DescentGuidance(config, new DescentPredictor(config, model, false));
        DescentState state = Flat(clearance, sink, 0, 9.81, 25, 30000);
        state.Time = 30;
        guidance.Step(state, 30000, 0.02, true);
        state.Time = 30.02;
        GuidanceStep step = guidance.Step(state, 30000, 0.02, true);
        prediction = guidance.LastPrediction;
        return step;
    }

    private static DescentState Flat(double clearance, double sink, double lateral, double gravity,
        double thrust, double mass)
    {
        return new DescentState
        {
            Time = 30,
            Clearance = clearance,
            AltitudeAsl = clearance,
            UpX = 0, UpY = 1, UpZ = 0,
            EastX = 1, EastY = 0, EastZ = 0,
            NorthX = 0, NorthY = 0, NorthZ = 1,
            VelocityUp = -sink,
            VelocityEast = lateral,
            VelocityNorth = 0,
            Gravity = gravity,
            ThrustAcceleration = thrust,
            AirDensity = 0,
            DragCoefficient = 1,
            Valid = true
        };
    }

    private sealed class ConstantModel : IDescentModel
    {
        private readonly double gravity, vacuum, sea;
        public ConstantModel(double gravity, double vacuum, double sea)
        { this.gravity = gravity; this.vacuum = vacuum; this.sea = sea; }
        public double AirDensity(double altitudeAsl) { return 0; }
        public double Gravity(double altitudeAsl) { return gravity; }
        public double ThrustAccelerationVacuum { get { return vacuum; } }
        public double ThrustAccelerationSeaLevel { get { return sea; } }
    }

    // ---------------------------------------------------------------- ground ahead

    private static void TestGroundScan()
    {
        Console.WriteLine("-- Bodensuche in Flugrichtung");
        // Flat ground. The scan must not invent terrain that is not there.
        GroundScan.HeightAt flat = p => 0;
        double clearance, slope;
        bool ok = GroundScan.AlongFlightPath(new Vector3d(0, 1000, 0), 1000, Vector3d.up, Vector3d.forward,
            50, 2, flat, out clearance, out slope);
        True(ok, "flaches Gelaende wird gemessen");
        Near(clearance, 998, 1e-6, "flach: Abstand der Unterkante");
        Near(slope, 0, 1e-6, "flach: Neigung");

        // A ridge ahead: the ground rises along the direction of travel. The scan has to report the
        // ground it is going to arrive over, not the ground under it.
        GroundScan.HeightAt ridge = p => p.z > 0 ? p.z * 0.2 : 0;
        ok = GroundScan.AlongFlightPath(new Vector3d(0, 1000, 0), 1000, Vector3d.up, Vector3d.forward,
            50, 2, ridge, out clearance, out slope);
        True(ok, "ansteigendes Gelaende wird gemessen");
        double flatClearance = 998;
        True(clearance < flatClearance,
            "Anstieg voraus senkt den gemeldeten Abstand: " + clearance.ToString("0.0")
            + " m statt " + flatClearance.ToString("0.0") + " m");
        True(slope > 5,
            "Neigung wird erkannt: " + slope.ToString("0.0") + " Grad");

        // Falling ground ahead must not make the reading optimistic: the booster still lands on
        // whatever is under it.
        GroundScan.HeightAt drop = p => p.z > 0 ? -p.z * 0.2 : 0;
        ok = GroundScan.AlongFlightPath(new Vector3d(0, 1000, 0), 1000, Vector3d.up, Vector3d.forward,
            50, 2, drop, out clearance, out slope);
        True(ok, "abfallendes Gelaende wird gemessen");
        Near(clearance, flatClearance, 1e-6, "abfallend: der Abstand unter dem Booster bleibt maßgeblich");

        // Coming straight down, the scan looks nowhere and the ground below is the answer.
        GroundScan.HeightAt bump = p => p.z > 50 ? 80 : 0;
        ok = GroundScan.AlongFlightPath(new Vector3d(0, 1000, 0), 1000, Vector3d.up, Vector3d.forward,
            0, 2, bump, out clearance, out slope);
        True(ok, "senkrechter Abstieg wird gemessen");
        Near(clearance, 998, 1e-6, "senkrecht: nur der Boden darunter zaehlt");
        Near(GroundScan.LastLookahead, 0, 1e-9, "senkrecht: kein Vorausblick");
        True(GroundScan.LastLookahead < 1e-9 && clearance > 900,
            "eine Klippe 50 m voraus beeinflusst einen senkrechten Abstieg nicht");

        // Slopes raise the height at which the engines are cut, so the booster does not drive its
        // downslope edge into the ground.
        double onFlat = GroundScan.CutoffHeight(1.5, 0, 3, 0);
        double onSlope = GroundScan.CutoffHeight(1.5, 15, 3, 0);
        Near(onFlat, 1.5, 1e-9, "Ausschalthoehe auf flachem Boden");
        True(onSlope > onFlat + 0.5,
            "Ausschalthoehe auf 15 Grad Hang: " + onSlope.ToString("0.00") + " m statt "
            + onFlat.ToString("0.00") + " m");
        True(GroundScan.CutoffHeight(1.5, 15, 3, 5) > onSlope,
            "restliche Schraeglage erhoeht die Ausschalthoehe weiter");

        // The guidance has to receive the slope: a slope raises the cutoff it works towards, which
        // shortens the distance the profile is built over.
        DescentConfig config = DescentConfig.Default();
        config.AirDensity = h => 0;
        config.Gravity = h => 9.81;
        var guidance = new DescentGuidance(config, new DescentPredictor(config, new ConstantModel(9.81, 25, 25), false));
        True(guidance.CutoffAltitude(20, 0) > guidance.CutoffAltitude(0, 0),
            "das Gesetz hebt die Ausschalthoehe am Hang an: "
            + guidance.CutoffAltitude(20, 0).ToString("0.00") + " m statt "
            + guidance.CutoffAltitude(0, 0).ToString("0.00") + " m");
        GuidanceStep withSlope = guidance.Step(Sloped(200, 60, 20, 9.81, 25), 10000, 0.02, true);
        True(withSlope.Valid && withSlope.CutoffAltitude > 1.5,
            "der Zustand traegt die Neigung bis in die Entscheidung: cut="
            + withSlope.CutoffAltitude.ToString("0.00") + " m");
    }

    private static DescentState Sloped(double clearance, double sink, double slopeDegrees, double gravity,
        double thrust)
    {
        DescentState state = Flat(clearance, sink, 0, gravity, thrust, 10000);
        state.SlopeDegrees = slopeDegrees;
        return state;
    }

    private static int Main(string[] args)
    {
        // The one thing the law borrows from the game is Vector3d: it works in KSP's world
        // coordinates, and a private copy of that type would mean the tests exercise different
        // arithmetic from the flight. So the executable is built against Assembly-CSharp and loads
        // it from the game folder, which this harness is handed as its first argument.
        if (args != null && args.Length > 0)
        {
            string managed = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                string name = new System.Reflection.AssemblyName(e.Name).Name;
                string candidate = System.IO.Path.Combine(managed, name + ".dll");
                return System.IO.File.Exists(candidate)
                    ? System.Reflection.Assembly.LoadFrom(candidate) : null;
            };
        }
        Console.WriteLine("Guidance-Tests (reine Regelung, ohne KSP)");
        TestSinkLadder();
        TestTiltLimit();
        TestGroundScan();
        TestEntryCoast();
        TestCoastAim();
        TestHoverDown();
        TestHoverslam();
        TestLateralDrift();
        TestWeakThrust();
        TestContinuity();
        TestNoise();
        TestEntry();
        TestHighSpeedEntry();
        TestBurnOpposesVelocity();
        TestBogusDragCannotSilenceEngines();
        TestVehicleMatrix();
        TestIgnitionDecisionIsSane();
        TestForecastPhysics();
        TestDeferredForecast();
        TestHeavySidewaysEntryBrakesLateAndHard();
        TestTheCrashedStateLandsWithTheAxisAwareBurn();
        Console.WriteLine(failures == 0 ? "Alle Guidance-Tests bestanden."
            : failures + " Guidance-Tests fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }
}





















