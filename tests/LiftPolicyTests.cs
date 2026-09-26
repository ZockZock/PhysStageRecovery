// Rumpfauftrieb aus der gemessenen Beschleunigung. Reine Arithmetik, kein KSP.
using System;
using BoosterWatch;

internal static class LiftPolicyTests
{
    private static int failures;
    private static void Check(bool ok, string text)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + text);
        if (!ok) failures++;
    }

    private static int Main()
    {
        double ratio;
        // Flug vom 25.09.2026, 12 km: 93 m/s^2 Widerstand, 41 m/s^2 quer nach unten.
        Check(LiftPolicy.Ratio(93, -41, out ratio) && Math.Abs(ratio + 0.44) < 0.01,
            "Abtrieb im Flug 21:21 ergibt -0,44 x Widerstand (" + ratio.ToString("0.00") + ")");
        Check(!LiftPolicy.Ratio(2, -1, out ratio), "Zu wenig Widerstand fuer eine Aussage");
        Check(!LiftPolicy.Ratio(double.NaN, 1, out ratio), "Ungueltige Messung wird verworfen");
        Check(LiftPolicy.Ratio(10, 100, out ratio) && ratio == 1.5, "Ausreisser werden begrenzt");
        Check(LiftPolicy.Smooth(double.NaN, -0.4, 0.02) == -0.4, "Erste Messung gilt sofort");
        double s = LiftPolicy.Smooth(0, -0.6, 0.15);
        Check(s < 0 && s > -0.1, "Einzelne Messung zieht nur langsam (" + s.ToString("0.000") + ")");
        Check(LiftPolicy.Smooth(0, -0.6, 10) == -0.6, "Nach langer Pause gilt die neue Messung");
        Console.WriteLine(failures == 0 ? "Alle Auftriebs-Tests bestanden." : failures + " fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }
}
