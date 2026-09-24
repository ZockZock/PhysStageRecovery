namespace BoosterWatch
{
    // Whether a separated stage is worth taking over at all.
    //
    // Three things can rule a stage out, and all three come down to the same question: can this stage
    // still be landed the way the mod would fly it?
    //
    //  * A canopy that is already out (SEMIDEPLOYED or DEPLOYED) rules the stage out completely, even
    //    if it carries a working engine and a control module. That canopy opened in a descent nobody
    //    planned for, the chute automation can no longer arm it, and the drag it produces is not part
    //    of any plan the mod made. Hands off.
    //  * A canopy that cannot be used - disabled, or cut by KSP - is no parachute at all. A stage
    //    whose only canopies are in that state needs an engine and a control module to be taken over,
    //    exactly like a stage without canopies.
    //  * A stage without a control module cannot be landed under power, and not because the autopilot
    //    would be missing something: KSP's engines ignore the order. ModuleEngines.UpdateThrottle asks
    //    Part.isControllable (which is Vessel.isControllable, set in Vessel.CheckControllable from
    //    GetControlLevel) and leaves requestedThrottle untouched when it is false.
    //
    // The rule is kept free of KSP: the caller passes what it found on the vessel, which is also what
    // makes it testable (tests/TrackingAcceptanceTests.cs).
    public static class TrackingAcceptance
    {
        // Reason to leave the stage alone, as a language tag, or null to track it.
        public static string Reason(bool usableChute, bool openChute, bool poweredLanding, bool engines,
            bool controlModule)
        {
            if (openChute) return "#PSR_Skip_ChuteOpen";
            if (usableChute) return null;
            if (!poweredLanding) return "#PSR_Skip_NoChutesAndOff";
            if (!engines) return "#PSR_Skip_NoLandingMeans";
            if (!controlModule) return "#PSR_Skip_NoControlModule";
            return null;
        }
    }
}
