using System;

namespace BoosterWatch.Guidance
{
    // The braking burn. One acceleration vector has to remove both velocity components by the time the
    // booster reaches the capture altitude, where the terminal phase takes over: the vertical speed
    // down to the capture speed and the sideways speed to nothing. Called by flight and prediction, so
    // the plan and the burn are the same arithmetic. Atmospheric drag is credited per component.
    //
    // The second thing it has to know is where the engine can actually push. Above the pinning
    // pressure the airstream owns the hull: the booster flies the relative wind, and a command to hold
    // the descent up is delivered as a push along the flight path instead - the flight logs of
    // 24.09.2026 show the attitude error equal to the angle between command and flight path, 74 deg at
    // 15 km. A law that ignores that both mis-aims the burn and mis-plans it: it asks for a vertical
    // deceleration it will not get, and its own forecast then has to start the burn kilometres early
    // to make up for authority that never arrives. So the command is aimed in the blend of what is
    // wanted and what the air allows, and while the air is in charge the engine runs at what it has -
    // the one thing that phase can do is take speed out, and every newton held back is paid for with
    // the height the ignition has to be moved up by.
    internal static class CaptureBraking
    {
        // Vorrat auf den Schub, den der Abstieg entlang der Bahn braucht.
        public const double PathMargin = 1.3;
        // Die Seitenfahrt ist so viele Meter ueber der Aufricht-Hoehe weg.
        public const double LateralMargin = 25;
        // Darunter: Restfahrt ueber so viele Sekunden, mit hoechstens ~5 Grad Schraeglage.
        public const double FinalLateralSeconds = 3;
        // Unter dieser Seitenfahrt [m/s] wird sie sanft, ueber mindestens SmoothLateralSeconds, abgebaut.
        public const double SmoothLateralSpeed = 5, SmoothLateralSeconds = 3;
        public const double FinalLeanTangent = 0.09;

