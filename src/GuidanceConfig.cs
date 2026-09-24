using System;

namespace BoosterWatch.Guidance
{
    // Every input the landing law uses, in KSP units. The same object is used by the flight
    // adapter and by the tests, so a test that lands a booster lands exactly the trajectory the
    // game would fly.
    public sealed class DescentConfig
    {
        public sealed class TerminalSettings
        {
            internal TerminalSettings Copy() { return (TerminalSettings)MemberwiseClone(); }
            // Target sink rate for the last metres and the vertical speed the vehicle aims to
            // touch down with. The user's number: 5 m/s.
            public double TouchdownSpeed = 5;
            // Height of the hull bottom above the ground at which the vertical final phase begins.
            public double Altitude = 50;
            // Below this height the sink target is blended down to SoftTouchdownSpeed so the
            // landing itself is softer than the approach.
            public double SoftFlareAltitude = 15;
            public double SoftTouchdownSpeed = 1.5;
            // Lateral speed still allowed at the moment of contact.
            public double LateralSpeed = 0.5;
            // Below this height nothing is spent on the drift any more and the booster is brought
            // upright. A booster lands on its legs, and a lean at contact puts the whole load on one
            // edge of the base - the recorded landing arrived with 3.7 m/s of drift still on it and
            // visibly tilted, which is this parameter's whole job.
            public double UprightAltitude = 20;
            // How far the guidance leans over at most, measured between the commanded thrust axis
            // and local vertical, at full descent authority.
            public double MaxTiltDegrees = 20;
            // The same limit while the booster is still above the terminal band. Braking from
            // orbital speed needs a deeper lean than the last hundred metres.
            public double HighAltitudeTiltDegrees = 40;
            // Thrust is cut this far above the ground, with the settle fall below it. A slope under
            // the touchdown point raises it by the hull's horizontal reach times the slope, because
            // the booster lands on one edge of its base first.
            public double EngineCutoffAltitude = 1.5;
            // Horizontal reach of the hull [m], used for the slope allowance above. Three metres is
            // a typical booster radius; the flight log reports what was actually used.
            public double HullRadius = 3;
        }

        public sealed class PredictorSettings
        {
            internal PredictorSettings Copy() { return (PredictorSettings)MemberwiseClone(); }
            public double CaptureAltitude = 100;
            public double CaptureSpeed = 10;
            public double CaptureLateralSpeed = 2;
            // How much sideways speed above `CaptureLateralSpeed` may still be on the vehicle when it
            // reaches the capture altitude. The gate is narrow on purpose: it is what keeps the
            // ignition early for an entry that arrives with its speed mostly sideways, and early is
            // what makes those entries work at all.
            //
            // Widening it to 4 m/s was tried on 24.09.2026 (0.9.16) and reverted the same day. With
            // the room, the planner takes the latest burn its trial accepts: 2683 m and 23 s instead
            // of 16793 m and 152 s, with the same soft touchdown predicted both times. But the trial
            // applies the commanded thrust along the commanded axis, and this vehicle cannot hold that
            // axis above about a kilometre - the airstream pins it retrograde and the flight log shows
            // 50 to 70 degrees of attitude error against a near-vertical command. The authority the
            // trial assumes is not there, so the vehicle reaches the flare far too fast. The flight of
            // 09:07 was decided by that defect either way (its plan was a best-effort burn at 4121 m
            // and it hit the ground at 111 m/s), but the wide gate moved the two entries that do land
            // onto exactly that unproven late burn, for a propellant saving that is not worth a
            // landing. What the gate really needs is a trial that models the axis the vehicle can
            // hold; until there is one, this is the safer number.
            public double CaptureLateralTolerance = 0.5;
            public double UpdateInterval = 0.5;
            // Integration step of the remaining-descent forecast [s].
            public double TimeStep = 0.05;
            public double MaxSeconds = 400;
            // The engine does not deliver thrust the instant it is told to. Both delays are
            // modelled in the forecast, which is what makes it ask for a higher ignition
            // altitude than a vacuum formula would.
            public double IgnitionDelay = 0.5;
            public double ThrustRampSeconds = 0.7;
            // Speed still allowed at the cut-off height when the forecast is solved backwards.
            public double TargetTouchdownSpeed = 5;
            // Samples in the coarse sweep over the ignition altitude.
            public int CoarseSamples = 20;
            public int BisectionSteps = 10;
            // Thrust is assumed to be off during the coast, because the guidance flies coast
            // phases retrograde with the engines dark.
            public double CoastSteeringLoss = 1.0;
            public double BurnSteeringLoss = 0.985;
            // How much of the hull's drag the forecast may count on. Drag always opposes the
            // velocity, so it always helps - but a burning booster leans to steer, and not all of
            // its cross-section stays presented to the airstream.
            public double DragEffectiveness = 0.8;
            // The whole law keeps this much of the engines' acceleration in reserve for attitude
            // control, spool-up error and a forecast that is one second off.
            public double ThrustReserve = 0.2;
            // Only used to let the forecast empty the tanks while it brakes. The thrust curve
            // itself comes from the vehicle, not from this number.
            public double SpecificImpulse = 310;
        }

