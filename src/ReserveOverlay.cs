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
    // DeltaVHeadingImage is the box that carries the gauge (UnityEngine.UI.Image). Inside that box the
    // bar is the widest Image/RawImage child; a label is a graphic too (TextMeshPro) but never the bar.
    // The overlay is a RawImage parented to the bar with its left edge on the bar's left edge and a
    // width of reserve share x bar width - so position, size, UI scale and resolution follow the stock
    // bar on their own, and the stripes are drawn on top of it because a child draws after its parent.
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
            float inset;
            RectTransform area = BarArea(box.rectTransform, out inset);
            if (area == null)
            {
                area = box.rectTransform;
                inset = 0f;
            }
            if (reserveOverlay == null || overlayHost != area) AttachReserveOverlay(box, area, inset, reserve);
            if (reserveOverlay == null) return;
            float span = Mathf.Max(0f, area.rect.width - inset);
            float width = Mathf.Max(0f, span * (float)reserve.Reserve);
            RectTransform rect = reserveOverlay.rectTransform;
            rect.anchoredPosition = new Vector2(inset, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
            reserveOverlay.uvRect = new Rect(0f, 0f, Mathf.Max(1f, width) / 8f,
                Mathf.Max(1f, area.rect.height) / 8f);
        }

        // The bar inside the heading box: the widest Image or RawImage child. A text is a graphic as
        // well, so it is measured separately - if there is no bar child at all, the box itself is the
        // bar and the hatch starts behind the label.
        private RectTransform BarArea(RectTransform box, out float inset)
        {
            inset = 0f;
            RectTransform best = null;
            float bestWidth = 0, labelWidth = 0;
            for (int i = 0; i < box.childCount; i++)
            {
                RectTransform child = box.GetChild(i) as RectTransform;
                if (child == null || child.name == OverlayName) continue;
                Graphic graphic = child.GetComponent<Graphic>();
                if (graphic == null) continue;
                bool bar = graphic is Image || graphic is RawImage;
                float width = child.rect.width;
                if (!bar) { labelWidth = Mathf.Max(labelWidth, width); continue; }
                if (width <= bestWidth) continue;
                bestWidth = width;
                best = child;
            }
            if (best != null) return best;
            // No bar child: the box itself is the bar, and the hatch starts behind the label.
            if (labelWidth > 0 && labelWidth < box.rect.width) inset = labelWidth;
            return null;
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
                float inset;
                RectTransform area = BarArea(box.rectTransform, out inset);
                ReportGauge(true, "Stufe " + vessel.currentStage + " Box=" + Size(box.rectTransform)
                    + " Balken=" + (area == null ? "Box selbst" : "'" + area.name + "' " + Size(area) + " " + GraphicType(area))
                    + (area == null && inset > 0 ? " (Kuerzel " + inset.ToString("0") + " px)" : "")
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
        // future KSP renames it, the log says so and the own field stays the display.
        private static Image HeadingImage(StageGroup group)
        {
            if (!gaugeFieldSearched)
            {
                gaugeFieldSearched = true;
                gaugeField = typeof(StageGroup).GetField("DeltaVHeadingImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gaugeField == null)
                    Debug.LogError("[PhysStageRecovery] Lande-Vorhalt: StageGroup.DeltaVHeadingImage gibt es nicht mehr; "
                        + "die Anzeige bleibt im eigenen Feld.");
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

        private static string GraphicType(RectTransform area)
        {
            if (area == null) return "-";
            Image image = area.GetComponent<Image>();
            if (image != null) return image.type + "/fill=" + FillAmount(area).ToString("0.00");
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
                + ". Der Vorhalt bleibt im Triebwerksmenue ablesbar.");
        }

        private void AttachReserveOverlay(Image box, RectTransform area, float inset, ReserveStatus reserve)
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
                // Left edge on the bar's left edge (plus the label, where the box itself is the bar),
                // height following the bar, so everything about the stock layout is inherited instead
                // of recomputed.
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(inset, 0f);
                rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(0f, 0f);
                gaugeBox = box;
                overlayHost = area;
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schraffur an '" + area.name + "' eingehaengt ("
                    + FuelReserve.PercentText(100 * reserve.Reserve) + " von " + Size(area)
                    + (inset > 0 ? ", Kuerzel " + inset.ToString("0") : "") + ", Rest "
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
