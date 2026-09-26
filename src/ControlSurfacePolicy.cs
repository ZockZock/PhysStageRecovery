using System;

namespace BoosterWatch
{
    // Reine Logik zu ControlSurfaceFlow (testbar ohne KSP).
    public static class ControlSurfacePolicy
    {
        // Darunter hat die Luft nichts zu sagen, und die Richtung bleibt wie sie ist [m/s].
        public const double MinimumSpeed = 30;
        // Hysterese auf dem Kosinus zwischen Bugrichtung und Flugrichtung.
        public const double EnterReverse = -0.2, LeaveReverse = 0.2;

        public static bool Reversed(bool current, double speed, double along)
        {
            if (double.IsNaN(speed) || double.IsNaN(along) || speed < MinimumSpeed) return current;
            if (along < EnterReverse) return true;
            if (along > LeaveReverse) return false;
            return current;
        }

        public static float Authority(float original, bool reversed)
        {
            return reversed ? -original : original;
        }

        // Der Wert ist gespeichert (isPersistant). Wurde mitten im Rueckwaertsflug gespeichert, steht
        // er umgekehrt im Spielstand; ein negativer Wert an einem gerade rueckwaerts fliegenden
        // Booster ist deshalb der schon umgekehrte.
        public static float Original(float seen, bool reversedNow)
        {
            return seen < 0 && reversedNow ? -seen : seen;
        }
    }
}
