using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens;

namespace BoosterWatch
{
    // The reserve drawn straight onto the stock fuel gauge of the staging list: the reserved share of
    // the bar gets the same hatched look the reference picture shows, in place, without a second bar
    // somewhere else.
    //
    // The stock element is public API, so nothing has to be reflected: StageManager.Instance.Stages
    // holds one StageGroup per stage, its field inverseStageIndex is the stage number, and its field
    // DeltaVHeadingImage is the bar itself (UnityEngine.UI.Image). The overlay is a RawImage that is
    // parented to that bar's RectTransform with its left edge on the bar's left edge and a width of
    // reserve share x bar width - so position, size, UI scale and resolution follow the stock bar on
    // their own, and the stripes are drawn on top of it because a child draws after its parent.
    //
    // Which propellant the stock bar belongs to does not matter here: the reserve is a share of the
    // engine's own tanks, and both fuel and oxidiser are reserved at the same percentage, so the share
    // is the same fraction of any of them.
    public sealed partial class BoosterWatchFlight
    {
        private RawImage reserveOverlay;
        private Image gaugeBar;
        private RectTransform overlayHost;
        private const string OverlayName = "PhysStageRecoveryReserve";
        private string gaugeNote = "";
        private bool gaugeReported;
        // The bar is the one field of StageGroup that is not public; the stage number is.
        private static FieldInfo gaugeField;
        private static bool gaugeFieldSearched;

        private void UpdateReserveOverlay()
        {
            ReserveStatus reserve = activeReserve;
            bool wanted = reserve != null && reserve.Configured && reserve.Armed
                && RecoveryPolicy.Finite(reserve.Reserve) && reserve.Reserve > 0 && reserve.Reserve < 1;
            Image bar = wanted ? FindStockGauge(FlightGlobals.ActiveVessel) : null;
            if (bar == null)
            {
                DetachReserveOverlay();
                return;
            }
            // DeltaVHeadingImage is the box that carries the label and the bar; the bar itself is the
            // widest graphic inside it. The hatch belongs on the bar, not over the label.
            RectTransform area = BarArea(bar.rectTransform);
            if (area == null) area = bar.rectTransform;
            if (reserveOverlay == null || overlayHost != area) AttachReserveOverlay(bar, area, reserve);
            if (reserveOverlay == null) return;
            float width = Mathf.Max(0f, area.rect.width * (float)reserve.Reserve);
            RectTransform rect = reserveOverlay.rectTransform;
            rect.sizeDelta = new Vector2(width, 0f);
            reserveOverlay.uvRect = new Rect(0f, 0f, Mathf.Max(1f, width) / 8f,
                Mathf.Max(1f, area.rect.height) / 8f);
            reserveOverlay.enabled = width > 0.5f;
        }

        // The bar inside the heading box: the widest graphic child that is not the overlay itself. A
        // label is a text object without a graphic, so it never wins here.
        private RectTransform BarArea(RectTransform box)
        {
            RectTransform best = null;
            float bestWidth = 0;
            for (int i = 0; i < box.childCount; i++)
            {
                RectTransform child = box.GetChild(i) as RectTransform;
                if (child == null || child.name == OverlayName) continue;
                if (child.GetComponent<Graphic>() == null) continue;
                float width = child.rect.width;
                if (width <= bestWidth) continue;
                bestWidth = width;
                best = child;
            }
            return best;
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
                Image bar = HeadingImage(group);
                if (bar == null) { ReportGauge(false, "Balken am Stufenfeld " + vessel.currentStage + " nicht gefunden"); return null; }
                if (!bar.gameObject.activeInHierarchy) { ReportGauge(false, "Balken am Stufenfeld " + vessel.currentStage + " ist ausgeblendet"); return null; }
                RectTransform area = BarArea(bar.rectTransform) ?? bar.rectTransform;
                ReportGauge(true, "Stufe " + vessel.currentStage + " Box=" + Size(bar.rectTransform)
                    + " Flaeche='" + area.name + "' " + Size(area)
                    + " Typ=" + GraphicType(area) + " fill=" + FillAmount(area).ToString("0.00"));
                return bar;
            }
            ReportGauge(false, "kein Stufenfeld fuer Stufe " + vessel.currentStage + " (Felder: " + seen + ")");
            return null;
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

        // The bar's fill, for the log only: it tells whether the found element really is a level gauge.
        // The overlay never uses it - the reserve share is what gets drawn either way.
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
            if (image != null) return image.type.ToString() + (image.sprite != null ? "/" + image.sprite.name : "");
            RawImage raw = area.GetComponent<RawImage>();
            if (raw != null) return "RawImage" + (raw.texture != null ? "/" + raw.texture.name : "");
            return "keine Grafik";
        }

        private void ReportGauge(bool found, string note)
        {
            if (gaugeReported && gaugeNote == note) return;
            gaugeReported = true;
            gaugeNote = note;
            if (found) Debug.Log("[PhysStageRecovery] Lande-Vorhalt auf der Stock-Tankanzeige: " + note);
            else Debug.LogWarning("[PhysStageRecovery] Lande-Vorhalt: Stock-Tankanzeige nicht nutzbar - " + note
                + ". Der Vorhalt bleibt im eigenen Feld sichtbar.");
        }

        private void AttachReserveOverlay(Image bar, RectTransform area, ReserveStatus reserve)
        {
            DetachReserveOverlay();
            try
            {
                EnsureWindowTheme();
                GameObject holder = new GameObject(OverlayName);
                holder.layer = bar.gameObject.layer;
                reserveOverlay = holder.AddComponent<RawImage>();
                reserveOverlay.texture = windowTheme.ReserveStripe;
                reserveOverlay.color = new Color(1f, 1f, 1f, 0.92f);
                reserveOverlay.raycastTarget = false;
                RectTransform rect = reserveOverlay.rectTransform;
                rect.SetParent(area, false);
                // Left edge on the bar's left edge, height following the bar, so everything about the
                // stock layout (anchors, scaling, pivots) is inherited instead of recomputed.
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(0f, 0f);
                gaugeBar = bar;
                overlayHost = area;
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schraffur an '" + area.name + "' eingehaengt ("
                    + FuelReserve.PercentText(100 * reserve.Reserve) + " von " + Size(area)
                    + ", Rest " + FuelReserve.ShareText(reserve.Remaining)
                    + (reserve.Engine.Length > 0 ? ", " + reserve.Engine : "") + ").");
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Lande-Vorhalt: Overlay konnte nicht eingehaengt werden: " + e);
                DetachReserveOverlay();
            }
        }

        private void DetachReserveOverlay()
        {
            gaugeBar = null;
            overlayHost = null;
            if (reserveOverlay == null) return;
            if (reserveOverlay.gameObject != null) Destroy(reserveOverlay.gameObject);
            reserveOverlay = null;
        }
    }
}
