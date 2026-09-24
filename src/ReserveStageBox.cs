using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens;

namespace BoosterWatch
{
    // The landing reserve, drawn INTO the stock resource box of the staging list - the small captioned
    // bar (e.g. "FT" for LiquidFuel) that KSP puts next to a stage icon. The reserved share of that bar
    // is hatched, so the bar itself shows what has to stay: exactly the reading the reference picture
    // shows, in the element the player already looks at.
    //
    // Everything used here is public API, so nothing is guessed:
    //
    //   StageManager.Instance.Stages -> StageGroup.Icons     the stage icons (StageIcon.Part = part)
    //   icon.GetComponentsInChildren<StageIconInfoBox>()     the boxes at that icon
    //   box.GetComponentInChildren<Slider>()                 its bar: value, minValue, maxValue,
    //                                                        fillRect, direction (uGUI Slider)
    //   fillRect.parent                                      the full track the fill runs in
    //
    // The box that belongs to the engine's propellant is found by its own label (the resource
    // abbreviation or display name, read from the box's text components). The hatch is a RawImage
    // added to the track, after the fill, clamped to the filled part: once the tanks are down to the
    // reserve, the whole filled bar is hatched.
    public sealed partial class BoosterWatchFlight
    {
        private const string HatchName = "PhysStageRecoveryReserve";
        private static Texture2D stripeTexture;

        private sealed class BoxMark
        {
            public StageIconInfoBox Box;
            public RawImage Hatch;
            public Slider Slider;
            public RectTransform Track;
        }

        private readonly List<BoxMark> marks = new List<BoxMark>();
        private string boxNote = "";
        private bool boxReported;

        private static Texture2D StripeTexture()
        {
            if (stripeTexture != null) return stripeTexture;
            const int size = 8;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "PhysStageRecovery.ReserveStripe";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;
            Color stripe = new Color(0.40f, 0.89f, 0.76f, 0.85f);
            Color background = new Color(0.05f, 0.12f, 0.13f, 0.85f);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, (x + y) % size < 3 ? stripe : background);
            texture.Apply();
            stripeTexture = texture;
            return texture;
        }

        private void UpdateReserveBox()
        {
            ReserveStatus reserve = activeReserve;
            if (reserve == null || !reserve.Configured || !reserve.Armed || !(reserve.Reserve > 0))
            {
                ClearBoxMarks();
                return;
            }
            StageIcon icon = FindReserveIcon(reserve, FlightGlobals.ActiveVessel);
            if (icon == null)
            {
                ClearBoxMarks();
                ReportBox("kein Stufen-Icon fuer das Triebwerk gefunden");
                return;
            }
            if (MarksLost(icon)) { ClearBoxMarks(); BuildBoxMarks(icon, reserve); }
            for (int i = 0; i < marks.Count; i++) PlaceMark(marks[i], (float)reserve.Reserve);
        }

        private bool MarksLost(StageIcon icon)
        {
            if (marks.Count == 0) return true;
            for (int i = 0; i < marks.Count; i++)
            {
                BoxMark mark = marks[i];
                if (mark.Hatch == null || mark.Box == null || mark.Track == null) return true;
                if (!mark.Box.transform.IsChildOf(icon.transform)) return true;
            }
            return false;
        }

