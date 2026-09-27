using System;

namespace BoosterWatch
{
    // Schwierigkeitsgrad der Wiedereintrittshitze fuer verfolgte Booster.
    //
    // KSP zerstoert ein Teil, sobald seine Temperatur oder Hauttemperatur ueber maxTemp /
    // skinMaxTemp steigt (Part._CheckPartTemp). HeatGuard setzt diese Grenzen je nach Grad:
    //   Leicht      - die Grenzen werden praktisch aufgehoben (bisheriges Verhalten, Standard).
    //   Normal      - echte Physik, aber die Grenzen liegen 20 % hoeher als im Original.
    //   Realistisch - die Originalgrenzen der Teile, nichts wird veraendert.
    // Die Rakete, die der Spieler fliegt, wird nie angefasst.
    public enum HeatMode { Easy = 0, Normal = 1, Realistic = 2 }

    public static class HeatPolicy
    {
        // Spielraum im Modus Normal.
        public const double NormalTolerance = 1.2;
        // Wert, mit dem der Modus Leicht die Grenzen "aufhebt".
        public const double ImmuneTemperature = 100000;

        // Faktor auf die Originalgrenze, ab der ein Teil zerstoert wird. Unendlich = nie.
        public static double LimitFactor(HeatMode mode)
        {
            switch (mode)
            {
                case HeatMode.Easy: return double.PositiveInfinity;
                case HeatMode.Normal: return NormalTolerance;
                default: return 1;
            }
        }

        // Die Grenze, die KSP fuer dieses Teil pruefen soll.
        public static double Limit(HeatMode mode, double original)
        {
            if (!(original > 0) || double.IsInfinity(original)) return original;
            double factor = LimitFactor(mode);
            return double.IsInfinity(factor) ? Math.Max(original, ImmuneTemperature) : original * factor;
        }

        // Anteil an der Grenze, an der das Teil in diesem Modus wirklich zerbricht (1 = zerbricht).
        // Im Modus Leicht zaehlt die Originalgrenze: das ist die Zahl, die ohne Schutz gelten wuerde.
        public static double Ratio(HeatMode mode, double temperature, double original)
        {
            if (!(original > 0) || double.IsNaN(temperature)) return double.NaN;
            double factor = LimitFactor(mode);
            return temperature / (original * (double.IsInfinity(factor) ? 1 : factor));
        }

        // Ein Teil kann durch Hitze zerstoert werden (Normal, Realistisch).
        public static bool Destructive(HeatMode mode) { return mode != HeatMode.Easy; }

        public static string Key(HeatMode mode)
        {
            switch (mode)
            {
                case HeatMode.Easy: return "easy";
                case HeatMode.Normal: return "normal";
                default: return "realistic";
            }
        }

        public static bool TryParse(string text, out HeatMode mode)
        {
            mode = HeatMode.Easy;
            if (string.IsNullOrEmpty(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "easy": case "leicht": case "0": mode = HeatMode.Easy; return true;
                case "normal": case "1": mode = HeatMode.Normal; return true;
                case "realistic": case "realistisch": case "2": mode = HeatMode.Realistic; return true;
                default: return false;
            }
        }

        public static string Tag(HeatMode mode)
        {
            switch (mode)
            {
                case HeatMode.Easy: return "#PSR_Heat_Easy";
                case HeatMode.Normal: return "#PSR_Heat_Normal";
                default: return "#PSR_Heat_Realistic";
            }
        }

        public static string HintTag(HeatMode mode)
        {
            switch (mode)
            {
                case HeatMode.Easy: return "#PSR_Heat_EasyHint";
                case HeatMode.Normal: return "#PSR_Heat_NormalHint";
                default: return "#PSR_Heat_RealisticHint";
            }
        }
    }
}
