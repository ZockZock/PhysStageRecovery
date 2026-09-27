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
            // Target sink rate for the last metres and the vertical speed the vehicle aims to touch
            // down with. The profile the whole descent is built on: full-thrust braking arrives at the
            // capture altitude with `CaptureSpeed` on the clock, and from there this is the number the
            // descent is tapered to by the time the engines are cut.
            public double TouchdownSpeed = 2;
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
            // The vertical speed the braking burn has to have reached by the capture altitude. From
            // there the terminal phase takes the descent over and tapers it to the touchdown speed, so
            // this is the number the whole burn is aimed at: the ignition point is the latest one from
            // which a full-thrust burn arrives here at exactly this speed, upright and without drift.
            public double CaptureSpeed = 5;
            // Sideways speed still allowed at the capture altitude. The burn aims at none of it; this
            // is the width of the gate the plan and the flight are measured against.
            public double CaptureLateralSpeed = 0.5;
            // How much sideways speed above `CaptureLateralSpeed` may still be on the vehicle when it
            // reaches the capture altitude. This is a gate on the *plan*, not on the landing: the burn
            // itself always aims at none of it, and a trial is only ever accepted when the same law
            // then flies it to a soft touchdown.
            //
            // It is what sets the ignition point for an entry that arrives with its speed mostly
            // sideways, and that is where its value is felt. Too tight and the plan is pushed
            // kilometres up: with 1 m/s, the flight of 24.09.2026 at 09:51 had every candidate below
            // 4.4 km rejected for the drift alone - the touchdown was predicted at 5.8 m/s for all of
            // them - so it lit up at 4.4 km and then modulated 48 s at 30 % thrust on the way down,
            // 7.9 t of propellant for a landing the same law flies on 4.0 t from 3.2 km.
            //
            // Widening the gate was tried once before (0.9.16) and reverted, because the trial of
            // that day applied the commanded thrust along the commanded axis and this vehicle cannot
            // hold that axis while it is fast: it planned a burn the vehicle could not fly. The trial
            // knows the axis now (AeroPinningPressure) and the command does too, which is what makes
            // the room usable. Measured against the recorded flights, with the gate at 4.5 m/s every
            // one of them still lands at 5.6 m/s with 0.2 to 0.5 m/s of drift left, including the
            // state that crashed on 09:07. Beyond 4 m/s nothing changes: the touchdown speed and the
            // flare, not the drift, decide the ignition from there on.
            public double CaptureLateralTolerance = 4;
            public double UpdateInterval = 0.5;
            // Integration step of the remaining-descent forecast [s].
            public double TimeStep = 0.05;
            public double MaxSeconds = 400;
            // The engine does not deliver thrust the instant it is told to. Both delays are
            // modelled in the forecast, which is what makes it ask for a higher ignition
            // altitude than a vacuum formula would.
            public double IgnitionDelay = 0.5;
            public double ThrustRampSeconds = 0.7;
            // Samples in the coarse sweep over the ignition altitude.
            public int CoarseSamples = 20;
            public int BisectionSteps = 10;
            public double BurnSteeringLoss = 0.985;
            // How much of the hull's drag the forecast may count on. Drag always opposes the
            // velocity, so it always helps - but a burning booster leans to steer, and not all of
            // its cross-section stays presented to the airstream.
            public double DragEffectiveness = 0.8;
            // Dynamic pressure above which the airstream decides where the hull points, not the
            // controller. A booster crossing the sky at entry speed cannot swing its engines to the
            // commanded axis: the air holds it in the relative wind, so the engine brakes along the
            // flight path instead of holding the descent up. A forecast that ignores this promises a
            // soft arrival the vehicle cannot fly, and the flight of 24.09.2026, 09:07 is what that
            // costs - a plan that read "touchdown 6 m/s" ended at 111 m/s.
            //
            // Measured on the flights of the same day: the 26.4 t booster held the commanded axis
            // again below about 21 kPa (0.3 deg of error at 738 m) and the 27.6 t one below about
            // 46 kPa (1.5 deg at 648 m), while above that their error equalled the angle between the
            // command and the flight path. 40 kPa sits between the two measurements, on the side
            // that keeps the forecast pessimistic - a forecast that is too gloomy only lights the
            // engines earlier, which is what lands these entries; one that is too optimistic flies
            // them into the ground.
            public double AeroPinningPressure = 40000;
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
            // So schnell dreht sich eine Stufe im Endanflug sicher [Grad/s] (Flug vom 26.09.2026: ~17 bei
            // 20 % Schub). CaptureBraking haelt die Schraeglage nie groesser, als sich bis zum Ende der
            // Seitenfahrt wieder aufrichten laesst.
            public double TurnRateDegrees = 10;
            // How hard the terminal phase pulls the sink rate back onto the profile below the capture
            // altitude, in seconds: the commanded deceleration is the error over this number. A short
            // value tracks the taper from the capture speed to the touchdown speed closely and asks
            // for more thrust while doing it; a long one lets the booster ride its capture speed down.
            // Half a second holds the taper with a metre per second of error at most.
            public double FlareResponse = 0.5;
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
            public double LateralDampingGain = 1.0;
            // Floor under the sideways authority [m/s^2]. The tilt limit scales with the braking
            // budget and collapses when the booster is falling fast, which would leave a crosswind
            // drift uncorrected for the whole descent. This keeps a small amount of tilt available
            // at every point.
            public double MinimumLateralAcceleration = 3.0;
            // Slew limits per physics step, so no command can jump between two ticks.
            public double MaxLateralAccelerationChange = 2.5;
            // Below this speed there is no direction that means anything, so the guidance points
            // up. This single line is what removed the "88 degrees at 103 m" of the old chain.
            public double MinimumSteeringSpeed = 2;
        }

        // Gleitflug im Sinkflug ohne Schub: die Stufe fliegt schraeg statt genau rueckwaerts.
        //
        // Rueckwaerts zeigt ein Booster der Luft nur seinen Boden - die kleinste Flaeche, die er hat
        // (im Flug vom 25.09.2026 Cd*A = 0,3). Schraeg angestellt faengt die Seitenwand Luft: bei 35
        // Grad etwa das Drei- bis Vierfache an Widerstand, und der Rumpf erzeugt etwas Auftrieb. Der
        // wird nach OBEN gelegt, damit die Stufe laenger in der dichteren Luft bleibt, statt steil
        // durchzufallen - das Prinzip des Steins auf dem Wasser, nur mit einem Rohr statt einer
        // flachen Scheibe: springen wird es nicht, aber es bremst mit Luft statt mit Treibstoff.
        //
        // Vor der Zuendung dreht die Stufe rechtzeitig zurueck auf rueckwaerts, damit das Triebwerk
        // richtig steht. Und der Vorhersage wird der Widerstand der RUECKWAERTS fliegenden Stufe
        // gegeben (DescentAdapter.AxialDragCoefficient): sie plant die Bremszuendung damit, also mit
        // dem kleineren Wert - zusaetzliche Bremsung durch das Gleiten verschiebt die Zuendung nur
        // nach hinten, nie zu spaet.
        public sealed class GlideSettings
        {
            internal GlideSettings Copy() { return (GlideSettings)MemberwiseClone(); }
            // Anstellwinkel gegen die Flugrichtung [Grad]. 0 schaltet das Gleiten ab.
            public double AngleDegrees = 35;
            // Unterhalb dieser Hoehe ueber Grund wird nicht mehr geglitten.
            // Die Vorhersage entscheidet selbst, wann zurueckgedreht wird; das hier ist nur die Grenze.
            public double MinimumClearance = 2000;
            // Erst ab dieser Luftdichte [kg/m^3] lohnt es (Kerbin: etwa 45 km Hoehe).
            public double MinimumDensity = 3e-4;
            // Unter dieser Geschwindigkeit bringt der Anstellwinkel kaum noch etwas [m/s].
            public double MinimumSpeed = 150;
            // So viele Sekunden vor der geplanten Zuendung steht die Stufe wieder rueckwaerts.
            // 5 s, nicht 12: bei 700 m/s kosteten 12 s Zurueckdrehen 9 km Hoehe, und der Plan mit Gleiten
            // hielt jede Landung fuer aussichtslos. Mit Steuerflaechen dreht die Stufe in 3-5 s zurueck.
            public double IgnitionLeadSeconds = 5;
            // So lange darf die Stufe brauchen, bis sie im Gleitwinkel liegt und Auftrieb liefert [s].
            public double LiftCheckSeconds = 10;
            // Oberhalb dieser Hoehe ueber Grund beginnt das Gleiten auch dann, wenn der Rueckwaerts-Plan
            // eine baldige Zuendung verlangt: der Plan mit Gleiten wird danach neu gerechnet.
            public double StartClearance = 12000;
            // Beim Gleiten Treibstoff vom Triebwerk weg pumpen (FuelTrim), damit die Steuerflaechen
            // gegen weniger Stabilitaet arbeiten.
            public bool PumpFuel = true;
            // Drueckt der Rumpf beim Gleiten nach unten (Auftrieb/Widerstand darunter), haengt die Stufe
            // auf der falschen Seite im Luftstrom: dann schadet das Gleiten. Ohne Auftrieb bremst es
            // immer noch mit dem viel groesseren Widerstand und bleibt deshalb erlaubt.
            public double MinimumLiftRatio = -0.05;
            // Erreicht die Stufe nach LiftCheckSeconds im Mittel weniger Anstellwinkel als das, reichen
            // die Steuerkraefte nicht fuer das Gleiten [Grad]. Fluege vom 26.09.2026, 09:16 und 09:23
            // (ohne Steuerflaechen): 0,2-3 Grad statt 35, Cd*A wie rueckwaerts, bis 2 km "gegleitet"
            // und mit 650 m/s aufgeschlagen. Mit Steuerflaechen wurden 9-12 Grad erreicht und das
            // Gleiten bremste mit dem Achtfachen.
            public double MinimumReachedDegrees = 5;
            // Geprueft wird erst ab diesem Staudruck [Pa]; gezaehlt werden LiftCheckSeconds darueber.
            public double CheckPressure = 2000;
        }

        public readonly TerminalSettings Terminal = new TerminalSettings();
        public readonly GlideSettings Glide = new GlideSettings();
        public readonly PredictorSettings Predictor = new PredictorSettings();
        public readonly ControlSettings Control = new ControlSettings();

        public DescentConfig() { }
        private DescentConfig(DescentConfig source)
        {
            Terminal = source.Terminal.Copy();
            Predictor = source.Predictor.Copy();
            Control = source.Control.Copy();
            Glide = source.Glide.Copy();
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

