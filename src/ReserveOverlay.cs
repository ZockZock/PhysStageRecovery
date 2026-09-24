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
            if (reserveOverlay == null || gaugeBar != bar) AttachReserveOverlay(bar, reserve);
            if (reserveOverlay == null) return;
            float width = Mathf.Max(0f, bar.rectTransform.rect.width * (float)reserve.Reserve);
            RectTransform rect = reserveOverlay.rectTransform;
            rect.sizeDelta = new Vector2(width, 0f);
            reserveOverlay.uvRect = new Rect(0f, 0f, Mathf.Max(1f, width) / 8f,
                Mathf.Max(1f, bar.rectTransform.rect.height) / 8f);
            reserveOverlay.enabled = width > 0.5f;
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
                ReportGauge(true, "Stufe " + vessel.currentStage + " rect=" + bar.rectTransform.rect.width.ToString("0") + "x"
                    + bar.rectTransform.rect.height.ToString("0") + " fill=" + FillAmount(bar).ToString("0.00"));
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

        // The stock bar is a filled Image; the fill tells whether it is really the level gauge. It is
        // only logged, never used for the overlay: the reserve share is what is drawn either way.
        private static float FillAmount(Image bar)
        {
            try { return bar.fillAmount; } catch { return float.NaN; }
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

        private void AttachReserveOverlay(Image bar, ReserveStatus reserve)
        {
            DetachReserveOverlay();
            try
            {
                EnsureWindowTheme();
                GameObject holder = new GameObject("PhysStageRecoveryReserve");
                holder.layer = bar.gameObject.layer;
                reserveOverlay = holder.AddComponent<RawImage>();
                reserveOverlay.texture = windowTheme.ReserveStripe;
                reserveOverlay.color = new Color(1f, 1f, 1f, 0.92f);
                reserveOverlay.raycastTarget = false;
                RectTransform rect = reserveOverlay.rectTransform;
                rect.SetParent(bar.rectTransform, false);
                // Left edge on the bar's left edge, height following the bar, so everything about the
                // stock layout (anchors, scaling, pivots) is inherited instead of recomputed.
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(0f, 0f);
                gaugeBar = bar;
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Overlay am Stock-Balken eingehaengt ("
                    + FuelReserve.PercentText(100 * reserve.Reserve) + ").");
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
            if (reserveOverlay == null) return;
            if (reserveOverlay.gameObject != null) Destroy(reserveOverlay.gameObject);
            reserveOverlay = null;
        }

        // The overlay replaces the own field while it is on screen; the field stays for the booster
        // being followed and as the fallback when the stock bar cannot be used.
        private bool ReserveOverlayActive { get { return reserveOverlay != null && reserveOverlay.enabled; } }
    }
}
