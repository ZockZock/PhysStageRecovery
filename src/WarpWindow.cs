using System;
using UnityEngine;

namespace BoosterWatch
{
    // Normaler Zeitraffer, solange alle Booster ausserhalb der Luft auf ihrer Bahn fliegen.
    //
    // Im Vakuum braucht ein Booster keine Physik: KSP rechnet ihn auf Schienen genau weiter, und die
    // erweiterten Ladeentfernungen (VesselRangeTransition) lassen ihn geladen. Physik braucht er erst in
    // der Luft. Also: Zeitraffer frei, bis der erste Booster PhysicsLeadSeconds (Spielzeit) vor dem
    // Eintritt ist; erst ganz kurz davor wird die Stufe so weit gebremst, dass die Marke nicht in einem
    // Bild uebersprungen wird. Dann steht die Zeit auf 1x, die Booster haben wieder Physik und richten
    // sich vor der Luft aus - danach gilt wie bisher nur der Physikwarp.
    public static class WarpWindow
    {
        // So viel Spielzeit vor dem Eintritt muss der Booster wieder Physik haben (Ausrichten, Trimmtank).
        // 30 s: Wunsch aus dem Test vom 26.09.2026, 60 s waren zu vorsichtig.
        public const double PhysicsLeadSeconds = 30;
        // Die Zeitrafferstufe wird erst so knapp begrenzt, dass die 30-s-Marke in hoechstens dieser
        // Zeit (echte Sekunden) erreicht wird - sonst spraenge ein einziges Bild bei 100000x ueber sie
        // hinweg und der Booster fiele auf Schienen in die Luft. Bis kurz davor laeuft der volle Zeitraffer.
        public const double LeadRealSeconds = 1.5;

        // Hoechste erlaubte Zeitrafferrate fuer eine Eintrittszeit (Spielsekunden). 1 = keine Schienen.
        public static double AllowedRate(double secondsToAtmosphere)
        {
            if (double.IsNaN(secondsToAtmosphere) || secondsToAtmosphere <= PhysicsLeadSeconds) return 1;
            if (double.IsPositiveInfinity(secondsToAtmosphere)) return double.PositiveInfinity;
            return Math.Max(1, (secondsToAtmosphere - PhysicsLeadSeconds) / LeadRealSeconds);
        }

        // Groesster Index in rates, dessen Rate hoechstens allowed ist.
        public static int IndexFor(float[] rates, double allowed)
        {
            if (rates == null || rates.Length == 0) return 0;
            int best = 0;
            for (int i = 0; i < rates.Length; i++) if (rates[i] <= allowed + 1e-6) best = i;
            return best;
        }

        // Spielsekunden bis der Booster die Obergrenze der Atmosphaere unterschreitet. 0, wenn er schon
        // darin ist, unendlich, wenn seine Bahn nicht hineinfuehrt. Aus der Bahn gerechnet, damit es auch
        // fuer einen Booster stimmt, der gerade auf Schienen liegt.
        public static double SecondsToAtmosphere(Vessel v, double now)
        {
            try
            {
                CelestialBody body = v.mainBody;
                if (body == null || !body.atmosphere) return double.PositiveInfinity;
                double top = body.Radius + body.atmosphereDepth;
                if (v.altitude < body.atmosphereDepth || v.LandedOrSplashed) return 0;
                Orbit o = v.orbit;
                if (o == null) return 0;
                if (o.PeR > top) return double.PositiveInfinity;
                // Schrittweise vorwaerts: fein zuerst, dann groeber. Suborbitale Booster treffen die
                // Luft meist innerhalb von Minuten.
                double t = 0;
                for (int i = 0; i < 2000; i++)
                {
                    double step = t < 120 ? 2 : t < 3600 ? 15 : 60;
                    t += step;
                    if (o.getRelativePositionAtUT(now + t).magnitude < top) return Math.Max(0, t - step);
                    if (t > 6 * 3600) break;
                }
                return double.PositiveInfinity;
            }
            catch (Exception) { return 0; }
        }
    }
}
