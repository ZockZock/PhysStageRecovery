using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens;

namespace BoosterWatch
{
    // The reserve drawn straight onto the stock fuel gauge of the staging list: the reserved share of
    // the bar gets a hatched look, in place, without a second bar somewhere else.
    //
    // The stock element is public API, so almost nothing has to be reflected: StageManager.Instance.Stages
    // holds one StageGroup per stage, its field inverseStageIndex is the stage number, and its field
    // DeltaVHeadingImage is the gauge. In this KSP that gauge is the box itself (the only other child is
    // the label text, see the log line), and it is a filled UnityEngine.UI.Image.
    //
    // Where the hatch goes is read from the fill, not guessed: the reserve is the fuel that is burned
    // LAST, so it sits at the end of the gauge the fuel drains towards - at the left for a bar filled
    // from the left, at the right for one anchored at the right (fillOrigin), inset by the sprite's
    // border, which is the space the label occupies. The overlay is a RawImage parented to the gauge,
    // first child so the label is drawn over it, and it is clipped to the part that is still filled.
    public sealed partial class BoosterWatchFlight
    {
        private const string OverlayName = "PhysStageRecoveryReserve";
        // The stripe tile is built here and not by the window theme: a theme may only be built inside
        // OnGUI (it touches GUI.skin), and the overlay is attached from the physics tick.
        private static Texture2D stripeTexture;
        private RawImage reserveOverlay;
        private Image gaugeBox;
        private RectTransform overlayHost;
        private string gaugeNote = "", attachNote = "";
        private bool gaugeReported;
        // The box field is the one member of StageGroup that is not public; the stage number is.
        private static FieldInfo gaugeField;
        private static bool gaugeFieldSearched;

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

        private void UpdateReserveOverlay()
        {
            ReserveStatus reserve = activeReserve;
            bool wanted = reserve != null && reserve.Configured && reserve.Armed
                && RecoveryPolicy.Finite(reserve.Reserve) && reserve.Reserve > 0 && reserve.Reserve < 1;
            Image box = wanted ? FindStockGauge(FlightGlobals.ActiveVessel) : null;
            if (box == null)
            {
                DetachReserveOverlay();
                return;
            }
            float labelWidth;
            RectTransform area = BarArea(box.rectTransform, out labelWidth);
            if (area == null) area = box.rectTransform;
            if (reserveOverlay == null || overlayHost != area) AttachReserveOverlay(box, area, reserve);
            if (reserveOverlay == null) return;
            PlaceReserveOverlay(reserveOverlay, area, (float)reserve.Reserve, labelWidth);
        }

        // The bar inside the heading box: the widest Image or RawImage child. A text is a graphic as
        // well, so it is measured separately; when there is no bar child (this KSP), the box itself is
        // the bar and the label width is the inset the visible fill starts behind.
        private RectTransform BarArea(RectTransform box, out float labelWidth)
        {
            labelWidth = 0f;
            RectTransform best = null;
            float bestWidth = 0;
            for (int i = 0; i < box.childCount; i++)
            {
                RectTransform child = box.GetChild(i) as RectTransform;
                if (child == null || child.name == OverlayName) continue;
                Graphic graphic = child.GetComponent<Graphic>();
                if (graphic == null) continue;
                float width = child.rect.width;
                if (!(graphic is Image) && !(graphic is RawImage))
                {
                    // A label: its rect is the whole box, so it is only useful as a fallback inset when
                    // it does not span the box (then it really is the label column).
                    if (width < box.rect.width * 0.95f) labelWidth = Mathf.Max(labelWidth, width);
                    continue;
                }
                if (width <= bestWidth) continue;
                bestWidth = width;
                best = child;
            }
            return best;
        }