        private void BuildBoxMarks(StageIcon icon, ReserveStatus reserve)
        {
            StageIconInfoBox[] boxes = icon.GetComponentsInChildren<StageIconInfoBox>(true);
            string labels = "";
            for (int i = 0; i < boxes.Length; i++)
            {
                StageIconInfoBox box = boxes[i];
                if (box == null) continue;
                string text = BoxText(box);
                if (labels.Length < 160) labels += (labels.Length > 0 ? ", " : "") + "'" + text.Trim() + "'";
                if (!Matches(text, reserve.Markers)) continue;
                Slider slider = box.GetComponentInChildren<Slider>(true);
                if (slider == null || slider.fillRect == null)
                {
                    ReportBox("Kaestchen '" + text.Trim() + "' hat keinen Balken (Slider) - Schraffur nicht moeglich");
                    continue;
                }
                RectTransform track = slider.fillRect.parent as RectTransform ?? slider.fillRect;
                BoxMark mark = new BoxMark { Box = box, Slider = slider, Track = track };
                mark.Hatch = CreateHatch(track);
                if (mark.Hatch != null) marks.Add(mark);
            }
            if (marks.Count > 0)
            {
                ReportBox("");
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schraffur in " + marks.Count
                    + " Stock-Kaestchen des Triebwerks (Kaestchen: " + labels + ").");
                return;
            }
            ReportBox("kein Ressourcen-Kaestchen zum Vorhalt gefunden (Kaestchen am Icon: "
                + (labels.Length > 0 ? labels : "keine") + "; gesucht: " + reserve.Markers + ")");
        }

        private RawImage CreateHatch(RectTransform track)
        {
            try
            {
                GameObject holder = new GameObject(HatchName);
                holder.layer = track.gameObject.layer;
                RawImage image = holder.AddComponent<RawImage>();
                image.texture = StripeTexture();
                image.color = new Color(1f, 1f, 1f, 0.9f);
                image.raycastTarget = false;
                RectTransform rect = image.rectTransform;
                rect.SetParent(track, false);
                // After the fill, so the hatch is drawn on top of it; the label sits in another part of
                // the box and stays readable.
                rect.SetAsLastSibling();
                rect.localScale = Vector3.one;
                return image;
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Lande-Vorhalt: Schraffur konnte nicht eingehaengt werden: " + e);
                return null;
            }
        }

        // The hatch. "reserve" is the share of the tank that has to stay; the stock bar shows the level,
        // so the hatch marks the end of the fill the fuel drains towards and is clipped to the filled
        // part. Direction and level come from the uGUI Slider, not from a guess.
        private static void PlaceMark(BoxMark mark, float reserve)
        {
            Slider slider = mark.Slider;
            RectTransform rect = mark.Hatch.rectTransform;
            RectTransform track = mark.Track;
            float span = slider.maxValue - slider.minValue;
            float level = span > 0f ? Mathf.Clamp01((slider.value - slider.minValue) / span) : 1f;
            float share = Mathf.Clamp01(Mathf.Min(reserve, level));
            float width = track.rect.width, height = track.rect.height;
            bool vertical = slider.direction == Slider.Direction.BottomToTop
                || slider.direction == Slider.Direction.TopToBottom;
            bool reverse = slider.direction == Slider.Direction.RightToLeft
                || slider.direction == Slider.Direction.TopToBottom;
            if (vertical)
            {
                float span2 = height * share;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, reverse ? height - span2 : 0f);
                rect.sizeDelta = new Vector2(0f, span2);
                mark.Hatch.uvRect = new Rect(0f, 0f, Mathf.Max(1f, width) / 8f, Mathf.Max(1f, span2) / 8f);
                return;
            }
            float pixels = width * share;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(reverse ? width - pixels : 0f, 0f);
            rect.sizeDelta = new Vector2(pixels, 0f);
            mark.Hatch.uvRect = new Rect(0f, 0f, Mathf.Max(1f, pixels) / 8f, Mathf.Max(1f, height) / 8f);
        }

        // The icon of the part that carries the reserve; if that part has no icon of its own (grouped or
        // collapsed staging), the icon of the stage the vessel is on.
        private static StageIcon FindReserveIcon(ReserveStatus reserve, Vessel vessel)
        {
            if (StageManager.Instance == null) return null;
            List<StageGroup> stages = StageManager.Instance.Stages;
            if (stages == null) return null;
            StageIcon fallback = null;
            for (int i = 0; i < stages.Count; i++)
            {
                StageGroup group = stages[i];
                if (group == null) continue;
                List<StageIcon> icons = group.Icons;
                if (icons == null) continue;
                if (vessel != null && group.inverseStageIndex == vessel.currentStage && icons.Count > 0)
                    fallback = icons[icons.Count - 1];
                if (reserve.Part == null) continue;
                for (int j = 0; j < icons.Count; j++)
                {
                    StageIcon icon = icons[j];
                    if (icon != null && icon.Part == reserve.Part) return icon;
                }
            }
            return fallback;
        }

        // All texts of the box: the caption is a TextMeshPro text, and reading "text" generically keeps
        // this mod free of a TextMeshPro reference.
        private static string BoxText(StageIconInfoBox box)
        {
            string text = "";
            Component[] parts = box.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                string value = TextOf(parts[i]);
                if (value.Length > 0) text += (text.Length > 0 ? " " : "") + value;
            }
            return text;
        }

        private static string TextOf(Component component)
        {
            if (component == null) return "";
            try
            {
                PropertyInfo property = component.GetType().GetProperty("text", typeof(string));
                if (property == null || !property.CanRead) return "";
                return (string)property.GetValue(component, null) ?? "";
            }
            catch { return ""; }
        }

        private static bool Matches(string text, string markers)
        {
            if (text.Length == 0 || markers.Length == 0) return false;
            string[] wanted = markers.Split('|');
            for (int i = 0; i < wanted.Length; i++)
            {
                string marker = wanted[i].Trim();
                if (marker.Length < 2) continue;
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void ClearBoxMarks()
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].Hatch != null && marks[i].Hatch.gameObject != null)
                    Destroy(marks[i].Hatch.gameObject);
            }
            marks.Clear();
        }

        // One line per reason, so a missing hatch is never a guess.
        private void ReportBox(string note)
        {
            if (boxReported && boxNote == note) return;
            boxReported = true;
            boxNote = note;
            if (note.Length == 0) return;
            Debug.LogWarning("[PhysStageRecovery] Lande-Vorhalt: " + note
                + ". Der Vorhalt wirkt weiter, nur die Schraffur im Kaestchen fehlt.");
        }
    }
}