        public static void Command(DescentState state, DescentConfig config,
            out double vertical, out double lateral, out bool emergency)
        {
            double sink = Math.Max(0, state.Sink), side = state.LateralSpeed;
            double thrust = Math.Max(0, state.ThrustAcceleration), gravity = Math.Max(0, state.Gravity);
            double speed = Math.Sqrt(sink * sink + side * side);
            double drag = state.DragValid ? Math.Max(0, state.DragAcceleration) : 0;
            // A measurement, so it can be wrong. Crediting more drag than five times local gravity is
            // never right, and the cost of believing it is an engine that stays dark.
            drag = Math.Min(drag, 5 * gravity);
            double dragUp = speed > 1e-9 ? drag * sink / speed : 0;
            double dragSide = speed > 1e-9 ? drag * side / speed : 0;
            // The one state this burn exists for: at the capture altitude the terminal phase expects to
            // take over a descent of exactly `CaptureSpeed`, upright and without drift. Both components
            // get the same deadline - the time a constant deceleration needs for the drop that is left
            // - so the sideways speed is gone by then instead of being carried to the ground.
            double targetSink = Math.Max(0, config.Predictor.CaptureSpeed);
            double distance = Math.Max(0.5, state.Clearance - config.Predictor.CaptureAltitude);
            double time = Math.Max(0.25, 2 * distance / Math.Max(1, sink + targetSink));
            double wantedUp = Math.Max(0, gravity - dragUp + (sink - targetSink) / time);
            // Die Seitenfahrt muss deutlich frueher weg sein als die Sinkrate: fuer die letzten
            // Meter steht die Stufe aufrecht, und aus einer Schraeglage kommt eine 25-t-Stufe bei
            // wenig Schub nur langsam heraus. Flug vom 25.09.2026, 22:57 (Zielhoehe 10 m): bis 14 m
            // 15 Grad Schraeglage gegen 20 m/s Seitenfahrt, darunter "aufrecht" befohlen, die Stufe
            // brauchte zwei Sekunden dafuer und schob sich dabei mit 4,4 m/s in die Gegenrichtung.
            // Daher eine eigene, hoehere Frist fuer die Seitenfahrt, und darunter nur noch sanft.
            double lateralFloor = Math.Max(config.Predictor.CaptureAltitude,
                config.Terminal.UprightAltitude + LateralMargin);
            double lateralDistance = state.Clearance - lateralFloor;
            // Ein kleiner Rest wird nie schaerfer als ueber SmoothLateralSeconds herausgenommen: eine
            // Frist, die gegen null geht, verlangt kurz davor die groesste Schraeglage - genau dann,
            // wenn sich die Stufe schon aufrichten muss. Flug vom 25.09.2026, 23:16: bei 52 m 28 Grad
            // Schraeglage fuer die letzten 4,7 m/s, danach 1,5 s Aufrichten mit 25 Grad Lagefehler,
            // die Stufe schob sich mit 10 m/s in die Gegenrichtung. Bei grosser Seitenfahrt bleibt die
            // Frist hart, sonst kaeme sie gar nicht mehr weg.
            double lateralTime = lateralDistance > 0.5
                ? Math.Max(side < SmoothLateralSpeed ? SmoothLateralSeconds : 0.25,
                    2 * lateralDistance / Math.Max(1, sink + targetSink))
                : FinalLateralSeconds;
            double wantedSide = Math.Max(0, side / lateralTime - dragSide);
            // Nie schraeger, als sich rechtzeitig wieder aufrichten laesst. Beim Aufrichten mit der
            // Drehrate w schiebt der Schub die Stufe weiter seitwaerts, um etwa a * theta^2 / (2 w).
            // Die Schraeglage theta = Wurzel(2 w v / a) nimmt die Seitenfahrt v deshalb genau bis
            // null heraus, waehrend sich die Stufe aufrichtet. Flug vom 26.09.2026, 08:35: bei 43 m
            // war die Seitenfahrt weg (2,6 m/s), die Stufe hing aber noch 28 Grad schief, und beim
            // Aufrichten schob sie sich mit 12 m/s in die Gegenrichtung.
            double turnRate = config.Control.TurnRateDegrees * Math.PI / 180;
            double push = Math.Max(wantedUp, 0.5 * gravity);
            if (turnRate > 0 && push > 0)
            {
                double lean = Math.Sqrt(2 * turnRate * side / push);
                if (lean < 1.2) wantedSide = Math.Min(wantedSide, wantedUp * Math.Tan(lean));
            }
            if (lateralDistance <= 0.5) wantedSide = Math.Min(wantedSide, wantedUp * FinalLeanTangent);
            double wanted = Math.Sqrt(wantedUp * wantedUp + wantedSide * wantedSide);

            // What the airstream leaves of that command. `control` is one when the vehicle can point
            // wherever it is asked to and zero when the hull belongs entirely to the wind.
            double pinning = config.Predictor.AeroPinningPressure;
            double control = 1;
            if (pinning > 1 && speed > 1e-6)
                control = Clamp(1 - (0.5 * Math.Max(0, state.AirDensity) * speed * speed) / pinning, 0, 1);

            double up = wantedUp, out2 = wantedSide, limit = Math.Min(thrust, wanted);
            if (control < 1 && speed > 1e-6)
            {
                // Aim in the blend, at the whole engine. Both components are capped by what the
                // descent needs with the flare's own response time: the path braking may take speed
                // out, but it may not take the sink rate away altogether (a booster held up by its own
                // engine at altitude burns the propellant the landing needs - measured in an earlier
                // flight: sink rate zero for 600 s at 31 km, 20 t of propellant overboard) and it may
                // not push the drift past zero.
                double windUp = sink / speed, windSide = side / speed;
                up = (1 - control) * windUp * thrust + control * wantedUp;
                out2 = (1 - control) * windSide * thrust + control * wantedSide;
                double response = Math.Max(0.1, config.Control.FlareResponse);
                double ceilingUp = gravity + sink / response;
                double ceilingSide = side / response;
                if (up > ceilingUp) up = ceilingUp;
                if (out2 > ceilingSide) out2 = ceilingSide;
                // Aber nicht mehr, als der Abstieg braucht. Entlang der Bahn schiebt der ganze Schub
                // beide Anteile zugleich; noetig ist so viel, dass der groessere Bedarf gedeckt ist -
                // mit PathMargin Vorrat. Mehr bremst die Stufe unter die Leiter, und von dort sinkt sie
                // langsam und teuer: im Nachbau des Fluges vom 25.09.2026 (frueh gezuendet) stand sie
                // bei 3 km mit 120 m/s und verbrannte den Rest der Tanks im Schwebeflug.
                double needed = Math.Max(windUp > 1e-3 ? wantedUp / windUp : 0,
                    windSide > 1e-3 ? wantedSide / windSide : 0);
                limit = Math.Min(thrust, PathMargin * needed);
            }
            double magnitude = Math.Min(limit, Math.Sqrt(up * up + out2 * out2));
            double norm = Math.Sqrt(up * up + out2 * out2);
            if (norm > 1e-9)
            {
                vertical = up / norm * magnitude;
                lateral = out2 / norm * magnitude;
            }
            else
            {
                vertical = 0;
                lateral = 0;
            }
            // The flag says whether the burn as asked for is beyond the engine - which, with the
            // ignition point chosen by the same arithmetic, means the plan is already too late.
            emergency = wanted > thrust;
        }

        private static double Clamp(double x, double low, double high)
        {
            return Math.Max(low, Math.Min(high, x));
        }
    }
}
