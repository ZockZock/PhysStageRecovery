using System;
using BoosterWatch;

// Which stages the mod takes over. The rule is plain logic, so it runs here without KSP - including the
// cases the flights found: a canopy that is already open, and engines without a control module where
// KSP would never answer the throttle.
class TrackingAcceptanceTests
{
    static int count;
    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }

    // usableChute, openChute, poweredLanding, engines, controlModule
    static string Reason(bool usable, bool open, bool powered = true, bool engines = true, bool control = true)
    { return TrackingAcceptance.Reason(usable, open, powered, engines, control); }

    static int Main()
    {
        // Ein geschlossener, eingeschalteter Schirm genuegt immer.
        Check(Reason(true, false) == null, "Stage with a usable canopy is tracked");
        Check(Reason(true, false, true, false, false) == null,
            "A usable canopy is enough without engine and control module");
        Check(Reason(true, false, false, false, false) == null,
            "A usable canopy is enough with powered landing switched off");

        // Ein schon offener Schirm schliesst die Stufe aus - auch mit Triebwerk und Kontrollmodul.
        Check(Reason(false, true) == "#PSR_Skip_ChuteOpen",
            "An already open canopy is refused");
        Check(Reason(true, true) == "#PSR_Skip_ChuteOpen",
            "An open canopy refuses the stage even when another one is still closed");
        Check(Reason(false, true, true, true, true) == "#PSR_Skip_ChuteOpen",
            "An open canopy refuses the stage even with engine and control module");
        Check(Reason(false, true, false, false, false) == "#PSR_Skip_ChuteOpen",
            "The open canopy is named before every other reason");

        // Gekappte oder abgeschaltete Schirme sind keine Schirme.
        Check(Reason(false, false) == null, "Cut canopies with engine and control module are tracked");
        Check(Reason(false, false, true, false, true) == "#PSR_Skip_NoLandingMeans",
            "Cut canopies without an engine are no landing means");
        Check(Reason(false, false, true, true, false) == "#PSR_Skip_NoControlModule",
            "Engine without a control module is refused with that reason");

        // Triebwerkslandung aus: nur ein brauchbarer Schirm zaehlt.
        Check(Reason(false, false, false, true, true) == "#PSR_Skip_NoChutesAndOff",
            "With powered landing off only canopies count");
        Check(Reason(false, false, false, true, false) == "#PSR_Skip_NoChutesAndOff",
            "Powered landing off is named even without a control module");

        Console.WriteLine(count + " tracking acceptance checks passed.");
        return 0;
    }
}
