using System;

namespace BoosterWatch
{
    // Kleine Pixel-Animation fuer das Kamerafenster, solange es kein Bild gibt (Zeitraffer, Booster
    // ausserhalb der Physik): ein Ingenieur in der Missionskontrolle versucht, die Verbindung zum
    // Booster wiederherzustellen. Er tippt, horcht in sein Headset, klopft auf die Konsole, und die
    // Signalbalken klettern - und fallen wieder.
    //
    // Alles wird hier aus Rechtecken, Kreisen und Linien gemalt, ohne Grafikdateien und ohne Unity: der
    // Bildinhalt ist ein RGBA-Feld, Zeile 0 oben. BoosterCamera/FlightWindow laden ihn in eine Textur.
    public sealed class ControlRoomAnimation
    {
        public const int Width = 192, Height = 108;
        public const int FramesPerSecond = 12;
        public const int FrameCount = 96; // 8 s
        public readonly byte[] Pixels = new byte[Width * Height * 4];

        // Die Signalstaerke dieses Bildes (0-4), fuer die Beschriftung darueber.
        public int Signal { get; private set; }
        // Was der Ingenieur gerade tut: 0 tippt, 1 horcht, 2 klopft, 3 hofft.
        public int Action { get; private set; }

        public static int FrameAt(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            return (int)(seconds * FramesPerSecond) % FrameCount;
        }

        public void Render(int frame)
        {
            frame = ((frame % FrameCount) + FrameCount) % FrameCount;
            double t = frame / (double)FramesPerSecond;
            // Ablauf: 0-2,5 s tippen, 2,5-4 s horchen, 4-5 s klopfen, 5-8 s Signal steigt und faellt.
            Action = t < 2.5 ? 0 : t < 4 ? 1 : t < 5 ? 2 : 3;
            bool knock = Action == 2 && ((frame / 2) % 2 == 0);
            int shake = knock ? ((frame % 2 == 0) ? 1 : -1) : 0;
            Signal = Action == 3 ? Math.Min(4, (int)((t - 5) * 2.2) + 1) : Action == 2 && knock ? 1 : 0;
            if (Action == 3 && t > 7.3) Signal = 0; // ... und weg ist es wieder

            // Raum: Wand mit leichtem Verlauf, Boden.
            for (int y = 0; y < Height; y++)
            {
                double k = y / (double)Height;
                Color row = Mix(new Color(18, 24, 38), new Color(28, 36, 54), k);
                FillRect(0, y, Width, 1, row);
            }
            FillRect(0, 84, Width, Height - 84, new Color(22, 26, 34));
            // Deckenlampen-Streifen
            for (int x = 10; x < Width; x += 46) FillRect(x, 0, 24, 2, new Color(70, 80, 96));

            // Grosse Wandanzeige mit Rauschen und "NO SIGNAL".
            int sx = 22 + shake, sy = 6;
            FillRect(sx - 2, sy - 2, 132, 44, new Color(40, 46, 60));
            Screen(sx, sy, 128, 40, frame, Signal, true);
            // Signalbalken rechts daneben
            for (int i = 0; i < 4; i++)
            {
                int h = 5 + i * 4;
                Color c = i < Signal ? (Signal >= 3 ? new Color(90, 230, 150) : new Color(240, 200, 70)) : new Color(50, 58, 74);
                FillRect(160 + i * 6 + shake, 40 - h, 4, h, c);
            }
            // Blinkende Warnlampe
            if (Signal == 0 && frame % 12 < 6) FillCircle(170 + shake, 12, 3, new Color(240, 70, 60));
            else FillCircle(170 + shake, 12, 3, new Color(90, 40, 40));

            // Konsole: Pult mit zwei Monitoren, Tastatur, Lampen, Kaffeetasse.
            FillRect(0, 74, Width, 10, new Color(52, 58, 72));
            FillRect(0, 74, Width, 2, new Color(84, 92, 110));
            FillRect(0, 84, Width, 24, new Color(38, 42, 54));
            Monitor(26 + shake, 52, frame, 0);
            Monitor(128 + shake, 52, frame, 1);
            for (int i = 0; i < 8; i++)
            {
                bool on = ((frame / 3 + i * 5) % 7) < 3;
                FillRect(18 + i * 7, 88, 3, 2, on ? new Color(90, 220, 140) : new Color(40, 70, 55));
                FillRect(150 + i * 5, 88, 2, 2, ((frame / 4 + i) % 5) == 0 ? new Color(240, 190, 60) : new Color(70, 60, 40));
            }
            // Tastatur
            FillRect(76, 76, 40, 5, new Color(28, 30, 38));
            for (int i = 0; i < 9; i++) FillRect(78 + i * 4, 77, 3, 1, new Color(70, 76, 90));
            // Kaffeetasse mit Dampf
            FillRect(172, 67, 7, 7, new Color(210, 90, 60));
            FillRect(179, 69, 2, 3, new Color(210, 90, 60));
            for (int i = 0; i < 3; i++)
            {
                int dy = (frame + i * 4) % 12;
                SetPixel(174 + ((dy / 3) % 2) + i, 65 - dy, new Color(150, 160, 175, 150));
            }

            Engineer(frame, t, knock);

            // Funken beim Klopfen
            if (knock)
                for (int i = 0; i < 6; i++)
                {
                    int px = 70 + (int)(Hash(frame * 7 + i) * 30), py = 66 + (int)(Hash(frame * 13 + i) * 8);
                    SetPixel(px, py, new Color(255, 230, 120));
                }
        }

