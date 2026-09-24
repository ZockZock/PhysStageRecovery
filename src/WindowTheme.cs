using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // Local styles only: never alter KSP's shared skin or other mods' GUI colors.
    internal sealed class WindowTheme : IDisposable
    {
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private Font font;
        public readonly Color Accent = new Color(0.40f, 0.89f, 0.76f);
        public readonly Color Text = new Color(0.91f, 0.94f, 0.97f);
        public readonly Color Secondary = new Color(0.58f, 0.65f, 0.73f);
        public GUIStyle Window, Panel, Inset, Title, Heading, Body, Muted, Small, Value, CompactValue, Button, ActiveButton, Field, Tab, ActiveTab, Badge;
        public GUIStyle FuelTrack, FuelFill, FuelLow;
        public WindowTheme()
        {
            try { font = Font.CreateDynamicFontFromOSFont("Segoe UI", 14); } catch { font = null; }
            Window = Surface(new Color(0.055f, 0.070f, 0.095f, 0.99f), 10);
            Panel = Surface(new Color(0.090f, 0.112f, 0.148f), 7);
            Inset = Surface(new Color(0.030f, 0.045f, 0.065f), 7);
            FuelTrack = Surface(new Color(0.035f, 0.051f, 0.078f), 3);
            FuelFill = Surface(Accent, 3);
            FuelLow = Surface(new Color(0.96f, 0.68f, 0.31f), 3);
            Title = Label(20, Text, FontStyle.Bold);
            Heading = Label(16, Text, FontStyle.Bold);
            Body = Label(14, Text); Body.wordWrap = true;
            Muted = Label(13, Secondary); Muted.wordWrap = true;
            Small = Label(11, Secondary);
            Value = Label(23, Text, FontStyle.Bold);
            CompactValue = Label(17, Text, FontStyle.Bold);
            Button = Control(new Color(0.14f, 0.18f, 0.23f), Text);
            ActiveButton = Control(new Color(0.12f, 0.29f, 0.28f), Accent);
            Field = new GUIStyle(GUI.skin.textField) { font = font ?? GUI.skin.label.font, fontSize = 14,
                alignment = TextAnchor.MiddleRight, padding = new RectOffset(10, 10, 4, 4), border = new RectOffset(6, 6, 6, 6) };
            SetStates(Field, Rounded(new Color(0.035f, 0.051f, 0.078f), 6), Text);
            Field.focused.background = Rounded(new Color(0.10f, 0.22f, 0.23f), 6);
            Field.focused.textColor = Accent;
            Tab = Control(new Color(0.075f, 0.095f, 0.125f), Secondary);
            ActiveTab = Control(new Color(0.12f, 0.22f, 0.24f), Accent);
            Badge = Label(11, Accent, FontStyle.Bold); Badge.alignment = TextAnchor.MiddleRight;
        }
        private GUIStyle Label(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label) { font = font ?? GUI.skin.label.font, fontSize = size,
                fontStyle = weight, richText = false, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
            SetStates(s, null, color); return s;
        }
        private GUIStyle Surface(Color color, int radius)
        {
            var s = new GUIStyle { border = new RectOffset(radius, radius, radius, radius), padding = new RectOffset(0, 0, 0, 0) };
            SetStates(s, Rounded(color, radius), Text); return s;
        }
        private GUIStyle Control(Color color, Color text)
        {
            var s = new GUIStyle(GUI.skin.button) { font = font ?? GUI.skin.label.font, fontSize = 13,
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false,
                border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(8, 8, 3, 3) };
            SetStates(s, Rounded(color, 6), text);
            s.hover.background = Rounded(color * 1.22f, 6); s.hover.textColor = Color.white;
            s.active.background = Rounded(color * 0.8f, 6); return s;
        }
        private static void SetStates(GUIStyle s, Texture2D texture, Color text)
        {
            foreach (GUIStyleState state in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused })
            { state.background = texture; state.textColor = text; }
        }
        private Texture2D Rounded(Color color, int radius)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "PhysStageRecovery.UI"; texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius));
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius));
                Color c = color;
                if (dx > 0 && dy > 0) c.a *= Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                texture.SetPixel(x, y, c);
            }
            texture.Apply(); textures.Add(texture); return texture;
        }
        public void Dispose()
        {
            foreach (Texture2D texture in textures) UnityEngine.Object.Destroy(texture);
            textures.Clear(); if (font != null) UnityEngine.Object.Destroy(font);
        }
    }
}
