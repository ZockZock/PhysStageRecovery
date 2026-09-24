using System;
using BoosterWatch;

// Which stages the mod takes over. The rule is plain logic, so it runs here without KSP - including
// the case the flight found: engines but no control module, where KSP would never answer the throttle.
class TrackingAcceptanceTests
{
    static int count;
    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }

    static int Main()
    {
        // Fallschirme genuegen immer - auch ohne Kontrollmodul.
        Check(TrackingAcceptance.Reason(true, true, true, true) == null,
            "Stage with canopies is tracked");
        Check(TrackingAcceptance.Reason(true, true, false, false) == null,
            "Stage with canopies but no engine and no control module is still tracked");
        Check(TrackingAcceptance.Reason(true, false, false, false) == null,
            "Canopies are enough even with powered landing switched off");

        // Triebwerkslandung: Triebwerk UND Kontrollmodul.
        Check(TrackingAcceptance.Reason(false, true, true, true) == null,
            "Engine and control module are enough without canopies");
        Check(TrackingAcceptance.Reason(false, true, true, false) == "#PSR_Skip_NoControlModule",
            "Engine without a control module is refused with that reason");
        Check(TrackingAcceptance.Reason(false, true, false, true) == "#PSR_Skip_NoLandingMeans",
            "No engine and no canopy is refused as before");
        Check(TrackingAcceptance.Reason(false, true, false, false) == "#PSR_Skip_NoLandingMeans",
            "Without engine and control module the missing engine is named first");

        // Triebwerkslandung aus: ein Triebwerk aendert nichts.
        Check(TrackingAcceptance.Reason(false, false, true, true) == "#PSR_Skip_NoChutesAndOff",
            "With powered landing off only canopies count");
        Check(TrackingAcceptance.Reason(false, false, true, false) == "#PSR_Skip_NoChutesAndOff",
            "Powered landing off is named even without a control module");

        Console.WriteLine(count + " tracking acceptance checks passed.");
        return 0;
    }
}
