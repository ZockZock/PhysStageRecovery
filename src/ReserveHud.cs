using System;
using UnityEngine;

namespace BoosterWatch
{
    // The reserve as an always-on display, independent of the big window: a small field with the
    // reserved share hatched at the left end of the bar, next to where the fuel readouts are. It shows
    // itself only while a reserve is configured somewhere, is dragged with the left mouse button and
    // keeps its place - so it can sit exactly at the fuel gauge of one's choice. The reserve is the
    // share of the engine's own tanks that may not be burned during the ascent; once the fuel drops
    // into the hatched part, the landing is living off its reserve.
    public sealed partial class BoosterWatchFlight
    {
        private const float ReserveHudWidth = 306, ReserveHudHeight = 62;
        private Rect reserveHud;
        private bool reserveHudReady;

        // Which reserve the field shows: the rocket being flown as long as it carries one, otherwise
        // the selected booster, which owns the reserve once it has separated.
        private ReserveStatus ReserveForHud()
        {
            if (activeReserve.Configured) return activeReserve;
            if (boosters.Count == 0) return null;
            TrackedBooster b = boosters[Mathf.Clamp(selection, 0, boosters.Count - 1)];
            // A booster that has landed and been recovered is gone; its last reading must not keep the
            // field on screen.
            if (b.Finished || b.Vessel == null) return null;
            return b.Readout.Reserve.Configured ? b.Readout.Reserve : null;
        }

        // Returns whether the pointer is on the field, so the stock camera keeps still while it is
        // dragged.
        private bool DrawReserveHud()
        {
            ReserveStatus reserve = ReserveForHud();
            if (reserve == null || !reserve.Configured) return false;
            if (!reserveHudReady)
            {
                reserveHudReady = true;
                reserveHud = new Rect(settings.ReserveHudX >= 0 ? settings.ReserveHudX : 8,
                    settings.ReserveHudY >= 0 ? settings.ReserveHudY : Mathf.Max(120, Screen.height * 0.55f),
                    ReserveHudWidth, ReserveHudHeight);
            }
            Rect before = reserveHud;
            reserveHud.x = Mathf.Clamp(reserveHud.x, 0, Mathf.Max(0, Screen.width - reserveHud.width));
            reserveHud.y = Mathf.Clamp(reserveHud.y, 0, Mathf.Max(0, Screen.height - reserveHud.height));
            reserveHud = GUI.Window(GetInstanceID() + 1, reserveHud, DrawReserveHudWindow, "", windowTheme.Window);
            if (reserveHud != before) geometryDirty = true;
            return reserveHud.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        private void DrawReserveHudWindow(int id)
        {
            ReserveStatus reserve = ReserveForHud();
            if (reserve == null || !reserve.Configured) return;
            float w = reserveHud.width;
            string hint = reserve.Engine.Length > 0 ? reserve.Engine : reserve.Worth;
            GUI.Label(new Rect(12, 5, w - 108, 18), new GUIContent("LANDE-VORHALT", hint), windowTheme.Small);
            GUI.Label(new Rect(w - 96, 3, 84, 22),
                new GUIContent(FuelReserve.PercentText(100 * reserve.Reserve), reserve.Worth), windowTheme.CompactValue);
            ReserveBar(new Rect(12, 27, w - 24, 8), reserve);
            GUI.Label(new Rect(12, 38, w - 24, 18), new GUIContent(reserve.StateText, reserve.Worth), windowTheme.Small);
            GUI.DragWindow(new Rect(0, 0, w, 26));
        }

        // The bar of the reserve field. It is read against the engine's OWN tanks - the same
        // denominator the reserve is a share of - and drawn in two parts: the reserved share on the
        // left, hatched, and above it what may still be burned. Once the fill drops into the hatched
        // part, the landing is living off its reserve.
        private void ReserveBar(Rect bar, ReserveStatus reserve)
        {
            GUI.Box(bar, "", windowTheme.FuelTrack);
            double level = RecoveryPolicy.Finite(reserve.Remaining) ? reserve.Remaining : double.NaN;
            float width = RecoveryPolicy.Finite(level) ? Mathf.Clamp01((float)level) * bar.width : 0;
            float limit = Mathf.Clamp01((float)reserve.Reserve) * bar.width;
            float hatched = Mathf.Min(width, limit);
            if (hatched > 0.5f)
                GUI.DrawTextureWithTexCoords(new Rect(bar.x, bar.y, hatched, bar.height), windowTheme.ReserveStripe,
                    new Rect(0, 0, Mathf.Max(1, hatched) / 8f, Mathf.Max(1, bar.height) / 8f));
            if (width > limit)
                GUI.Box(new Rect(bar.x + limit, bar.y, width - limit, bar.height), "", windowTheme.FuelFill);
            GUI.Box(new Rect(bar.x + limit - 1, bar.y - 1, 2, bar.height + 2), "", windowTheme.FuelMark);
            GUI.Label(bar, new GUIContent("", "Vorhalt " + FuelReserve.PercentText(100 * reserve.Reserve)
                + " der eigenen Tanks, Rest " + FuelReserve.ShareText(reserve.Remaining)
                + "\n" + reserve.Worth), windowTheme.Small);
        }
    }
}