        public sealed class ControlSettings
        {
            internal ControlSettings Copy() { return (ControlSettings)MemberwiseClone(); }
            public double LateralTimeConstant = 1.2;
            // Bell under the braking command: the descent profile is a ladder whose coefficient is
            // this fraction of the ballistic fall (capped at 1, see DescentGuidance.TargetSink).
            // Higher means a steeper profile, a faster fall and a later, harder brake.
            //
            // Measured against the point-mass model: 0.05 flies a clean landing, 0.10 lands softly
            // as well and holds the profile more tightly, 0.20 arrives at 24 m/s, and above 1 the
            // ladder outruns free fall and a booster that separates high up climbs instead of
            // landing. 0.35 is the value the entry profile needs; it stays under the ballistic fall
            // at every altitude.
            public double ProfileFraction = 0.05;
            // The margin by which the forecast's arrival speed may exceed the target before the
            // burn is due. Together with the forecast this is the ignition trigger.
            public double IgnitionSpeedMargin = 20;
            // The arrival speed at which the coast is abandoned because the descent can no longer be
            // stopped at all. It used to be "two metres per second over target", which is not an
            // emergency: it cancelled the coast on a predicted arrival of 7 m/s and lit the engines in
            // the stratosphere. 40 m/s is a crash by any standard and by then the burn is genuinely
            // the last chance.
            public double CoastAbortSpeed = 40;
            // Reserve on the braking DISTANCE, not on the arrival speed. A booster falling fast has
            // almost no height between "the burn would still arrive gently" and "too late": measured
            // on a light 7.4 t rocket, the forecast ordered the burn at 2215 m with 314 m/s of sink,
            // and stopping that needs 3650 m at the engines it had. Igniting with this factor on the
            // braking distance covers the spool-up and the model being wrong.
            public double IgnitionReserve = 1.3;
            // How hard the law pulls the sink rate onto the ladder, per m/s of deviation and per
            // second. The "stop by the ground" term alone is far too patient: at 3 km with 204 m/s of
            // sink against a ladder of 80 it asks for 5.9 m/s^2, the vehicle converges slowly, and
            // the recorded landing arrived with 29 m/s still on it. Following a ladder means paying
            // for the deviation, not for the ground.
            public double LadderGain = 0.5;
            // The landing-burn profile: above this height the engines stay dark whatever the
            // arithmetic says, because the atmosphere is the brake and the propellant is for the
            // landing. Below it - and only below it - the profile can order a burn on its own.
            // Below this air density NOTHING is decided - no coast budget, no ignition. In near
            // vacuum the only honest answer is "keep falling": a forecast up there describes a burn
            // that starts now and runs to the ground, in air this thin that always reads as "arrives
            // too hard", and the law lit up in the stratosphere on the strength of it. 1e-3 kg/m^3 is
            // about 35 km on Kerbin, where the air a returning booster actually brakes in begins.
            // A nearly empty booster is assumed to manage at least this much acceleration, whatever
            // its part data says about it when it was heavy. The mass that matters for the END of the
            // burn is the dry one, and a booster that looks unable to brake at all while full of
            // propellant - 12 m/s^2 of thrust against 9.8 of gravity - brakes perfectly well once the
            // tanks are nearly empty. Without this the law refused to plan a burn for exactly the
            // boosters that need one.
            public double EmptyBrakingFloor = 25;
            // Hard ceiling for the burn, in metres above ground. Above it the engines stay dark
            // whatever any prediction says.
            //
            // The physics gate below (the air must be braking at least half of gravity) is the useful
            // rule and it is per vehicle - but it is still a MODEL, and a model that is wrong lights
            // the engines at 30 km. Measured: the booster burned its whole landing propellant on the
            // way down, arrived at 760 m with dry tanks and hit at 110 m/s. This is the guard that
            // cannot be argued with, and it costs nothing: no booster has ever needed to brake in the
            // stratosphere on the way to a landing.
            public double BurnCeiling = 20000;
            public double PlanningDensity = 1e-2;
            public double CoastFloor = 200;
            // The last resort: inside this height a sink rate above the profile is ignited on sight,
            // without asking the forecast.
            public double IgnitionBackstop = 100;
            // While the booster is still crossing the sky it coasts and lets drag brake it. The
            // phase ends at this height above ground whatever else is true, because below it there
            // is no longer enough air to do the work and the descent has to own the vehicle.
            //
            // Keep it low. The band has to sit under the height at which an ordinary descent starts
            // its brake, or the coast would end before such a booster has begun and force it into a
            // long burn from altitude - measured: with the band at 2000 m a booster that should have
            // landed at 1.8 m/s arrived at 15.8 m/s, because its profile was never flown.
            public double CoastMargin = 0.5;
            // Rate of descent used to work out how much time the remaining height buys when the
            // booster is climbing or barely sinking [m/s].
            public double CoastTimeReferenceSpeed = 50;
            public double LateralDampingGain = 1.0;
            // Floor under the sideways authority [m/s^2]. The tilt limit scales with the braking
            // budget and collapses when the booster is falling fast, which would leave a crosswind
            // drift uncorrected for the whole descent. This keeps a small amount of tilt available
            // at every point.
            public double MinimumLateralAcceleration = 3.0;
            // A forecast that still arrives this much too fast is a crash with no manoeuvre left.
            public double AbortTouchdownMargin = 2.0;
            // Slew limits per physics step, so no command can jump between two ticks.
            public double MaxLateralAccelerationChange = 2.5;
            // Below this speed there is no direction that means anything, so the guidance points
            // up. This single line is what removed the "88 degrees at 103 m" of the old chain.
            public double MinimumSteeringSpeed = 2;
            // Before this many seconds of flight no descent state is trusted. A vessel KSP has
            // just created reports motion that does not match its position.
            public double MinimumFlightSeconds = 3;
        }