        // The hatch. "reserve" is the share of the tank that has to stay; the stock gauge shows what is
        // left, so the hatch marks the end of the fill the fuel drains towards and is clipped to the
        // filled part: once the fuel is down to the reserve, the whole filled bar is hatched.
        private static void PlaceReserveOverlay(RawImage overlay, RectTransform bar, float reserve, float labelWidth)
        {
            Image image = bar.GetComponent<Image>();
            float width = bar.rect.width;
            float left = labelWidth, right = 0f;
            if (image != null && image.sprite != null)
            {
                float ppu = image.sprite.pixelsPerUnit <= 0f ? 100f : image.sprite.pixelsPerUnit;
                Vector4 border = image.sprite.border;
                if (border.x > 0f && border.x / ppu < width * 0.5f) left = Mathf.Max(left, border.x / ppu);
                if (border.z > 0f && border.z / ppu < width * 0.5f) right = border.z / ppu;
            }
            float usable = Mathf.Max(0f, width - left - right);
            float level = FillAmount(bar);
            float share = Mathf.Clamp01(reserve);
            if (!float.IsNaN(level)) share = Mathf.Min(share, Mathf.Clamp01(level));
            float hatch = usable * share;
            bool fromRight = image != null && image.type == Image.Type.Filled
                && image.fillMethod == Image.FillMethod.Horizontal && image.fillOrigin == 1;
            RectTransform rect = overlay.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(fromRight ? width - right - hatch : left, 0f);
            rect.sizeDelta = new Vector2(hatch, 0f);
            overlay.uvRect = new Rect(0f, 0f, Mathf.Max(1f, hatch) / 8f, Mathf.Max(1f, bar.rect.height) / 8f);
        }

        // The gauge of the stage the vessel is on right now. The staging list belongs to the active
        // vessel; a booster being followed has no list of its own.
        private Image FindStockGauge(Vessel vessel)
        {
            if (vessel == null || StageManager.Instance == null) return null;
            List<StageGroup> stages = StageManager.Instance.Stages;
            if (stages == null) return null;
            string seen = "";
            for (int i = 0; i < stages.Count; i++)
            {
                StageGroup group = stages[i];
                if (group == null) continue;
                if (seen.Length < 120) seen += (seen.Length > 0 ? "," : "") + group.inverseStageIndex;
                if (group.inverseStageIndex != vessel.currentStage) continue;
                Image box = HeadingImage(group);
                if (box == null) { ReportGauge(false, "Box am Stufenfeld " + vessel.currentStage + " nicht gefunden"); return null; }
                if (!box.gameObject.activeInHierarchy) { ReportGauge(false, "Box am Stufenfeld " + vessel.currentStage + " ist ausgeblendet"); return null; }
                float labelWidth;
                RectTransform area = BarArea(box.rectTransform, out labelWidth);
                ReportGauge(true, "Stufe " + vessel.currentStage + " Box=" + Size(box.rectTransform)
                    + " " + GraphicType(box.rectTransform)
                    + " Balken=" + (area == null ? "Box selbst (Kuerzel " + labelWidth.ToString("0") + ")"
                        : "'" + area.name + "' " + Size(area) + " " + GraphicType(area))
                    + " Kinder: " + Children(box.rectTransform));
                return box;
            }
            ReportGauge(false, "kein Stufenfeld fuer Stufe " + vessel.currentStage + " (Felder: " + seen + ")");
            return null;
        }

        private static string Children(RectTransform box)
        {
            string text = "";
            for (int i = 0; i < box.childCount; i++)
            {
                RectTransform child = box.GetChild(i) as RectTransform;
                if (child == null) continue;
                if (text.Length > 200) { text += ", ..."; break; }
                text += (text.Length > 0 ? ", " : "") + child.name + "(" + GraphicType(child) + " " + Size(child) + ")";
            }
            return text;
        }

