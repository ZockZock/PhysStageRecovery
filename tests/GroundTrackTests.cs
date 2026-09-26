using System;
using BoosterWatch;

// Die Bodenspur als Gegenprobe zu KSPs Geschwindigkeitsanzeige. Reine Arithmetik.
internal static class GroundTrackTests
{
    private static int failures;
    private const double KerbinRadius = 600000.0;

    private static void Check(bool ok, string name)
    {
        if (ok) { Console.WriteLine("OK: " + name); return; }
        failures++;
        Console.WriteLine("FAIL: " + name);
    }

    private static bool Near(double a, double b, double tolerance)
    {
        return Math.Abs(a - b) <= tolerance;
    }

    private static int Main()
    {
        // Reine Rechnung: Bewegung nach Norden und Osten.
        Check(Near(GroundTrack.Speed(0, 0, 0, 0.001, 0, 1, KerbinRadius), 10.47, 0.05),
            "1 mrad Nord in 1 s ergibt 10,47 m/s");
        Check(Near(GroundTrack.Speed(0, 0, 0, 0, 0.001, 1, KerbinRadius), 10.47, 0.05),
            "1 mrad Ost am Aequator ergibt 10,47 m/s");
        Check(GroundTrack.Speed(60, 0, 0, 60, 0.001, 1, KerbinRadius) < 6,
            "auf 60 Grad Breite ist derselbe Laengengrad-Schritt nur halb so schnell");
        Check(Near(GroundTrack.Speed(0, 179.9, 0, 0, -179.9, 1, KerbinRadius), 2094, 2),
            "ueber die Datumsgrenze gerechnet, nicht 3,8 Mio m/s");
        Check(double.IsNaN(GroundTrack.Speed(0, 0, 0, 0, 0, 0, KerbinRadius)), "ohne Zeitdifferenz keine Aussage");
        Check(double.IsNaN(GroundTrack.Speed(0, 0, 1, 0, 0, 0, KerbinRadius)), "rueckwaerts keine Aussage");
        Check(double.IsNaN(GroundTrack.Speed(double.NaN, 0, 0, 0, 0, 1, KerbinRadius)), "ohne Breitengrad keine Aussage");
        Check(double.IsNaN(GroundTrack.Speed(0, 0, 0, 0, 0, 1, 0)), "ohne Koerperradius keine Aussage");

        // Der gemeldete Fehler: Anzeige 175 m/s, stillstehend am Fallschirm.
        GroundTrack still = new GroundTrack();
        double resolved = double.NaN;
        for (int tick = 0; tick <= 15; tick++) resolved = still.Resolve(175.0, 1.0, 2.0, tick * 0.02, KerbinRadius);
        Check(Near(resolved, 0.0, 0.01), "50-Hz-Ticks, stillstehend: nach 0,3 s gewinnt die Spur (0 statt 175)");
        Check(still.UsedTrack, "der Vergleich hat die Anzeige als falsch erkannt");
        Check(Near(still.Derived, 0.0, 0.01), "die Spurgeschwindigkeit ist null");

        // Vor Ablauf des Fensters wird nicht verglichen.
        GroundTrack early = new GroundTrack();
        early.Resolve(175.0, 1.0, 2.0, 0.0, KerbinRadius);
        Check(early.Resolve(175.0, 1.0, 2.0, 0.28, KerbinRadius) == 175.0,
            "nach 0,28 s gilt weiter die Anzeige");
        Check(!early.UsedTrack, "vor Ablauf des Fensters wird nichts ersetzt");

        // Echte Bewegung: beide Quellen stimmen ueberein.
        GroundTrack real = new GroundTrack();
        real.Resolve(250.0, 0.0, 0.0, 0.0, KerbinRadius);
        double moved = real.Resolve(250.0, 0.0, 0.007162, 0.3, KerbinRadius);
        Check(Near(moved, 250.0, 1.0), "echte 250 m/s bleiben 250 m/s");
        Check(!real.UsedTrack, "bei Uebereinstimmung wird nichts ersetzt");

        // Kleine Abweichung ist Rauschen und aendert nichts.
        GroundTrack noise = new GroundTrack();
        noise.Resolve(10.0, 0.0, 0.0, 0.0, KerbinRadius);
        Check(noise.Resolve(10.0, 0.0, 0.00002, 0.3, KerbinRadius) == 10.0,
            "Abweichung unter 25 m/s bleibt die Anzeige");

        // Die Anzeige darf auch zu klein sein - dann bleibt sie.
        GroundTrack low = new GroundTrack();
        low.Resolve(2.0, 0.0, 0.0, 0.0, KerbinRadius);
        Check(low.Resolve(2.0, 0.0, 0.00898, 0.3, KerbinRadius) == 2.0,
            "zu kleine Anzeige bei echter Bewegung: der kleinere Wert bleibt");
        Check(low.UsedTrack, "dort hat die Spur den Ausschlag gegeben");

        Console.WriteLine(failures == 0
            ? "Alle Bodenspur-Tests bestanden."
            : failures + " Bodenspur-Tests fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }
}
