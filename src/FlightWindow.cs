using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BoosterWatch
{
    public sealed partial class BoosterWatchFlight
    {
        private WindowTheme windowTheme;
        private void EnsureWindowTheme() { if (windowTheme == null) windowTheme = new WindowTheme(); }
        private void DrawWindow(int id)
        {
            float w = window.width - 36;
            GUI.Label(new Rect(18, 12, w - 120, 30), "PhysStageRecovery", windowTheme.Title);
            GUI.Label(new Rect(window.width - 125, 17, 60, 22), "0.9.30", windowTheme.Small);
            if (GUI.Button(new Rect(window.width - 48, 14, 30, 28), new GUIContent("×", "Fenster schließen"), windowTheme.Button)) SetVisible(false);
            GUI.Label(new Rect(19, 44, w, 20), "STUFEN LANDEN. MISSION FORTSETZEN.", windowTheme.Small);

            bool mod = Switch(new Rect(18, 77, 147, 30), "Mod", enabledMod);
            if (mod != enabledMod) ToggleMod(mod);
            bool recovery = Switch(new Rect(177, 77, 176, 30), "Auto-Bergung", autoRecovery);
            if (recovery != autoRecovery)
            {
                autoRecovery = recovery; settings.SetBehavior(enabledMod, autoRecovery); settings.Save();
                if (RecoveryJournal.Instance != null) RecoveryJournal.Instance.AutoRecovery = autoRecovery;
            }
            if (window.width >= 620)
                GUI.Label(new Rect(375, 77, window.width - 393, 30), faulted ? "FEHLER" : boosters.Count(b => !b.Finished) + " IN VERFOLGUNG", windowTheme.Badge);

            float tabWidth = (w - 8) / 2;
            if (GUI.Button(new Rect(18, 121, tabWidth, 32), "Flugübersicht", settingsOpen ? windowTheme.Tab : windowTheme.ActiveTab))
            { settingsOpen = false; GUI.FocusControl(null); }
            if (GUI.Button(new Rect(26 + tabWidth, 121, tabWidth, 32), "Einstellungen", settingsOpen ? windowTheme.ActiveTab : windowTheme.Tab))
            { settingsOpen = true; GUI.FocusControl(null); }
            cameraViewport = new Rect();
            if (settingsOpen) DrawSettings(); else DrawOverview();

            GUI.Label(new Rect(20, window.height - 30, w - 100, 20),
                (settings.PhysicsRange / 1000).ToString("0") + " km Reichweite  ·  Bergung bei Bodenkontakt", windowTheme.Small);
            GUI.Label(new Rect(window.width - 96, window.height - 30, 56, 20), "Alt + B", windowTheme.Small);
            GUI.Label(new Rect(window.width - 23, window.height - 23, 18, 18), "◢", windowTheme.Muted);
            GUI.DragWindow(new Rect(0, 0, window.width - 55, 67));
        }
        private bool Switch(Rect rect, string label, bool value)
        {
            return GUI.Button(rect, label + (value ? "  ·  AN" : "  ·  AUS"), value ? windowTheme.ActiveButton : windowTheme.Button) ? !value : value;
        }
        private void DrawOverview()
        {
            float w = window.width - 36;
            if (boosters.Count == 0)
            {
                Rect empty = new Rect(18, 168, w, window.height - 212);
                GUI.Box(empty, "", windowTheme.Panel);
                float emptyY = empty.y + Mathf.Max(24, (empty.height - 180) / 2);
                GUI.Label(new Rect(38, emptyY, w - 40, 32), faulted ? "Verfolgung angehalten" : enabledMod ? "Bereit für die nächste Stufe" : "Mod ist ausgeschaltet", windowTheme.Heading);
                GUI.Label(new Rect(38, emptyY + 44, w - 40, 62), faulted ? notice :
                    "Sobald sich ein Booster mit Fallschirmen oder nutzbarem Landetriebwerk trennt, öffnet sich dieses Fenster automatisch.", windowTheme.Body);
                GUI.Label(new Rect(38, emptyY + 117, w - 40, 50), initialized ? notice : "Du steuerst deine Rakete. Wir verfolgen die abgetrennten Stufen.", windowTheme.Muted);
                return;
            }
            TrackedBooster b = boosters[Mathf.Clamp(selection, 0, boosters.Count - 1)];
            if (GUI.Button(new Rect(18, 168, 32, 30), "‹", windowTheme.Button)) Select(-1);
            GUI.Label(new Rect(61, 168, w - 88, 30), new GUIContent((selection + 1) + " / " + boosters.Count + "   " + b.Name, b.Name), windowTheme.Heading);
            if (GUI.Button(new Rect(window.width - 50, 168, 32, 30), "›", windowTheme.Button)) Select(1);
            bool narrow = w < 650;
            float metricsHeight = narrow ? 136 : 63;
            float imageHeight = Mathf.Max(100, window.height - 310 - metricsHeight);
            cameraViewport = new Rect(18, 210, w, imageHeight);
            GUI.Box(cameraViewport, "", windowTheme.Inset);
            bool live = b.Vessel != null && b.Vessel.loaded && !b.Vessel.packed && !b.Finished;
            if (cameraEnabled && cameraFeed.Texture != null && cameraFeed.HasFrame && (live || b.Finished))
                GUI.DrawTexture(cameraViewport, cameraFeed.Texture, ScaleMode.ScaleToFit, false);
            string overlay = faulted ? notice : b.Finished ? "VERFOLGUNG BEENDET" : !cameraEnabled ? "Kamera in settings.cfg deaktiviert"
                : cameraFeed.Error ?? (!live ? "Warte auf Livebild" : cameraFeed.HasFrame ? "" : "Kamera wird verbunden …");
            if (overlay.Length > 0)
            {
                GUI.Box(new Rect(28, 220, w - 20, 42), "", windowTheme.Panel);
                GUI.Label(new Rect(40, 220, w - 44, 42), overlay, windowTheme.Body);
            }
            else
                GUI.Label(new Rect(32, 222, 105, 20), "● LIVE", windowTheme.Badge);
            Vector2 mouse = new Vector2(Input.mousePosition.x - window.x, Screen.height - Input.mousePosition.y - window.y);
            if (cameraEnabled && live && cameraViewport.Contains(mouse))
            {
                GUI.Box(new Rect(28, cameraViewport.yMax - 34, w - 20, 26), "", windowTheme.Inset);
                GUI.Label(new Rect(39, cameraViewport.yMax - 34, w - 42, 26), "Rechte Maustaste: drehen   ·   Mausrad: zoomen", windowTheme.Small);
            }
            float y = cameraViewport.yMax + 12, card = narrow ? (w - 10) / 2 : (w - 30) / 4;
            Metric(new Rect(18, y, card, 63), "BODENABSTAND", Number(b.Sample.Clearance, " m"));
            bool validReadout = b.Sample.PhysicsActive || b.Finished;
            Metric(new Rect(28 + card, y, card, 63), "SURFACE SPEED",
                Number(validReadout ? b.Readout.SurfaceSpeed : double.NaN, " m/s"));
            Metric(new Rect(narrow ? 18 : 38 + 2 * card, narrow ? y + 73 : y, card, 63),
                "ENTFERNUNG", Number(b.Distance / 1000, " km"));
            FuelMetric(new Rect(narrow ? 28 + card : 48 + 3 * card, narrow ? y + 73 : y, card, 63),
                validReadout ? b.Readout.FuelFraction : double.NaN,
                validReadout ? b.Readout.RemainingDeltaV : double.NaN);
            y += metricsHeight + 10;
            string state = PrimaryStatus(b);
            if (!faulted && enabledMod && !b.Finished && b.Sample.PhysicsActive
                && b.Landing.OwnsControl && b.Landing.Predictive)
                state = Guidance.DescentPhases.Name(b.Landing.Phase);
            GUI.Label(new Rect(20, y, w - 155, 28), new GUIContent("Zustand: " + state, state), windowTheme.Heading);
            double throttle = b.Finished ? 0 : validReadout ? b.Readout.Throttle : double.NaN;
            GUI.Label(new Rect(window.width - 166, y, 146, 28), new GUIContent(
                "Schub " + (RecoveryPolicy.Finite(throttle) ? (100 * throttle).ToString("0") : "—") + " %",
                "Angewandte Drosselstellung nach der Schubsperre"), windowTheme.Heading);
        }
        private void Metric(Rect rect, string label, string value)
        {
            GUI.Box(rect, "", windowTheme.Panel);
            GUI.Label(new Rect(rect.x + 12, rect.y + 5, rect.width - 24, 20), label, windowTheme.Small);
            GUIStyle style = windowTheme.Value.CalcSize(new GUIContent(value)).x > rect.width - 24 ? windowTheme.CompactValue : windowTheme.Value;
            GUI.Label(new Rect(rect.x + 12, rect.y + 25, rect.width - 24, 32), new GUIContent(value, value), style);
        }
        private void FuelMetric(Rect rect, double fraction, double deltaV)
        {
            GUI.Box(rect, "", windowTheme.Panel);
            GUI.Label(new Rect(rect.x + 12, rect.y + 5, rect.width - 24, 18), "REST-Δv", windowTheme.Small);
            string value = RecoveryPolicy.Finite(deltaV) ? deltaV.ToString("N0", CultureInfo.CurrentCulture) + " m/s" : "—";
            GUIStyle style = windowTheme.Value.CalcSize(new GUIContent(value)).x > rect.width - 24
                ? windowTheme.CompactValue : windowTheme.Value;
            GUI.Label(new Rect(rect.x + 12, rect.y + 22, rect.width - 24, 27),
                new GUIContent(value, "Verbleibendes Δv im Vakuum für die Landetriebwerke"), style);
            Rect bar = new Rect(rect.x + 12, rect.y + 53, rect.width - 24, 6);
            GUI.Box(bar, "", windowTheme.FuelTrack);
            if (RecoveryPolicy.Finite(fraction) && fraction > 0)
                GUI.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01((float)fraction), bar.height), "",
                    fraction < 0.15 ? windowTheme.FuelLow : windowTheme.FuelFill);
            GUI.Label(bar, new GUIContent("", RecoveryPolicy.Finite(fraction)
                ? "Landetreibstoff: " + (100 * fraction).ToString("0") + " % der Tankkapazität"
                : "Kein Tankfüllstand verfügbar"), windowTheme.Small);
        }

        private string PrimaryStatus(TrackedBooster b)        {
            if (faulted) return "Verfolgung angehalten";
            if (b.Finished) return b.ImpactFailed ? "Landung fehlgeschlagen" : "Verfolgung abgeschlossen";
            if (!enabledMod) return "Mod ausgeschaltet";
            if (!b.Sample.PhysicsActive) return "Warte auf Simulation";
            if (b.Sample.Sink < 0) return "Steigflug";
            if (b.Landing.InFinalDescent) return "Endanflug";
            if (b.OpenChutes > 0) return "Fallschirmlandung";
            return b.Landing.OwnsControl ? "Triebwerkslandung" : "Sinkflug wird verfolgt";
        }
        private void DrawSettings()
        {
            float w = window.width - 36;
            Rect viewport = new Rect(18, 168, w, window.height - 272);
            float contentWidth = w - 20;
            bool wide = contentWidth >= 760;
            float contentHeight = wide ? 406 : 620;
            settingsScroll = GUI.BeginScrollView(viewport, settingsScroll, new Rect(0, 0, contentWidth, contentHeight));
            SettingsCard(new Rect(0, 0, contentWidth, 128), "Verfolgung", "Reichweite für abgetrennte Stufen");
            rangeInput = SettingField(0, 74, contentWidth, "Physikreichweite", rangeInput, "km", "5–2000 km", "BWRange");
            float col = wide ? (contentWidth - 14) / 2 : contentWidth;
            SettingsCard(new Rect(0, 142, col, 214), "Autostaging", "Stufen im Sinkflug automatisch auslösen");
            autoStageInput = Switch(new Rect(16, 203, col - 32, 30), "Autostaging", autoStageInput);
            bool enabled = GUI.enabled; GUI.enabled = enabled && autoStageInput;
            stageHeightInput = SettingField(0, 246, col, "Auslösehöhe", stageHeightInput, "m", "50–70000 m über Grund", "BWStageHeight");
            lastStageInput = SettingField(0, 293, col, "Letzte Stufe", lastStageInput, "", "0–100, einschließlich", "BWLastStage");
            GUI.enabled = enabled;
            float x = wide ? col + 14 : 0, y = wide ? 142 : 370;
            SettingsCard(new Rect(x, y, col, 236), "Triebwerkslandung", "Automatischer Anflug und Bodenkontakt");
            poweredInput = Switch(new Rect(x + 16, y + 61, col - 32, 30), "Landeautomat", poweredInput);
            GUI.enabled = enabled && poweredInput;
            landingSpeedInput = SettingField(x, y + 105, col, "Ziel-Sinken", landingSpeedInput, "m/s", "0,5–5 m/s", "BWLandingSpeed");
            GUI.enabled = enabled;
            chuteHeightInput = SettingField(x, y + 149, col, "Schirmhöhe", chuteHeightInput, "m",
                "100–20000 m über Grund", "BWChuteHeight");
            GUI.EndScrollView();
            GUI.Label(new Rect(20, window.height - 96, w - 171, 47), settingsMessage.Length > 0 ? settingsMessage : "Änderungen gelten für alle verfolgten Stufen.", windowTheme.Muted);
            if (GUI.Button(new Rect(window.width - 181, window.height - 94, 163, 36), "Speichern", windowTheme.ActiveButton)) ApplySettings();
            if (settings.ShowDiagnostics && GUI.Button(new Rect(20, window.height - 62, 155, 24), "Diagnose protokollieren", windowTheme.Button)) LogDiagnostics();
        }
        private void SettingsCard(Rect rect, string title, string subtitle)
        {
            GUI.Box(rect, "", windowTheme.Panel);
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width - 32, 27), title, windowTheme.Heading);
            GUI.Label(new Rect(rect.x + 16, rect.y + 36, rect.width - 32, 22), subtitle, windowTheme.Muted);
        }
        private string SettingField(float x, float y, float width, string label, string value, string unit, string hint, string control)
        {
            GUI.Label(new Rect(x + 16, y, width - 165, 21), label, windowTheme.Body);
            GUI.Label(new Rect(x + 16, y + 22, width - 165, 18), hint, windowTheme.Small);
            GUI.SetNextControlName(control);
            string result = GUI.TextField(new Rect(x + width - 140, y + 2, 85, 32), value, 8, windowTheme.Field);
            GUI.Label(new Rect(x + width - 48, y + 2, 38, 32), unit, windowTheme.Muted);
            return result;
        }
        private void ApplySettings()
        {
            double range, altitude, speed, chute; int stage;
            if (!ParseSetting(rangeInput, 5, 2000, out range) || !ParseSetting(stageHeightInput, 50, 70000, out altitude)
                || !int.TryParse(lastStageInput, out stage) || stage < 0 || stage > 100 || !ParseSetting(landingSpeedInput, 0.5, 5, out speed)
                || !ParseSetting(chuteHeightInput, 100, 20000, out chute))
            { settingsMessage = "Bitte die angegebenen Zahlenbereiche beachten."; return; }
            float oldRange = settings.PhysicsRange;
            bool oldAuto = settings.AutoStage, oldPowered = settings.PoweredLanding;
            double oldAltitude = settings.AutoStageHeight, oldSpeed = settings.LandingSpeed, oldChute = settings.ChuteHeight;
            int oldStage = settings.LastAutoStage;
            settings.PhysicsRange = (float)(range * 1000); settings.AutoStage = autoStageInput;
            settings.AutoStageHeight = altitude; settings.LastAutoStage = stage;
            settings.PoweredLanding = poweredInput; settings.LandingSpeed = speed;
            settings.ChuteHeight = chute;
            if (!settings.Save())
            {
                settings.PhysicsRange = oldRange; settings.AutoStage = oldAuto; settings.AutoStageHeight = oldAltitude;
                settings.LastAutoStage = oldStage; settings.PoweredLanding = oldPowered; settings.LandingSpeed = oldSpeed;
                settings.ChuteHeight = oldChute;
                settingsMessage = "Speichern fehlgeschlagen. Details stehen im KSP.log."; return;
            }
            foreach (TrackedBooster b in boosters)
            {
                if (b.Finished || b.Vessel == null) continue;
                if (enabledMod && !faulted && oldRange != settings.PhysicsRange) b.RequestRange(settings.PhysicsRange);
                if (oldPowered != settings.PoweredLanding || oldSpeed != settings.LandingSpeed)
                { b.Policy.Reset(); b.Landing.Stop("Einstellungen geändert", false); }
            }
            settingsMessage = "Gespeichert. Auf laufende Stufen angewendet."; GUI.FocusControl(null);
        }
        private static bool ParseSetting(string input, double min, double max, out double value)
        { return double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && RecoveryPolicy.Finite(value) && value >= min && value <= max; }
    }
}

