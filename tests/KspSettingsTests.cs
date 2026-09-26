using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BoosterWatch;

class KspSettingsTests
{
    static string managed;
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    static int Main(string[] args)
    {
        managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string path = System.IO.Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run(args[1]);
        Console.WriteLine(checks + " settings checks passed using KSP ConfigNode (no flight simulation).");
        return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        JournalEntry staged = new JournalEntry { Id = Guid.NewGuid(), Name = "Detached booster", Status = "Tracking",
            Time = 123, Funds = 456, StageCursor = 2, AnchorPart = 123456 };
        staged.FailedParts.Add(400); staged.FailedParts.Add(401);
        ConfigNode journalRoot = new ConfigNode(); staged.Write(journalRoot.AddNode("BOOSTER"));
        string journalPath = System.IO.Path.Combine(dir, "journal.cfg"); journalRoot.Save(journalPath);
        JournalEntry resumed = JournalEntry.Read(ConfigNode.Load(journalPath).GetNode("BOOSTER"));
        Check(resumed.Id == staged.Id && resumed.StageCursor == 2 && resumed.AnchorPart == 123456,
            "Staging cursor and booster identity survive an actual ConfigNode file round trip");
        Check(resumed.Status == "Tracking" && resumed.Funds == 456 && resumed.Time == 123,
            "Existing recovery journal data is preserved");
        Check(resumed.FailedParts.SetEquals(new uint[] { 400, 401 }),
            "Crash debris exclusion survives saving and loading, even with changed vessel identities");
        Check(StagePolicy.Next(new[] { 4, 2, 1, 0 }, resumed.StageCursor, 1) == 1,
            "Reload resumes below the fired stage without activating it again");
        ConfigNode finalStage = new ConfigNode(); staged.StageCursor = 0; staged.Write(finalStage);
        Check(JournalEntry.Read(finalStage).StageCursor == 0, "Executed stage zero remains exhausted after reload");
        ConfigNode legacyEntry = new ConfigNode(); legacyEntry.AddValue("id", Guid.NewGuid().ToString());
        Check(JournalEntry.Read(legacyEntry).StageCursor == -1 && JournalEntry.Read(legacyEntry).AnchorPart == 0,
            "Old saves without autostage state retain safe initialization defaults");
        Check(JournalEntry.Read(new ConfigNode()) == null, "Malformed journal identity is ignored");
        string defaults = System.IO.Path.Combine(dir, "defaults.cfg");
        File.WriteAllText(defaults, "BoosterWatch\n{\n physicsRangeKm = 150\n maxSinkSpeed = 5\n maxTotalSpeed = 6\n cameraFps = 12\n customKey = keep\n}\n");
        Settings upgraded = Settings.Load(defaults);
        Check(upgraded.PhysicsRange == 500000 && upgraded.Limits.SinkSpeed == 8 && upgraded.Limits.TotalSpeed == 9,
            "Old defaults migrate to 500 km and 8 m/s");
        Check(File.Exists(defaults + ".bak"), "Migration backs up the previous settings");
        Check(File.ReadAllText(defaults).Contains("customKey = keep") && upgraded.CameraFps == 12,
            "Unrelated settings survive migration");
        upgraded.PhysicsRange = 1250000; upgraded.Limits.SinkSpeed = 9; upgraded.Limits.TotalSpeed = 10;
        Check(upgraded.Save(), "Custom flight settings save atomically");
        Settings reloaded = Settings.Load(defaults);
        Check(reloaded.PhysicsRange == 1250000 && reloaded.Limits.SinkSpeed == 9,
            "Range above old 250 km cap survives reload");
        reloaded.CameraFps = 20;
        reloaded.WindowWidth = 1100; reloaded.WindowHeight = 850; reloaded.WindowX = 90; reloaded.WindowY = 120;
        Check(reloaded.Save(), "Window geometry and existing flight settings save together");
        Settings restored = Settings.Load(defaults);

        Check(restored.HeatImmune, "Heat protection for tracked boosters is on by default");
        restored.HeatImmune = false;
        Check(restored.Save(), "Switching heat protection off saves through the real KSP ConfigNode");
        Settings withoutHeat = Settings.Load(defaults);
        Check(!withoutHeat.HeatImmune,
            "A disabled heat protection survives reload without touching the other switches");
        withoutHeat.HeatImmune = true;
        Check(withoutHeat.Save(), "Heat protection can be switched back on");
        Check(Settings.Load(defaults).HeatImmune, "Re-enabled heat protection survives reload");
        string standalone = System.IO.Path.Combine(dir, "standalone.cfg");
        File.WriteAllText(standalone, "PhysStageRecovery\n{\n configVersion = 2\n useMechJeb = True\n touchdownSeconds = 10\n requireTouchdown = False\n cameraFps = 20\n}\n");
        Settings independent = Settings.Load(standalone);
        Check(independent.CameraFps == 20 && !File.ReadAllText(standalone).Contains("requireTouchdown"),
            "Legacy virtual recovery is removed while camera FPS is preserved");
        Check(independent.HeatImmune, "A config written before the option existed gets heat protection by default");
        independent.Save();
        Check(!File.ReadAllText(standalone).Contains("useMechJeb"), "Obsolete dependency toggle is removed on save");

        // One number for the landing target: the window writes landingSpeed, the predictive law reads
        // the target. Until 0.9.36 those were two keys, so the window could show 5 m/s while the
        // booster aimed at 2 - the value the player sets has to win.
        string target = System.IO.Path.Combine(dir, "target.cfg");
        File.WriteAllText(target, "PhysStageRecovery\n{\n configVersion = 6\n landingSpeed = 4\n touchdownSpeed = 12\n}\n");
        Settings picked = Settings.Load(target);
        Check(picked.LandingSpeed == 4, "The window's target sink wins over the retired second key");
        Check(picked.TouchdownSpeed == 4, "The landing law reads exactly the value the window shows");
        picked.TouchdownSpeed = 3;
        Check(picked.LandingSpeed == 3, "Moving the target moves the window's value with it");
        Check(picked.Save(), "The straightened target saves");
        string saved = File.ReadAllText(target);
        Check(!saved.Contains("touchdownSpeed"), "The retired second key is removed from the file");
        Check(saved.Contains("configVersion = " + Settings.CurrentVersion), "Settings are marked as straightened");
        Settings again = Settings.Load(target);
        Check(again.TouchdownSpeed == 3 && again.LandingSpeed == 3,
            "One target survives a reload, from one key");
        Check(!restored.AutoStage && !restored.PoweredLanding, "New automation is disabled for existing installations");
        restored.AutoStage = true; restored.LastAutoStage = 2; restored.AutoStageHeight = 4200;
        restored.PoweredLanding = true; restored.LandingSpeed = 1.5;
        Check(restored.Save(), "New automation settings save through the real KSP ConfigNode");
        restored = Settings.Load(defaults);
        Check(restored.AutoStage && restored.LastAutoStage == 2 && restored.AutoStageHeight == 4200,
            "Autostage enable, inclusive boundary and trigger height survive reload");
        Check(restored.PoweredLanding && restored.LandingSpeed == 1.5, "Powered landing and target speed survive reload");
        Check(restored.CameraFps == 20,
            "Camera FPS survive settings edits");
        restored.SetBehavior(false, false);
        restored.CameraEnabled = false; restored.AutoOpenWindow = false; restored.ShowDiagnostics = true;
        restored.CameraDistance = 112; restored.CameraHeading = -75; restored.CameraPitch = 43;
        restored.Limits.SinkSpeed = 11; restored.HeatImmune = false;
        Check(restored.Save(), "Hidden settings and UI preferences save together");
        Settings ui = Settings.Load(defaults);
        Check(ui.HasBehaviorSettings && !ui.ModEnabled && !ui.AutoRecovery, "Operational switches persist independently of the save journal");
        Check(!ui.CameraEnabled && !ui.AutoOpenWindow && ui.ShowDiagnostics, "Hidden camera, auto-open and diagnostic preferences survive reload");
        Check(ui.CameraDistance == 112 && ui.CameraHeading == -75 && ui.CameraPitch == 43,
            "Mouse-controlled camera zoom and orientation survive reload");
        Check(ui.Limits.SinkSpeed == 11 && !ui.HeatImmune, "Hidden limits and heat protection are preserved");
        Check(!File.ReadAllText(defaults).Contains("recoveryHeight") && !File.ReadAllText(defaults).Contains("requireTouchdown"),
            "Obsolete virtual recovery settings are absent after migration");
        // The predictive landing law brought its own keys. An installation that predates them must
        // get them written out, and the target speed it had configured must come across: losing it
        // would silently change how hard every existing booster lands. The target itself is the one
        // key the window writes - a second one would drift away from it again.
        string legacyLanding = System.IO.Path.Combine(dir, "legacy-landing.cfg");
        File.WriteAllText(legacyLanding,
            "PhysStageRecovery\n{\n configVersion = 4\n poweredLanding = True\n landingSpeed = 3.5\n}\n");
        Settings carried = Settings.Load(legacyLanding);
        Check(Math.Abs(carried.TouchdownSpeed - 3.5) < 1e-9,
            "The old target speed becomes the new law's touchdown speed");
        string landingText = File.ReadAllText(legacyLanding);
        Check(!landingText.Contains("guidanceMode") && landingText.Contains("landingSpeed")
            && landingText.Contains("terminalAltitude") && landingText.Contains("tiltLimit")
            && landingText.Contains("thrustReserve") && !landingText.Contains("touchdownSpeed"),
            "The new landing settings are written into the file for the player to edit");
        Settings chosen = Settings.Load(legacyLanding);
        chosen.TouchdownSpeed = 2.5; chosen.TerminalAltitude = 120;
        Check(chosen.Save(), "The new landing settings save through the real KSP ConfigNode");
        Settings back = Settings.Load(legacyLanding);
        Check(Math.Abs(back.TouchdownSpeed - 2.5) < 1e-9
            && Math.Abs(back.TerminalAltitude - 120) < 1e-9,
            "Landing mode, touchdown speed and terminal altitude survive a reload");
        string boundsPath = System.IO.Path.Combine(dir, "camera-bounds.cfg");
        File.WriteAllText(boundsPath, "PhysStageRecovery\n{\n configVersion = 4\n cameraDistance = NaN\n cameraHeading = 999\n cameraPitch = -999\n}\n");
        Settings bounds = Settings.Load(boundsPath);
        Check(bounds.CameraDistance == 65 && bounds.CameraHeading == 180 && bounds.CameraPitch == -80,
            "Invalid manual camera settings fall back or clamp to usable bounds");
        Check(restored.WindowWidth == 1100 && restored.WindowHeight == 850 && restored.WindowX == 90 && restored.WindowY == 120,
            "Freely chosen window dimensions and position survive a reload");
        ConfigNode renamed = ConfigNode.Load(defaults);
        Check(renamed.GetNode("PhysStageRecovery") != null && renamed.GetNode("BoosterWatch") == null,
            "Legacy settings migrate to the new mod name without duplicate settings nodes");
        string custom = System.IO.Path.Combine(dir, "custom.cfg");
        File.WriteAllText(custom, "BoosterWatch\n{\n physicsRangeKm = 220\n maxSinkSpeed = 4\n maxTotalSpeed = 5\n}\n");
        Settings preserved = Settings.Load(custom);
        Check(preserved.PhysicsRange == 220000 && preserved.Limits.SinkSpeed == 4,
            "Existing non-default custom settings are preserved");
        // Exercise the actual KSP range copy constructor used during live changes.
        VesselRanges baseline = new VesselRanges();
        float oldPack = baseline.flying.pack;
        VesselRanges copy = new VesselRanges(baseline);
        copy.flying.pack = 1000000;
        Check(baseline.flying.pack == oldPack, "Vessel range copies do not mutate global/original ranges");
        VesselRangeTransition transition = new VesselRangeTransition(baseline);
        transition.Request(baseline, 500000);
        Check(transition.Current.flying.load >= 502000 && transition.Current.flying.pack == oldPack,
            "Increasing range expands loading before changing physics");
        transition.Advance(); transition.Advance();
        Check(transition.Current.flying.pack >= 501000 && !transition.Complete,
            "Physics is expanded on a later tick");
        transition.Advance();
        Check(transition.Complete, "Increase completes without changing the original range object");
        transition.Request(transition.Current, 100000);
        Check(transition.Current.flying.load >= 502000 && transition.Current.flying.pack >= 501000,
            "Decreasing range initially retains the old load/physics envelope");
        transition.Advance(); transition.Advance();
        Check(transition.Current.flying.pack == Math.Max(oldPack, 101000) && transition.Current.flying.load >= 502000,
            "Decrease shrinks physics while leaving load distances intact for another tick");
        transition.Advance();
        Check(transition.Current.flying.load == Math.Max(baseline.flying.load, 102000) && transition.Complete,
            "Decrease finally shrinks loading on a separate tick");
        transition.Request(transition.Current, 2000000);
        transition.Advance(); transition.Advance(); transition.Advance();
        Check(transition.Current.flying.unpack >= 2000000 && transition.Current.splashed.unpack >= 2000000
            && transition.Current.subOrbital.unpack >= 2000000 && baseline.flying.pack == oldPack,
            "Large ranges apply to multiple vessel situations without touching stock defaults");
    }
}