        public readonly TerminalSettings Terminal = new TerminalSettings();
        public readonly PredictorSettings Predictor = new PredictorSettings();
        public readonly ControlSettings Control = new ControlSettings();

        public DescentConfig() { }
        private DescentConfig(DescentConfig source)
        {
            Terminal = source.Terminal.Copy();
            Predictor = source.Predictor.Copy();
            Control = source.Control.Copy();
            // No adapter delegates: a background prediction uses only sampled tables.
        }
        internal DescentConfig Snapshot() { return new DescentConfig(this); }

        // Air density used by the free predictor. The adapter replaces it with KSP's own
        // pressure profile at the booster's position; the default is Kerbin-like.
        public Func<double, double> AirDensity;
        // Gravitational acceleration at an altitude above the datum [m/s^2].
        public Func<double, double> Gravity;

        public double SampleAirDensity(double altitude)
        {
            if (AirDensity != null) return AirDensity(altitude);
            return DefaultAirDensity(altitude);
        }

        public double SampleGravity(double altitude)
        {
            if (Gravity != null) return Gravity(altitude);
            return 9.81;
        }

        // Kerbin's lower atmosphere, rho0 = 1.225 kg/m^3 and a 5600 m scale height. Only ever
        // used when no profile was supplied, i.e. in the tests.
        public static double DefaultAirDensity(double altitude)
        {
            if (altitude > 70000) return 0;
            return 1.225 * Math.Exp(-Math.Max(0, altitude) / 5600.0);
        }

        public static DescentConfig Default()
        {
            return new DescentConfig();
        }
    }
}



