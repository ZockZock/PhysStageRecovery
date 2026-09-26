// Steuerflaechen im Rueckwaertsflug umkehren. Reine Logik, kein KSP.
using System;
using BoosterWatch;

internal static class ControlSurfacePolicyTests
{
    private static int failures;
    private static void Check(bool ok, string text)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + text);
        if (!ok) failures++;
    }

    private static int Main()
    {
        Check(!ControlSurfacePolicy.Reversed(false, 800, 0.99), "Aufstieg: Luft von vorn, normal");
        Check(ControlSurfacePolicy.Reversed(false, 800, -0.99), "Abstieg mit Triebwerk voran: umgekehrt");
        Check(ControlSurfacePolicy.Reversed(true, 800, 0.1) && !ControlSurfacePolicy.Reversed(false, 800, -0.1),
            "Quer zur Luft bleibt der letzte Stand (Hysterese)");
        Check(ControlSurfacePolicy.Reversed(true, 5, 0.99), "Stillstand aendert nichts");
        Check(!ControlSurfacePolicy.Reversed(false, double.NaN, -1), "Ungueltige Messung aendert nichts");
        Check(ControlSurfacePolicy.Authority(100, true) == -100 && ControlSurfacePolicy.Authority(100, false) == 100,
            "Stellbereich wird umgekehrt und zurueckgesetzt");
        Check(ControlSurfacePolicy.Authority(-50, true) == 50, "Vom Spieler umgekehrte Flaeche bleibt relativ richtig");
        Check(ControlSurfacePolicy.Original(-100, true) == 100, "Im Rueckwaertsflug gespeicherter Wert wird erkannt");
        Check(ControlSurfacePolicy.Original(80, true) == 80 && ControlSurfacePolicy.Original(-80, false) == -80,
            "Sonst gilt der gesehene Wert");
        Console.WriteLine(failures == 0 ? "Alle Steuerflaechen-Tests bestanden." : failures + " fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }
}
