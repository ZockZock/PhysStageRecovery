namespace BoosterWatch
{
    // Whether a separated stage is worth taking over at all.
    //
    // A stage without a control module cannot be landed under power, and not because the autopilot
    // would be missing something: KSP's engines ignore the order. ModuleEngines.UpdateThrottle asks
    // Part.isControllable (which is Vessel.isControllable, set in Vessel.CheckControllable from
    // GetControlLevel) and leaves requestedThrottle untouched when it is false. The autopilot would
    // command thrust into the void while the booster falls.
    //
    // Parachutes need none of that, so a stage with canopies is taken over either way - the powered
    // landing then simply stays off and the window says why.
    //
    // The rule is kept free of KSP: the caller passes what it found on the vessel, which is also what
    // makes it testable (tests/TrackingAcceptanceTests.cs).
    public static class TrackingAcceptance
    {
        // Reason to leave the stage alone, as a language tag, or null to track it.
        public static string Reason(bool chutes, bool poweredLanding, bool engines, bool controlModule)
        {
            if (chutes) return null;
            if (!poweredLanding) return "#PSR_Skip_NoChutesAndOff";
            if (!engines) return "#PSR_Skip_NoLandingMeans";
            if (!controlModule) return "#PSR_Skip_NoControlModule";
            return null;
        }
    }
}