        // Der Ingenieur, von hinten schraeg: Stuhl, weisses Hemd, Headset. Die Arme zeigen, was er tut.
        private void Engineer(int frame, double t, bool knock)
        {
            int bob = Action == 1 ? 1 : knock ? 2 : 0;
            int cx = 96, baseY = 84;
            Color chair = new Color(30, 32, 40), chairEdge = new Color(60, 64, 78);
            Color shirt = new Color(226, 230, 236), shade = new Color(180, 186, 198);
            Color skin = new Color(222, 176, 140), hair = new Color(86, 56, 36);
            Color tie = new Color(60, 90, 170), headset = new Color(24, 24, 28);

            // Stuhl
            FillRect(cx - 14, baseY - 22, 28, 22, chair);
            FillRect(cx - 14, baseY - 22, 28, 2, chairEdge);
            FillRect(cx - 2, baseY, 4, 10, chair);
            FillRect(cx - 12, baseY + 10, 24, 2, chair);

            // Oberkoerper (ueber der Stuhllehne), leicht nach vorn gebeugt beim Horchen
            int ty = baseY - 38 + bob;
            FillRect(cx - 11, ty + 10, 22, 20, shirt);
            FillRect(cx - 11, ty + 10, 3, 20, shade);
            FillRect(cx + 8, ty + 10, 3, 20, shade);
            FillRect(cx - 1, ty + 10, 2, 5, tie); // Krawattenknoten schaut am Kragen vor
            // Kopf
            int hy = ty + 1;
            FillCircle(cx, hy + 4, 6, skin);
            FillRect(cx - 6, hy - 2, 13, 6, hair);
            FillCircle(cx, hy + 1, 6, hair);
            FillRect(cx - 6, hy + 4, 13, 2, hair);
            // Ohren
            SetPixel(cx - 7, hy + 5, skin); SetPixel(cx + 7, hy + 5, skin);
            // Headset: Buegel und Mikrofon
            FillRect(cx - 7, hy - 3, 15, 1, headset);
            FillRect(cx - 8, hy + 2, 2, 5, headset);
            FillRect(cx + 7, hy + 2, 2, 5, headset);
            Line(cx + 8, hy + 6, cx + 11, hy + 10, headset);
            SetPixel(cx + 11, hy + 10, new Color(200, 60, 60));

            // Arme: Schulter -> Ellbogen -> Hand
            int shoulderL = cx - 10, shoulderR = cx + 10, sy = ty + 12;
            int lhx, lhy, rhx, rhy;
            if (Action == 0)
            {
                // Tippen: Haende wechseln sich ab
                bool left = (frame / 2) % 2 == 0;
                lhx = 86; lhy = left ? 76 : 74; rhx = 106; rhy = left ? 74 : 76;
            }
            else if (Action == 1)
            {
                // Horchen: rechte Hand am Headset, linke auf dem Pult
                lhx = 84; lhy = 75; rhx = cx + 9; rhy = hy + 5;
            }
            else if (Action == 2)
            {
                // Klopfen: rechte Faust auf den Monitor/die Konsole
                lhx = 84; lhy = 75; rhx = 74; rhy = knock ? 70 : 62;
            }
            else
            {
                // Hoffen: beide Haende hoch, bei vollem Signal Jubel, danach haengen sie
                bool cheer = Signal >= 3;
                bool gone = t > 7.3;
                lhx = gone ? 84 : 80; lhy = gone ? 76 : cheer ? ty - 2 : ty + 6;
                rhx = gone ? 108 : 112; rhy = gone ? 76 : cheer ? ty - 2 : ty + 6;
            }
            Arm(shoulderL, sy, lhx, lhy, shade, skin);
            Arm(shoulderR, sy, rhx, rhy, shade, skin);
        }

        private void Arm(int sx, int sy, int hx, int hy, Color sleeve, Color skin)
        {
            int ex = (sx + hx) / 2 + (hx < sx ? -2 : 2), ey = (sy + hy) / 2 + 2;
            ThickLine(sx, sy, ex, ey, 2, sleeve);
            ThickLine(ex, ey, hx, hy, 1, sleeve);
            FillRect(hx - 1, hy - 1, 3, 3, skin);
        }

