namespace BoosterWatch
{
    // Die Geometrie des eigenen Bodens, ohne KSP und ohne Unity: nur Gitter und Dreiecke.
    //
    // Das Hoehenfeld liegt in einem Quadrat von 2 * halfSize Kantenlaenge, aufgespannt in zwei
    // Achsen (Ost und Nord) senkrecht zur lokalen Senkrechten. Die Hoehen selbst kommen von
    // aussen (KSP kennt sie), die Form des Netzes steht hier und ist damit pruefbar.
    public static class GroundField
    {
        // Anzahl der Punkte je Kante (Aufloesung + 1).
        public static int SideCount(int resolution)
        {
            return resolution < 1 ? 2 : resolution + 1;
        }

        public static int VertexCount(int resolution)
        {
            int side = SideCount(resolution);
            return side * side;
        }

        public static int TriangleCount(int resolution)
        {
            int r = resolution < 1 ? 1 : resolution;
            return r * r * 2;
        }

        public static int IndexCount(int resolution)
        {
            return TriangleCount(resolution) * 3;
        }

        // Lage eines Gitterpunkts in der Flaeche, in Metern, symmetrisch um die Mitte.
        public static void Offset(int resolution, double halfSize, int i, int j,
            out double east, out double north)
        {
            int r = resolution < 1 ? 1 : resolution;
            east = (i / (double)r - 0.5) * 2.0 * halfSize;
            north = (j / (double)r - 0.5) * 2.0 * halfSize;
        }

        // Dreiecke des Gitters. Die Windungsrichtung ist so, dass die Normalen nach oben zeigen,
        // wenn Ost x Nord = oben gilt - sonst waere die Flaeche von oben unsichtbar.
        public static int[] Indices(int resolution)
        {
            int r = resolution < 1 ? 1 : resolution;
            int side = SideCount(resolution);
            int[] indices = new int[IndexCount(resolution)];
            int t = 0;
            for (int j = 0; j < r; j++)
            {
                for (int i = 0; i < r; i++)
                {
                    int a = j * side + i, b = a + 1, c = a + side, d = c + 1;
                    indices[t++] = a; indices[t++] = b; indices[t++] = c;
                    indices[t++] = b; indices[t++] = d; indices[t++] = c;
                }
            }
            return indices;
        }
    }
}
