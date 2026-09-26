using System;

namespace BoosterWatch
{
    // Die Bergungsentscheidung der Triebwerkslandung faellt beim Abschalten des Triebwerks.
    //
    // Die Landeregelung schaltet das Triebwerk in Schnitthoehe (Terminal.EngineCutoffAltitude,
    // 1,5 m, am Hang etwas hoeher) ab. In diesem Moment ist der Booster noch in der Luft, die
    // Regelung hat ihre Arbeit getan, und jede Zahl ist sauber: Sinkrate, seitliche Fahrt,
    // Drehung, Neigung. Was danach passiert - die letzten 1,5 m freier Fall, das Federn der Beine,
    // ein Kollider, der einen Tick zu spaet steht - sagt ueber die Landung nichts mehr aus.
    //
    // Anlass (Flug vom 25.09.2026, 20:01): der Booster setzte mit 3,7 m/s senkrecht, 0,3 m/s
    // seitlich und 0,1 Grad Neigung auf, und wurde im selben Moment von einem Bezugssystem-Wechsel
    // weggeschleudert (21 m/s nach oben, 12 rad/s Drehung). Die Bergung, die erst am Kontakt
    // pruefte, sah nur noch den fliegenden Schrott.
    public static class CutoffPolicy
    {
        // Nach dem Abschalten wird spaetestens nach dieser Zeit geborgen, auch ohne gemeldeten
        // Bodenkontakt: 1,5 m freier Fall aus 5 m/s dauern weit unter einer Sekunde.
        public const double ContactWaitSeconds = 1.5;

        // Hoeher als so viel ueber Grund ist kein Abschalten zum Aufsetzen mehr.
        public const double MaximumCutoffClearance = 6;

        // Entscheidung beim Abschalten. reason nennt die erste verletzte Grenze.
        public static TouchdownOutcome Evaluate(double sink, double horizontal, double angular,
            double tiltDegrees, double clearance, RecoveryLimits limits, double tiltLimit, out string reason)
        {
            reason = "";
            if (!Finite(sink) || !Finite(horizontal) || !Finite(angular) || !Finite(clearance))
            { reason = "Messwerte ungueltig"; return TouchdownOutcome.Unconfirmed; }
            if (clearance > MaximumCutoffClearance)
            { reason = "Abschaltung " + clearance.ToString("0.0") + " m ueber Grund"; return TouchdownOutcome.Unconfirmed; }
            if (sink > limits.SinkSpeed)
            { reason = "Sinken " + sink.ToString("0.0") + " > " + limits.SinkSpeed.ToString("0.0") + " m/s"; return TouchdownOutcome.Crashed; }
            if (horizontal > limits.HorizontalSpeed)
            { reason = "seitlich " + horizontal.ToString("0.0") + " > " + limits.HorizontalSpeed.ToString("0.0") + " m/s"; return TouchdownOutcome.Crashed; }
            if (angular > limits.AngularSpeed)
            { reason = "Drehung " + angular.ToString("0.00") + " > " + limits.AngularSpeed.ToString("0.00") + " rad/s"; return TouchdownOutcome.Crashed; }
            if (Finite(tiltDegrees) && tiltDegrees > tiltLimit)
            { reason = "Neigung " + tiltDegrees.ToString("0") + " > " + tiltLimit.ToString("0") + " Grad"; return TouchdownOutcome.Crashed; }
            return TouchdownOutcome.Safe;
        }

        // Jetzt bergen? Bei Kontakt sofort, sonst nach der Wartezeit.
        public static bool RecoverNow(TouchdownOutcome verdict, double cutoffTime, double now, bool contact)
        {
            if (verdict != TouchdownOutcome.Safe || !Finite(cutoffTime)) return false;
            return contact || now - cutoffTime >= ContactWaitSeconds;
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
    }
}
