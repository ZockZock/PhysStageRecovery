using System;
using BoosterWatch;

// Der eigene Boden: wann er gebaut wird und welche Form sein Netz hat. Reine Arithmetik.
internal static class GroundPatchPolicyTests
{
    private static int failures;

    private static void Check(bool ok, string name)
    {
        if (ok) { Console.WriteLine("OK: " + name); return; }
        failures++;
        Console.WriteLine("FAIL: " + name);
    }

    private static int Main()
    {
        // Wann gebaut wird: weit weg vom aktiven Schiff und in Bodennaehe.
        Check(GroundPatchPolicy.Needed(3000, 100, false), "3000 m entfernt, 100 m hoch: Boden bauen");
        Check(GroundPatchPolicy.Needed(250000, 900, false), "250 km entfernt, 900 m hoch: Boden bauen");
        Check(!GroundPatchPolicy.Needed(2500, 100, false), "genau an der Grenze: noch kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(100, 100, false), "in KSPs eigener Bodenblase: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(3000, 2500, false), "2500 m hoch: noch kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(3000, 5000, false), "hoch ueber Grund: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(double.NaN, 100, false), "Entfernung unbekannt: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(3000, double.NaN, false), "Hoehe unbekannt: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(double.PositiveInfinity, 100, false), "unendlich weit: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(3000, 100, true), "ueber Wasser: kein eigener Boden");
        Check(!GroundPatchPolicy.Needed(250000, 900, true), "ueber Wasser, weit entfernt: kein eigener Boden");

        // Wann neu gebaut wird: der Booster zieht weiter, oder der Patch ist zu alt.
        Check(!GroundPatchPolicy.MustRebuild(0, 0, 100), "frisch gebaut und nicht gezogen: bleibt");
        Check(!GroundPatchPolicy.MustRebuild(100, 2, 100), "100 m gezogen, 2 s alt: bleibt");
        Check(GroundPatchPolicy.MustRebuild(200, 1, 100), "200 m gezogen: neu bauen");
        Check(GroundPatchPolicy.MustRebuild(10, 5, 100), "5 s alt: neu bauen");
        Check(GroundPatchPolicy.MustRebuild(10, 1, 3000), "Booster ueber 2500 m: Patch aufgeben");
        Check(GroundPatchPolicy.MustRebuild(double.NaN, 1, 100), "Ziehung unbekannt: neu bauen");

        // Form des Netzes.
        Check(GroundField.SideCount(32) == 33, "33 Punkte je Kante bei Aufloesung 32");
        Check(GroundField.SideCount(0) == 2, "Aufloesung 0 wird auf das kleinste Netz gezogen");
        Check(GroundField.VertexCount(32) == 33 * 33, "1089 Punkte im Netz");
        Check(GroundField.IndexCount(32) == 32 * 32 * 6, "6144 Indizes im Netz");
        Check(GroundField.TriangleCount(32) == 32 * 32 * 2, "2048 Dreiecke im Netz");

        double east, north;
        GroundField.Offset(32, 350, 0, 0, out east, out north);
        Check(Math.Abs(east + 350) < 1e-9 && Math.Abs(north + 350) < 1e-9,
            "erster Punkt liegt in der Ecke -350/-350");
        GroundField.Offset(32, 350, 32, 32, out east, out north);
        Check(Math.Abs(east - 350) < 1e-9 && Math.Abs(north - 350) < 1e-9,
            "letzter Punkt liegt in der Ecke +350/+350");
        GroundField.Offset(32, 350, 16, 16, out east, out north);
        Check(Math.Abs(east) < 1e-9 && Math.Abs(north) < 1e-9, "Mitte liegt bei null");

        int[] indices = GroundField.Indices(32);
        int limit = GroundField.VertexCount(32);
        bool inRange = true, distinct = true;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= limit) inRange = false;
            if (i % 3 == 2)
            {
                int a = indices[i - 2], b = indices[i - 1], c = indices[i];
                if (a == b || b == c || a == c) distinct = false;
            }
        }
        Check(inRange, "kein Index zeigt aus dem Netz heraus");
        Check(distinct, "kein Dreieck ist entartet");
        Check(indices.Length == GroundField.IndexCount(32), "Indexliste hat die angekuendigte Laenge");

        Console.WriteLine(failures == 0
            ? "Alle Boden-Tests bestanden."
            : failures + " Boden-Tests fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }
}