        private void Monitor(int x, int y, int frame, int which)
        {
            FillRect(x, y, 38, 22, new Color(14, 16, 20));
            FillRect(x + 17, y + 22, 4, 2, new Color(14, 16, 20));
            // Links: gruene Zeilen, die durchlaufen; rechts: Rauschen
            if (which == 0)
            {
                FillRect(x + 2, y + 2, 34, 18, new Color(10, 30, 20));
                for (int i = 0; i < 5; i++)
                {
                    int len = 6 + (int)(Hash((frame / 2 + i) * 31) * 24);
                    FillRect(x + 4, y + 4 + i * 3, len, 1, new Color(80, 220, 130));
                }
            }
            else Screen(x + 2, y + 2, 34, 18, frame + 50, Signal, false);
        }

        // Rauschen; mit Signal ein immer klarer werdendes Bild (Booster-Silhouette am Himmel).
        private void Screen(int x, int y, int w, int h, int frame, int signal, bool label)
        {
            double clarity = signal / 4.0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    double n = Hash((frame * 977 + j * 131 + i * 7919) & 0xFFFFF);
                    int g = (int)(30 + n * 140);
                    Color noise = new Color(g, g, g + 10);
                    Color sky = Mix(new Color(40, 90, 160), new Color(120, 170, 220), j / (double)h);
                    // Booster in der Mitte, Rueckwaertsflug, Flamme darunter
                    int bx = x + w / 2 - x, by = h / 2;
                    bool body = Math.Abs(i - bx) <= Math.Max(1, w / 40) && j > by - h / 4 && j < by + h / 5;
                    bool flame = Math.Abs(i - bx) <= 1 && j >= by + h / 5 && j < by + h / 5 + 3 && (frame % 2 == 0);
                    Color clear = body ? new Color(230, 230, 235) : flame ? new Color(255, 170, 60) : sky;
                    SetPixel(x + i, y + j, Mix(noise, clear, clarity * (0.7 + 0.3 * n)));
                }
            // Rollierender Balken
            int bar = (frame * 3) % (h + 6) - 3;
            FillRect(x, y + bar, w, 2, new Color(200, 200, 210, 60));
            if (label && signal == 0)
            {
                FillRect(x + w / 2 - 22, y + h / 2 - 4, 44, 9, new Color(10, 10, 14, 220));
                Text(x + w / 2 - 17, y + h / 2 - 2, "NO SIGNAL", new Color(240, 80, 70));
            }
        }

        // ---- Zeichnen ----------------------------------------------------------------------------

        public struct Color
        {
            public byte R, G, B, A;
            public Color(int r, int g, int b, int a = 255)
            { R = Clamp(r); G = Clamp(g); B = Clamp(b); A = Clamp(a); }
            private static byte Clamp(int v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }
        }

        private static Color Mix(Color a, Color b, double k)
        {
            k = k < 0 ? 0 : k > 1 ? 1 : k;
            return new Color((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k),
                (int)(a.B + (b.B - a.B) * k), (int)(a.A + (b.A - a.A) * k));
        }

        private void SetPixel(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = (y * Width + x) * 4;
            if (c.A >= 255) { Pixels[i] = c.R; Pixels[i + 1] = c.G; Pixels[i + 2] = c.B; Pixels[i + 3] = 255; return; }
            double k = c.A / 255.0;
            Pixels[i] = (byte)(Pixels[i] + (c.R - Pixels[i]) * k);
            Pixels[i + 1] = (byte)(Pixels[i + 1] + (c.G - Pixels[i + 1]) * k);
            Pixels[i + 2] = (byte)(Pixels[i + 2] + (c.B - Pixels[i + 2]) * k);
            Pixels[i + 3] = 255;
        }

        private void FillRect(int x, int y, int w, int h, Color c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) SetPixel(i, j, c);
        }

        private void FillCircle(int cx, int cy, int r, Color c)
        {
            for (int j = -r; j <= r; j++)
                for (int i = -r; i <= r; i++)
                    if (i * i + j * j <= r * r + r / 2) SetPixel(cx + i, cy + j, c);
        }

        private void Line(int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixel(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void ThickLine(int x0, int y0, int x1, int y1, int r, Color c)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int s = 0; s <= steps; s++)
            {
                double k = steps == 0 ? 0 : s / (double)steps;
                FillCircle((int)Math.Round(x0 + (x1 - x0) * k), (int)Math.Round(y0 + (y1 - y0) * k), r, c);
            }
        }

        // Winzige 3x5-Schrift, nur die Buchstaben, die gebraucht werden.
        private static readonly string[] Glyphs =
        {
            "N:10011101101110011001", "O:111101101101111", "S:111100111001111", "I:111010010010111",
            "G:111100101101111", "A:010101111101101", "L:100100100100111"
        };
        private void Text(int x, int y, string text, Color c)
        {
            foreach (char ch in text)
            {
                int width = 3;
                if (ch != ' ')
                    foreach (string g in Glyphs)
                        if (g[0] == ch)
                        {
                            width = (g.Length - 2) / 5;
                            for (int k = 0; k < g.Length - 2; k++)
                                if (g[2 + k] == '1') SetPixel(x + k % width, y + k / width, c);
                        }
                x += width + 1;
            }
        }

        private static double Hash(int n)
        {
            unchecked
            {
                uint h = (uint)n * 2654435761u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
