using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace BoosterWatch
{
    // The landing reserve, shown where KSP shows its own stage information: as a stock information box
    // next to the stage icon - the small captioned bar with the progress fill that the reference picture
    // points at. That element is public API in KSP, so nothing has to be drawn into someone else's gauge
    // and nothing has to be reflected:
    //
    //   StageManager.Instance.Stages        one StageGroup per stage
    //   group.Icons                         its StageIcons; StageIcon.Part says which part an icon is
    //   StageIcon.DisplayInfo()             creates an information box at that icon (StageIconInfoBox)
    //   box.SetCaption / SetMessage         its two text lines
    //   box.SetValue(value, min, max)       the progress fill
    //   box.SetProgressBarColor / BgColor   the fill and track colours
    //   StageIcon.RemoveInfo(box)           gives the box back
    //
    // The bar is read against the reserve itself: SetValue(level, reserve, 1) leaves the bar empty
    // exactly when the tanks are down to the reserve and turns it orange at that moment - the reading
    // "from here the landing is living off its reserve". The box lives and dies with its stage icon,
    // which is also what happens on staging, so it is re-created whenever it is gone.
    public sealed partial class BoosterWatchFlight
    {
        private static readonly Color BoxOk = new Color(0.40f, 0.89f, 0.76f);
        private static readonly Color BoxOnReserve = new Color(0.96f, 0.68f, 0.31f);
        private StageIconInfoBox reserveBox;
        private StageIcon reserveIcon;
        private string boxNote = "";
        private bool boxReported;

        private void UpdateReserveBox()
        {
            ReserveStatus reserve = activeReserve;
            if (reserve == null || !reserve.Configured || !reserve.Armed || !(reserve.Reserve > 0))
            {
                RemoveReserveBox();
                return;
            }
            StageIcon icon = FindReserveIcon(reserve, FlightGlobals.ActiveVessel);
            if (icon == null)
            {
                RemoveReserveBox();
                ReportBox("kein Stufen-Icon fuer das Triebwerk gefunden");
                return;
            }
            if (reserveBox == null || reserveIcon != icon)
            {
                RemoveReserveBox();
                AttachReserveBox(icon);
            }
            if (reserveBox == null) return;
            double share = Math.Max(0, Math.Min(0.99, reserve.Reserve));
            double level = double.IsNaN(reserve.Remaining) ? 1 : Math.Max(0, Math.Min(1, reserve.Remaining));
            try
            {
                reserveBox.SetCaption("Vorhalt " + FuelReserve.PercentText(100 * share));
                reserveBox.SetMessage("Rest " + FuelReserve.ShareText(level));
                reserveBox.SetProgressBarColor(level <= share ? BoxOnReserve : BoxOk);
                reserveBox.SetValue((float)level, (float)share, 1f);
            }
            catch (Exception e) { ReportBox("Kaestchen liess sich nicht beschriften: " + e.Message); }
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

        private void AttachReserveBox(StageIcon icon)
        {
            try
            {
                reserveBox = icon.DisplayInfo();
                if (reserveBox == null)
                {
                    ReportBox("KSP hat kein Informationskaestchen frei (maxInfoBoxes=" + StageIcon.maxInfoBoxes + ")");
                    return;
                }
                reserveIcon = icon;
                boxNote = "";
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Stock-Infokaestchen am Stufen-Icon angebracht ("
                    + (icon.Part != null ? icon.Part.partInfo.title : "?") + ").");
            }
            catch (Exception e) { ReportBox("DisplayInfo fehlgeschlagen: " + e.Message); }
        }

        private void RemoveReserveBox()
        {
            if (reserveBox == null) { reserveIcon = null; return; }
            try
            {
                if (reserveIcon != null && reserveIcon.gameObject != null) reserveIcon.RemoveInfo(reserveBox);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PhysStageRecovery] Lande-Vorhalt: Kaestchen konnte nicht zurueckgegeben werden: " + e.Message);
            }
            reserveBox = null;
            reserveIcon = null;
        }

        // One line per reason, so a missing box is never a guess.
        private void ReportBox(string note)
        {
            if (boxReported && boxNote == note) return;
            boxReported = true;
            boxNote = note;
            Debug.LogWarning("[PhysStageRecovery] Lande-Vorhalt: " + note
                + ". Der Vorhalt wirkt weiter, nur die Anzeige am Stufen-Icon fehlt.");
        }
    }
}
