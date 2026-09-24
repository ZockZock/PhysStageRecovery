using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BoosterWatch
{
    // Which landing law flies a booster. `legacy` is the ported MechJeb state chain, `predictive`
    // the forecast-driven law. The switch exists so a flight that goes wrong can be repeated on
    // the other path without a rebuild.
    public enum GuidanceMode { Legacy = 0, Predictive = 1 }

    public sealed class Settings
    {
        public readonly RecoveryLimits Limits = new RecoveryLimits();
        public float PhysicsRange = 500000;
        public int MaxBoosters = 8;
        public int CameraFps = 15;
        public bool AutoArm = true;
        public bool AutoStage, PoweredLanding;
        // The predictive law is what gets flown; the ported chain stays one setting away.
        public GuidanceMode GuidanceMode = GuidanceMode.Predictive;
        public bool ModEnabled = true, AutoRecovery = true, CameraEnabled = true, AutoOpenWindow = true;
        public bool ShowDiagnostics;
        public bool HasBehaviorSettings { get; private set; }
        public float CameraDistance = 65, CameraHeading = 35, CameraPitch = 12;
        // On by default: an aligned booster presents its smallest cross-section and would
        // otherwise be destroyed by reentry heat before the landing autopilot can fly.
        public bool HeatImmune = true;
        public int LastAutoStage = 0;
        public double AutoStageHeight = 5000, LandingSpeed = 0.5;
        // What the predictive law aims for at the ground.
        //
        // Deliberately not a field of its own: until 0.9.36 the law read its own key
        // (`touchdownSpeed`) while the window wrote `landingSpeed`, and the two were only equalised
        // once, when the law was introduced. The window could therefore show 5 m/s while the booster
        // aimed at 2. Now the value the player sets in the window *is* the target, by construction -
        // there is no second number that could drift away from it.
        public double TouchdownSpeed { get { return LandingSpeed; } set { LandingSpeed = value; } }
        // Where the vertical final phase starts.
        public double TerminalAltitude = 50;
        public double CaptureAltitude = 100;
        // How far the booster may lean over while it brakes, and how far above local gravity the
        // engines are allowed to be commanded. The reserve is what stays available for attitude
        // control, spool-up error and a forecast that is one second off.
        public double TiltLimit = 20, ThrustReserve = 0.2;
        // Height above the ground at which the automatic canopies open. KSP's own parts open their
        // canopy at a fixed height above the sea; what matters for a landing is the height above the
        // ground, and how much of it the canopy needs to inflate.
        public double ChuteHeight = 1000;
        public float WindowWidth = 720, WindowHeight = 720, WindowX = 35, WindowY = 60;
        private string filePath;
        public static string Path { get { return System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/PhysStageRecovery/PluginData/settings.cfg"); } }

        public static Settings Load() { return Load(Path); }

        public static Settings Load(string path)
        {
            Settings s = new Settings { filePath = path };
            try
            {
                if (!File.Exists(path)) return s;
                ConfigNode root = ConfigNode.Load(path);
                ConfigNode n = root.GetNode("PhysStageRecovery") ?? root.GetNode("BoosterWatch") ?? root;
                s.PhysicsRange = (float)Read(n, "physicsRangeKm", 500, 5, 2000) * 1000;
                s.MaxBoosters = (int)Read(n, "maxBoosters", 8, 1, 16);
                s.CameraFps = (int)Read(n, "cameraFps", 15, 5, 30);
                s.WindowWidth = (float)Read(n, "windowWidth", 720, 480, 7680);
                s.WindowHeight = (float)Read(n, "windowHeight", 720, 540, 4320);
                s.WindowX = (float)Read(n, "windowX", 35, 0, 7680);
                s.WindowY = (float)Read(n, "windowY", 60, 0, 4320);
                s.Limits.SinkSpeed = Read(n, "maxSinkSpeed", 8, 0.5, 20);
                s.Limits.HorizontalSpeed = Read(n, "maxHorizontalSpeed", 3, 0.1, 10);
                s.Limits.TotalSpeed = Read(n, "maxTotalSpeed", 9, 0.5, 25);
                s.Limits.StableSeconds = Read(n, "stableSeconds", 3, 1, 10);
                s.Limits.Acceleration = Read(n, "maxAcceleration", 0.75, 0.05, 2);
                s.Limits.AngularSpeed = Read(n, "maxAngularSpeed", 0.5, 0.05, 2);
                bool b;
                if (bool.TryParse(n.GetValue("autoArm"), out b)) s.AutoArm = b;
                if (bool.TryParse(n.GetValue("autoStage"), out b)) s.AutoStage = b;
                if (bool.TryParse(n.GetValue("poweredLanding"), out b)) s.PoweredLanding = b;
                s.HasBehaviorSettings = n.HasValue("modEnabled") || n.HasValue("autoRecovery");
                if (bool.TryParse(n.GetValue("modEnabled"), out b)) s.ModEnabled = b;
                if (bool.TryParse(n.GetValue("autoRecovery"), out b)) s.AutoRecovery = b;
                if (bool.TryParse(n.GetValue("cameraEnabled"), out b)) s.CameraEnabled = b;
                if (bool.TryParse(n.GetValue("autoOpenWindow"), out b)) s.AutoOpenWindow = b;
                if (bool.TryParse(n.GetValue("showDiagnostics"), out b)) s.ShowDiagnostics = b;
                s.CameraDistance = (float)Read(n, "cameraDistance", 65, 8, 180);
                s.CameraHeading = (float)Read(n, "cameraHeading", 35, -180, 180);
                s.CameraPitch = (float)Read(n, "cameraPitch", 12, -80, 80);
                if (bool.TryParse(n.GetValue("heatImmune"), out b)) s.HeatImmune = b;
                s.LastAutoStage = (int)Read(n, "lastAutoStage", 0, 0, 100);
                s.AutoStageHeight = Read(n, "autoStageHeight", 5000, 50, 70000);
                s.LandingSpeed = Read(n, "landingSpeed", 0.5, 0.5, 5);
                s.TerminalAltitude = Read(n, "terminalAltitude", 50, 1, 1000);
                s.CaptureAltitude = Read(n, "captureAltitude", 100, 10, 5000);
                s.TiltLimit = Read(n, "tiltLimit", 20, 1, 80);
                s.ThrustReserve = Read(n, "thrustReserve", 0.2, 0, 0.6);
                string mode = n.GetValue("guidanceMode");
                if (!string.IsNullOrEmpty(mode))
                    s.GuidanceMode = mode.Trim().ToLowerInvariant() == "legacy" ? GuidanceMode.Legacy : GuidanceMode.Predictive;
                s.ChuteHeight = Read(n, "chuteHeight", 1000, 100, 20000);
                double version = Read(n, "configVersion", 1, 1, 7);
                if (version < 2)
                {
                    // Upgrade only the old defaults. Preserve explicitly customized limits.
                    if (Math.Abs(s.PhysicsRange - 150000) < 1) s.PhysicsRange = 500000;
                    if (s.Limits.SinkSpeed == 5 && s.Limits.TotalSpeed == 6)
                    { s.Limits.SinkSpeed = 8; s.Limits.TotalSpeed = 9; }
                }
                if (version < 5)
                {
                    // The predictive landing law brought its own settings. The old landingSpeed is
                    // what the ported chain aimed for; the new law uses its own key, so carry the
                    // existing value across instead of silently replacing it with the default.
                    if (n.HasValue("landingSpeed")) s.TouchdownSpeed = s.LandingSpeed;
                    s.Save();
                }
                else if (version < 7 || !n.HasValue("captureAltitude")) s.Save();
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Settings: " + e); }
            return s;
        }

        public bool Save()
        {
            string path = filePath ?? Path;
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                ConfigNode root = File.Exists(path) ? ConfigNode.Load(path) : new ConfigNode();
                if (root == null) root = new ConfigNode();
                ConfigNode n = root.GetNode("PhysStageRecovery") ?? root.GetNode("BoosterWatch") ?? root.AddNode("PhysStageRecovery");
                n.name = "PhysStageRecovery";
                n.RemoveValues("useMechJeb");
                n.RemoveValues("requireTouchdown"); n.RemoveValues("recoveryHeight"); n.RemoveValues("touchdownSeconds");
                // The retired second key for the landing target: the window's value is the target now.
                n.RemoveValues("touchdownSpeed");
                Set(n, "configVersion", 7); Set(n, "physicsRangeKm", PhysicsRange / 1000);
                Set(n, "maxBoosters", MaxBoosters); Set(n, "cameraFps", CameraFps);
                n.SetValue("autoArm", AutoArm.ToString(), true);
                n.SetValue("autoStage", AutoStage.ToString(), true);
                n.SetValue("poweredLanding", PoweredLanding.ToString(), true);
                // Legacy per-save switches are migrated by the flight component once the journal loads.
                if (HasBehaviorSettings)
                {
                    n.SetValue("modEnabled", ModEnabled.ToString(), true);
                    n.SetValue("autoRecovery", AutoRecovery.ToString(), true);
                }
                n.SetValue("cameraEnabled", CameraEnabled.ToString(), true);
                n.SetValue("autoOpenWindow", AutoOpenWindow.ToString(), true);
                n.SetValue("showDiagnostics", ShowDiagnostics.ToString(), true);
                Set(n, "cameraDistance", CameraDistance); Set(n, "cameraHeading", CameraHeading); Set(n, "cameraPitch", CameraPitch);
                n.SetValue("heatImmune", HeatImmune.ToString(), true);
                Set(n, "lastAutoStage", LastAutoStage); Set(n, "autoStageHeight", AutoStageHeight);
                Set(n, "landingSpeed", LandingSpeed);
                Set(n, "terminalAltitude", TerminalAltitude);
                Set(n, "captureAltitude", CaptureAltitude);
                Set(n, "tiltLimit", TiltLimit);
                Set(n, "thrustReserve", ThrustReserve);
                n.SetValue("guidanceMode", GuidanceMode == GuidanceMode.Legacy ? "legacy" : "predictive", true);
                Set(n, "chuteHeight", ChuteHeight);
                Set(n, "maxSinkSpeed", Limits.SinkSpeed);
                Set(n, "maxHorizontalSpeed", Limits.HorizontalSpeed); Set(n, "maxTotalSpeed", Limits.TotalSpeed);
                Set(n, "stableSeconds", Limits.StableSeconds); Set(n, "maxAcceleration", Limits.Acceleration);
                Set(n, "maxAngularSpeed", Limits.AngularSpeed);
                Set(n, "windowWidth", WindowWidth); Set(n, "windowHeight", WindowHeight);
                Set(n, "windowX", WindowX); Set(n, "windowY", WindowY);
                root.Save(temporary);
                // Keep one backup of the previous file, then swap the new one in. File.Replace
                // is not available under every file policy, so back up and move explicitly.
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Saving settings failed: " + e); return false; }
        }

        private static void Set(ConfigNode node, string key, double value)
        { node.SetValue(key, value.ToString("R", CultureInfo.InvariantCulture), true); }

        public void SetBehavior(bool enabled, bool recovery)
        { HasBehaviorSettings = true; ModEnabled = enabled; AutoRecovery = recovery; }

        private static double Read(ConfigNode n, string key, double fallback, double min, double max)
        {
            double value;
            if (!double.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || !RecoveryPolicy.Finite(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
