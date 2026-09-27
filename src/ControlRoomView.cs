using UnityEngine;

namespace BoosterWatch
{
    // Bringt die Pixel-Animation (ControlRoomAnimation) in eine Textur fuer das Kamerafenster. Die Zeit
    // ist die echte, nicht die Spielzeit: im Zeitraffer laeuft die Animation normal schnell.
    public sealed class ControlRoomView
    {
        private readonly ControlRoomAnimation animation = new ControlRoomAnimation();
        private readonly Color32[] colors = new Color32[ControlRoomAnimation.Width * ControlRoomAnimation.Height];
        private Texture2D texture;
        private int shown = -1;

        public int Action { get { return animation.Action; } }
        public int Signal { get { return animation.Signal; } }

        public Texture2D Frame(float seconds)
        {
            if (texture == null)
            {
                texture = new Texture2D(ControlRoomAnimation.Width, ControlRoomAnimation.Height, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                shown = -1;
            }
            int frame = ControlRoomAnimation.FrameAt(seconds);
            if (frame == shown) return texture;
            shown = frame;
            animation.Render(frame);
            byte[] p = animation.Pixels;
            int w = ControlRoomAnimation.Width, h = ControlRoomAnimation.Height;
            // Zeile 0 der Animation ist oben, in Unity-Texturen unten.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    colors[(h - 1 - y) * w + x] = new Color32(p[i], p[i + 1], p[i + 2], 255);
                }
            texture.SetPixels32(colors);
            texture.Apply(false);
            return texture;
        }

        public void Dispose()
        {
            if (texture != null) Object.Destroy(texture);
            texture = null;
        }
    }
}
