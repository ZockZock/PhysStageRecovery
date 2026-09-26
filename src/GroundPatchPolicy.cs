namespace BoosterWatch
{
    // Wann ein eigener Boden unter einen getrackten Booster gebaut wird.
    //
    // Das Hoehenfeld ist das Sicherheitsnetz unter fernen Boostern (siehe GroundPatch); ob echter
    // Boden darunter liegt, entscheidet GroundPatch.SetRealGround, nicht diese Regel.
    //
    // Die Entscheidung haengt bewusst NICHT davon ab, ob gerade ein fremder Kollider gefunden
    // wurde: der eigene Patch ist selbst so ein Kollider. Wuerde er mitgezaehlt, waere er im
    // naechsten Tick wieder "unnoetig" und wuerde gebaut und verworfen, im Sekundentakt.
    public static class GroundPatchPolicy
    {
        // Naeher als das liegt der Booster im Bereich, in dem KSP selbst Gelaende und Kollision
        // baut - dort ist ein eigener Patch unnoetig und wuerde sich mit dem echten Boden doppeln.
        public const double MinimumDistance = 2500;

        // Nur in Bodennaehe lohnt der Patch. In groesserer Hoehe waere er nutzlos und wuerde bei
        // jeder Bewegung neu gebaut.
        public const double MaximumClearance = 2500;

        // Ab dieser Verschiebung seit dem letzten Bau wird das Hoehenfeld neu zentriert.
        public const double RecentreDistance = 150;

        // Ein Patch, der aelter ist, wird erneuert - der Booster driftet, und das Feld soll unter
        // ihm bleiben.
        public const double MaximumAge = 3;

        public static bool Needed(double distance, double clearance, bool overWater)
        {
            // Ueber Wasser ist die Wasseroberflaeche die Bodenreferenz; KSP behandelt sie eigen,
            // und ein Netz auf dem Meeresboden waere hunderte Meter zu tief und damit wertlos.
            if (overWater) return false;
            if (!RecoveryPolicy.Finite(distance) || !RecoveryPolicy.Finite(clearance)) return false;
            return distance > MinimumDistance && clearance < MaximumClearance;
        }

        // Bleibt der Patch, oder muss er neu gebaut werden?
        public static bool MustRebuild(double movedSinceBuild, double age, double clearance)
        {
            if (!RecoveryPolicy.Finite(movedSinceBuild) || !RecoveryPolicy.Finite(age)) return true;
            if (!RecoveryPolicy.Finite(clearance) || clearance >= MaximumClearance) return true;
            return movedSinceBuild > RecentreDistance || age > MaximumAge;
        }
    }
}