        // StageGroup.DeltaVHeadingImage is private, so it is read by name once and then cached. If a
        // future KSP renames it, the log says so and there is nothing to draw on.
        private static Image HeadingImage(StageGroup group)
        {
            if (!gaugeFieldSearched)
            {
                gaugeFieldSearched = true;
                gaugeField = typeof(StageGroup).GetField("DeltaVHeadingImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gaugeField == null)
                    Debug.LogError("[PhysStageRecovery] Lande-Vorhalt: StageGroup.DeltaVHeadingImage gibt es nicht mehr; "
                        + "die Schraffur bleibt aus, der Vorhalt wirkt weiter.");
            }
            if (gaugeField == null || group == null) return null;
            try { return gaugeField.GetValue(group) as Image; } catch { return null; }
        }

        private static float FillAmount(RectTransform area)
        {
            Image image = area == null ? null : area.GetComponent<Image>();
            if (image == null) return float.NaN;
            try { return image.fillAmount; } catch { return float.NaN; }
        }

        private static string Size(RectTransform area)
        {
            if (area == null) return "-";
            return area.rect.width.ToString("0") + "x" + area.rect.height.ToString("0");
        }

        // Everything about the stock graphic that decides where the hatch goes: the fill direction, the
        // level it currently shows and the sprite border the visible fill starts behind.
        private static string GraphicType(RectTransform area)
        {
            if (area == null) return "-";
            Image image = area.GetComponent<Image>();
            if (image != null)
            {
                string sprite = image.sprite == null ? "ohne Sprite" : image.sprite.name
                    + " border=" + image.sprite.border.ToString("0");
                return image.type + "/" + image.fillMethod + "/Origin" + image.fillOrigin
                    + " fill=" + FillAmount(area).ToString("0.00") + " " + sprite;
            }
            RawImage raw = area.GetComponent<RawImage>();
            if (raw != null) return "RawImage";
            Graphic graphic = area.GetComponent<Graphic>();
            return graphic == null ? "keine Grafik" : graphic.GetType().Name;
        }

        private void ReportGauge(bool found, string note)
        {
            if (gaugeReported && gaugeNote == note) return;
            gaugeReported = true;
            gaugeNote = note;
            if (found) Debug.Log("[PhysStageRecovery] Lande-Vorhalt auf der Stock-Tankanzeige: " + note);
            else Debug.LogWarning("[PhysStageRecovery] Lande-Vorhalt: Stock-Tankanzeige nicht nutzbar - " + note
                + ". Der Vorhalt wirkt weiter, nur die Schraffur fehlt.");
        }

        private void AttachReserveOverlay(Image box, RectTransform area, ReserveStatus reserve)
        {
            DetachReserveOverlay();
            try
            {
                GameObject holder = new GameObject(OverlayName);
                holder.layer = box.gameObject.layer;
                reserveOverlay = holder.AddComponent<RawImage>();
                reserveOverlay.texture = StripeTexture();
                reserveOverlay.color = new Color(1f, 1f, 1f, 0.92f);
                reserveOverlay.raycastTarget = false;
                RectTransform rect = reserveOverlay.rectTransform;
                rect.SetParent(area, false);
                // First child: the gauge draws its own fill first, then the hatch, then the label - so
                // the hatch never covers the label glyphs.
                rect.SetAsFirstSibling();
                rect.localScale = Vector3.one;
                gaugeBox = box;
                overlayHost = area;
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schraffur an '" + area.name + "' eingehaengt ("
                    + FuelReserve.PercentText(100 * reserve.Reserve) + " von " + Size(area) + ", Rest "
                    + FuelReserve.ShareText(reserve.Remaining)
                    + (reserve.Engine.Length > 0 ? ", " + reserve.Engine : "") + ").");
            }
            catch (Exception e)
            {
                if (attachNote != "x")
                {
                    attachNote = "x";
                    Debug.LogError("[PhysStageRecovery] Lande-Vorhalt: Schraffur konnte nicht eingehaengt werden: " + e);
                }
                DetachReserveOverlay();
            }
        }

        private void DetachReserveOverlay()
        {
            gaugeBox = null;
            overlayHost = null;
            if (reserveOverlay == null) return;
            if (reserveOverlay.gameObject != null) Destroy(reserveOverlay.gameObject);
            reserveOverlay = null;
        }
    }
}
