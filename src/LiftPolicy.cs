using System;

namespace BoosterWatch
{
    // Rumpfauftrieb aus der gemessenen Beschleunigung, reine Arithmetik (testbar ohne KSP).
    public static class LiftPolicy
    {
        // Darunter ist der Widerstand zu klein fuer eine Aussage [m/s^2]: die Rechnung aus der
        // geglaetteten Beschleunigung traegt im rotierenden Bezugssystem bis ~1 m/s^2 Coriolis-Rest.
        public const double MinimumDrag = 5;
        // So lange nach der letzten Messung gilt der Wert noch (die Zuendung selbst misst nicht).
        public const double HoldSeconds = 120;
        // Glaettung [s].
        public const double TimeConstant = 1.5;

        // ratio = Auftrieb (oben positiv) / Widerstand. false, wenn es nichts zu messen gibt.
        public static bool Ratio(double drag, double liftUp, out double ratio)
        {
            ratio = double.NaN;
            if (!Finite(drag) || !Finite(liftUp) || drag < MinimumDrag) return false;
            ratio = Math.Max(-1.5, Math.Min(1.5, liftUp / drag));
            return true;
        }

        public static double Smooth(double previous, double sample, double dt)
        {
            if (!Finite(previous) || !Finite(dt) || dt <= 0) return sample;
            double k = Math.Min(1, dt / TimeConstant);
            return previous + (sample - previous) * k;
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
