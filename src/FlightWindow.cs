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
            GUI.Label(new Rect(window.width - 125, 17, 60, 22), "0.9.35", windowTheme.Small);
            if (GUI.Button(new Rect(window.width - 48, 14, 30, 28), new GUIContent("×", Loc.Get("#PSR_Window_Close")), windowTheme.Button)) SetVisible(false);
            GUI.Label(new Rect(19, 44, w, 20), Loc.Get("#PSR_Window_Tagline"), windowTheme.Small);

            bool mod = Switch(new Rect(18, 77, 147, 30), Loc.Get("#PSR_Window_Mod"), enabledMod);
            if (mod != enabledMod) ToggleMod(mod);
            bool recovery = Switch(new Rect(177, 77, 176, 30), Loc.Get("#PSR_Window_AutoRecovery"), autoRecovery);
            if (recovery != autoRecovery)
            {
                autoRecovery = recovery; settings.SetBehavior(enabledMod, autoRecovery); settings.Save();
                if (RecoveryJournal.Instance != null) RecoveryJournal.Instance.AutoRecovery = autoRecovery;
            }
            if (window.width >= 620)
                GUI.Label(new Rect(375, 77, window.width - 393, 30),
                    faulted ? Loc.Get("#PSR_Window_Fault") : Loc.Get("#PSR_Window_Tracked", boosters.Count(b => !b.Finished)),
                    windowTheme.Badge);

            float tabWidth = (w - 8) / 2;
            if (GUI.Button(new Rect(18, 121, tabWidth, 32), Loc.Get("#PSR_Window_TabOverview"), settingsOpen ? windowTheme.Tab : windowTheme.ActiveTab))
            { settingsOpen = false; GUI.FocusControl(null); }
            if (GUI.Button(new Rect(26 + tabWidth, 121, tabWidth, 32), Loc.Get("#PSR_Window_TabSettings"), settingsOpen ? windowTheme.ActiveTab : windowTheme.Tab))
            { settingsOpen = true; GUI.FocusControl(null); }
            cameraViewport = new Rect();
            if (settingsOpen) DrawSettings(); else DrawOverview();

            GUI.Label(new Rect(20, window.height - 30, w - 100, 20),
                Loc.Get("#PSR_Window_Footer", (settings.PhysicsRange / 1000).ToString("0")), windowTheme.Small);
            GUI.Label(new Rect(window.width - 96, window.height - 30, 56, 20), "Alt + B", windowTheme.Small);
            GUI.Label(new Rect(window.width - 23, window.height - 23, 18, 18), "◢", windowTheme.Muted);
            GUI.DragWindow(new Rect(0, 0, window.width - 55, 67));
        }
        private bool Switch(Rect rect, string label, bool value)
        {
            string state = Loc.Get(value ? "#PSR_Window_On" : "#PSR_Window_Off");
            return GUI.Button(rect, label + "  ·  " + state, value ? windowTheme.ActiveButton : windowTheme.Button) ? !value : value;
        }
        private void DrawOverview()
        {
            float w = window.width - 36;
            if (boosters.Count == 0)
            {
                DrawGuide(new Rect(18, 168, w, window.height - 212));
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
            string overlay = faulted ? notice : b.Finished ? Loc.Get("#PSR_Window_TrackingDone")
                : !cameraEnabled ? Loc.Get("#PSR_Window_CameraOff")
                : cameraFeed.Error ?? (!live ? Loc.Get("#PSR_Window_WaitingVideo")
                    : cameraFeed.HasFrame ? "" : Loc.Get("#PSR_Window_ConnectingCamera"));
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
                GUI.Label(new Rect(39, cameraViewport.yMax - 34, w - 42, 26), Loc.Get("#PSR_Window_CameraHint"), windowTheme.Small);
            }
            float y = cameraViewport.yMax + 12, card = narrow ? (w - 10) / 2 : (w - 30) / 4;
            Metric(new Rect(18, y, card, 63), Loc.Get("#PSR_Metric_Clearance"), Number(b.Sample.Clearance, " m"));
            bool validReadout = b.Sample.PhysicsActive || b.Finished;
            Metric(new Rect(28 + card, y, card, 63), Loc.Get("#PSR_Metric_SurfaceSpeed"),
                Number(validReadout ? b.Readout.SurfaceSpeed : double.NaN, " m/s"));
            Metric(new Rect(narrow ? 18 : 38 + 2 * card, narrow ? y + 73 : y, card, 63),
                Loc.Get("#PSR_Metric_Distance"), Number(b.Distance / 1000, " km"));
            FuelMetric(new Rect(narrow ? 28 + card : 48 + 3 * card, narrow ? y + 73 : y, card, 63),
                validReadout ? b.Readout.FuelFraction : double.NaN,
                validReadout ? b.Readout.RemainingDeltaV : double.NaN);
            y += metricsHeight + 10;
            string state = PrimaryStatus(b);
            if (!faulted && enabledMod && !b.Finished && b.Sample.PhysicsActive
                && b.Landing.OwnsControl && b.Landing.Predictive)
                state = Loc.Get(Guidance.DescentPhases.Tag(b.Landing.Phase));
            GUI.Label(new Rect(20, y, w - 155, 28), new GUIContent(Loc.Get("#PSR_Window_State", state), state), windowTheme.Heading);
            double throttle = b.Finished ? 0 : validReadout ? b.Readout.Throttle : double.NaN;
            GUI.Label(new Rect(window.width - 166, y, 146, 28), new GUIContent(
                Loc.Get("#PSR_Window_Throttle", RecoveryPolicy.Finite(throttle) ? (100 * throttle).ToString("0") : "—"),
                Loc.Get("#PSR_Window_ThrottleHint")), windowTheme.Heading);
        }
        // What the window says while no booster is tracked: the state, then why nothing was taken over,
        // then the guide. The guide is longer than the window has room for at its smallest size, so it
        // scrolls - with the mouse wheel over it, exactly like the settings tab.
        private Vector2 guideScroll;
        private void DrawGuide(Rect area)
        {
            GUI.Box(area, "", windowTheme.Panel);
            float width = area.width - 32;
            float y = area.y + 12;
            GUI.Label(new Rect(area.x + 16, y, width, 30), faulted ? Loc.Get("#PSR_Status_TrackingStopped")
                : enabledMod ? Loc.Get("#PSR_Window_Ready") : Loc.Get("#PSR_Window_ModOff"), windowTheme.Heading);
            y += 34;
            string message = GuideMessage();
            if (message.Length > 0)
            {
                float height = windowTheme.Muted.CalcHeight(new GUIContent(message), width);
                GUI.Label(new Rect(area.x + 16, y, width, height), message, windowTheme.Muted);
                y += height + 8;
            }
            Rect view = new Rect(area.x + 8, y, area.width - 16, Mathf.Max(60, area.yMax - y - 8));
            string[] titles = GuideTitles();
            string[] texts = GuideTexts();
            float inner = view.width - 26;
            float content = 0;
            for (int i = 0; i < titles.Length; i++)
                content += GuideHeight(titles[i], texts[i], inner);
            guideScroll = GUI.BeginScrollView(view, guideScroll, new Rect(0, 0, inner + 4, content));
            float block = 0;
            for (int i = 0; i < titles.Length; i++)
            {
                float titleHeight = windowTheme.Body.CalcHeight(new GUIContent(titles[i]), inner);
                GUI.Label(new Rect(10, block, inner, titleHeight), titles[i], windowTheme.Body);
                block += titleHeight + 1;
                float textHeight = windowTheme.Muted.CalcHeight(new GUIContent(texts[i]), inner);
                GUI.Label(new Rect(10, block, inner, textHeight), texts[i], windowTheme.Muted);
                block += textHeight + 12;
            }
            GUI.EndScrollView();
        }
        private float GuideHeight(string title, string text, float width)
        {
            return windowTheme.Body.CalcHeight(new GUIContent(title), width) + 1
                + windowTheme.Muted.CalcHeight(new GUIContent(text), width) + 12;
        }
        // The one line that explains the state: an error, a stage that was not taken over, or the
        // rocket whose separations are being watched.
        private string GuideMessage()
        {
            if (faulted) return notice;
            if (skipNotice.Length > 0) return skipNotice;
            return initialized ? notice : "";
        }
        private string[] GuideTitles()
        {
            return new[]
            {
                Loc.Get("#PSR_Help_IntroTitle"), Loc.Get("#PSR_Help_TrackedTitle"), Loc.Get("#PSR_Help_ChutesTitle"),
                Loc.Get("#PSR_Help_PoweredTitle"), Loc.Get("#PSR_Help_GearTitle"), Loc.Get("#PSR_Help_AutoStageTitle"),
                Loc.Get("#PSR_Help_RecoveryTitle"), Loc.Get("#PSR_Help_WarpTitle")
            };
        }
        // Two of the texts name a value from the settings, so the guide cannot contradict them.
        private string[] GuideTexts()
        {
            return new[]
            {
                Loc.Get("#PSR_Help_Intro"),
                Loc.Get("#PSR_Help_Tracked", settings.MaxBoosters),
                Loc.Get("#PSR_Help_Chutes", settings.ChuteHeight.ToString("0") + " m"),
                Loc.Get("#PSR_Help_Powered"),
                Loc.Get("#PSR_Help_Gear"),
                Loc.Get("#PSR_Help_AutoStage"),
                Loc.Get("#PSR_Help_Recovery"),
                Loc.Get("#PSR_Help_Warp")
            };
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
            GUI.Label(new Rect(rect.x + 12, rect.y + 5, rect.width - 24, 18), Loc.Get("#PSR_Metric_RemainingDv"), windowTheme.Small);
            string value = RecoveryPolicy.Finite(deltaV) ? deltaV.ToString("N0", CultureInfo.CurrentCulture) + " m/s" : "—";
            GUIStyle style = windowTheme.Value.CalcSize(new GUIContent(value)).x > rect.width - 24
                ? windowTheme.CompactValue : windowTheme.Value;
            GUI.Label(new Rect(rect.x + 12, rect.y + 22, rect.width - 24, 27),
                new GUIContent(value, Loc.Get("#PSR_Metric_RemainingDvHint")), style);
            Rect bar = new Rect(rect.x + 12, rect.y + 53, rect.width - 24, 6);
            GUI.Box(bar, "", windowTheme.FuelTrack);
            if (RecoveryPolicy.Finite(fraction) && fraction > 0)
                GUI.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01((float)fraction), bar.height), "",
                    fraction < 0.15 ? windowTheme.FuelLow : windowTheme.FuelFill);
            GUI.Label(bar, new GUIContent("", RecoveryPolicy.Finite(fraction)
                ? Loc.Get("#PSR_Metric_Fuel", (100 * fraction).ToString("0"))
                : Loc.Get("#PSR_Metric_FuelUnknown")), windowTheme.Small);
        }

        private string PrimaryStatus(TrackedBooster b)        {
            if (faulted) return Loc.Get("#PSR_Status_TrackingStopped");
            if (b.Finished) return b.ImpactFailed ? Loc.Get("#PSR_Status_LandingFailed") : Loc.Get("#PSR_Status_TrackingFinished");
            if (!enabledMod) return Loc.Get("#PSR_Status_ModOff");
            if (!b.Sample.PhysicsActive) return Loc.Get("#PSR_Status_WaitingSimulation");
            // An uncontrollable booster cannot be landed under power whatever the autopilot does, so
            // the window names that instead of a descent state that will never happen. While canopies
            // are carrying it, the chute landing is the better answer.
            if (b.ControlMissing && settings.PoweredLanding && b.OpenChutes == 0) return Loc.Get("#PSR_Status_NoControl");
            if (b.Sample.Sink < 0) return Loc.Get("#PSR_Status_Climbing");
            if (b.Landing.InFinalDescent) return Loc.Get("#PSR_Status_FinalApproach");
            if (b.OpenChutes > 0) return Loc.Get("#PSR_Status_ChuteLanding");
            return b.Landing.OwnsControl ? Loc.Get("#PSR_Status_PoweredLanding") : Loc.Get("#PSR_Status_DescentTracked");
        }
        private void DrawSettings()
        {
            float w = window.width - 36;
            Rect viewport = new Rect(18, 168, w, window.height - 272);
            float contentWidth = w - 20;
            bool wide = contentWidth >= 760;
            float contentHeight = wide ? 406 : 620;
            settingsScroll = GUI.BeginScrollView(viewport, settingsScroll, new Rect(0, 0, contentWidth, contentHeight));
            SettingsCard(new Rect(0, 0, contentWidth, 128), Loc.Get("#PSR_Settings_Tracking"), Loc.Get("#PSR_Settings_TrackingHint"));
            rangeInput = SettingField(0, 74, contentWidth, Loc.Get("#PSR_Settings_Range"), rangeInput, "km",
                Loc.Get("#PSR_Settings_RangeHint"), "BWRange");
            float col = wide ? (contentWidth - 14) / 2 : contentWidth;
            SettingsCard(new Rect(0, 142, col, 214), Loc.Get("#PSR_Settings_AutoStage"), Loc.Get("#PSR_Settings_AutoStageHint"));
            autoStageInput = Switch(new Rect(16, 203, col - 32, 30), Loc.Get("#PSR_Settings_AutoStage"), autoStageInput);
            bool enabled = GUI.enabled; GUI.enabled = enabled && autoStageInput;
            stageHeightInput = SettingField(0, 246, col, Loc.Get("#PSR_Settings_StageHeight"), stageHeightInput, "m",
                Loc.Get("#PSR_Settings_StageHeightHint"), "BWStageHeight");
            lastStageInput = SettingField(0, 293, col, Loc.Get("#PSR_Settings_LastStage"), lastStageInput, "",
                Loc.Get("#PSR_Settings_LastStageHint"), "BWLastStage");
            GUI.enabled = enabled;
            float x = wide ? col + 14 : 0, y = wide ? 142 : 370;
            SettingsCard(new Rect(x, y, col, 236), Loc.Get("#PSR_Settings_Powered"), Loc.Get("#PSR_Settings_PoweredHint"));
            poweredInput = Switch(new Rect(x + 16, y + 61, col - 32, 30), Loc.Get("#PSR_Settings_PoweredToggle"), poweredInput);
            GUI.enabled = enabled && poweredInput;
            landingSpeedInput = SettingField(x, y + 105, col, Loc.Get("#PSR_Settings_LandingSpeed"), landingSpeedInput, "m/s",
                Loc.Get("#PSR_Settings_LandingSpeedHint"), "BWLandingSpeed");
            GUI.enabled = enabled;
            chuteHeightInput = SettingField(x, y + 149, col, Loc.Get("#PSR_Settings_ChuteHeight"), chuteHeightInput, "m",
                Loc.Get("#PSR_Settings_ChuteHeightHint"), "BWChuteHeight");
            GUI.EndScrollView();
            GUI.Label(new Rect(20, window.height - 96, w - 171, 47), settingsMessage.Length > 0 ? settingsMessage : Loc.Get("#PSR_Settings_Footer"), windowTheme.Muted);
            if (GUI.Button(new Rect(window.width - 181, window.height - 94, 163, 36), Loc.Get("#PSR_Settings_Save"), windowTheme.ActiveButton)) ApplySettings();
            if (settings.ShowDiagnostics && GUI.Button(new Rect(20, window.height - 62, 155, 24), Loc.Get("#PSR_Settings_Diagnostics"), windowTheme.Button)) LogDiagnostics();
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
            { settingsMessage = Loc.Get("#PSR_Settings_RangeError"); return; }
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
                settingsMessage = Loc.Get("#PSR_Settings_SaveFailed"); return;
            }
            foreach (TrackedBooster b in boosters)
            {
                if (b.Finished || b.Vessel == null) continue;
                if (enabledMod && !faulted && oldRange != settings.PhysicsRange) b.RequestRange(settings.PhysicsRange);
                if (oldPowered != settings.PoweredLanding || oldSpeed != settings.LandingSpeed)
                { b.Policy.Reset(); b.Landing.Stop("Einstellungen geändert", false); }
            }
            settingsMessage = Loc.Get("#PSR_Settings_Saved"); GUI.FocusControl(null);
        }
        private static bool ParseSetting(string input, double min, double max, out double value)
        { return double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && RecoveryPolicy.Finite(value) && value >= min && value <= max; }
    }
}
