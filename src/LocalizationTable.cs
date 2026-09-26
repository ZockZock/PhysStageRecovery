using System.Collections.Generic;

namespace BoosterWatch
{
    // Every text the player can see, with its English wording. This table is compiled into the DLL
    // and is the last line of defence: if the Localization folder is missing, the window still reads
    // English instead of raw tags. The files in packaging/Localization carry the same English text
    // for KSP's own lookup and the German wording on top of it. A test compares table and files
    // against each other and against every tag used in src, so the three cannot drift apart.
    //
    // Only text the player can see belongs here: window, settings, status lines, the reserve menu and
    // the on-screen messages. Log lines and the recorder's columns stay as they are, so a flight log
    // and a CSV can be read the same way in every language.
    public static class LocalizationTable
    {
        public const string Prefix = "#PSR_";

        public static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            // Window frame and tabs.
            { "#PSR_Window_Close", "Close window" },
            { "#PSR_Window_Tagline", "LAND STAGES. CONTINUE THE MISSION." },
            { "#PSR_Window_Mod", "Mod" },
            { "#PSR_Window_AutoRecovery", "Auto recovery" },
            { "#PSR_Window_On", "ON" },
            { "#PSR_Window_Off", "OFF" },
            { "#PSR_Window_Fault", "FAULT" },
            { "#PSR_Window_Tracked", "<<1>> TRACKING" },
            { "#PSR_Window_TabOverview", "Flight overview" },
            { "#PSR_Window_TabSettings", "Settings" },
            { "#PSR_Window_Footer", "<<1>> km range  ·  recovery on ground contact" },
            { "#PSR_Window_Ready", "Ready for the next stage" },
            { "#PSR_Window_ModOff", "Mod is switched off" },
            { "#PSR_Window_TrackingDone", "TRACKING ENDED" },
            { "#PSR_Window_CameraOff", "Camera disabled in settings.cfg" },
            { "#PSR_Window_WaitingVideo", "Waiting for live picture" },
            { "#PSR_Window_ConnectingCamera", "Connecting camera …" },
            { "#PSR_Window_CameraHint", "Right mouse button: turn   ·   Mouse wheel: zoom" },
            { "#PSR_Window_State", "State: <<1>>" },
            { "#PSR_Window_Throttle", "Throttle <<1>> %" },
            { "#PSR_Window_ThrottleHint", "Applied throttle after the thrust gate" },
            { "#PSR_Metric_Clearance", "GROUND CLEARANCE" },
            { "#PSR_Metric_SurfaceSpeed", "SURFACE SPEED" },
            { "#PSR_Metric_Distance", "DISTANCE" },
            { "#PSR_Metric_RemainingDv", "REMAINING Δv" },
            { "#PSR_Metric_RemainingDvHint", "Remaining Δv in vacuum for the landing engines" },
            { "#PSR_Metric_Fuel", "Landing propellant: <<1>> % of tank capacity" },
            { "#PSR_Metric_FuelUnknown", "No tank level available" },

            // The state line. These also name the descent phase the landing law is in.
            { "#PSR_Status_TrackingStopped", "Tracking stopped" },
            { "#PSR_Status_LandingFailed", "Landing failed" },
            { "#PSR_Status_TrackingFinished", "Tracking finished" },
            { "#PSR_Status_ModOff", "Mod switched off" },
            { "#PSR_Status_WaitingSimulation", "Waiting for simulation" },
            { "#PSR_Status_Climbing", "Climbing" },
            { "#PSR_Status_FinalApproach", "Final approach" },
            { "#PSR_Status_ChuteLanding", "Parachute landing" },
            { "#PSR_Status_PoweredLanding", "Powered landing" },
            { "#PSR_Status_DescentTracked", "Descent tracked" },
            { "#PSR_Status_NoControl", "Booster not controllable - no powered landing" },
            { "#PSR_Phase_Ready", "Ready" },
            { "#PSR_Phase_Align", "Aligning" },
            { "#PSR_Phase_Entry", "Entry" },
            { "#PSR_Phase_Burn", "Braking burn" },
            { "#PSR_Phase_Terminal", "Terminal" },
            { "#PSR_Phase_Touchdown", "Touchdown" },
            { "#PSR_Phase_Done", "Done" },
            { "#PSR_Phase_Abort", "Crash" },

            // Settings tab.
            { "#PSR_Settings_Tracking", "Tracking" },
            { "#PSR_Settings_TrackingHint", "Range for separated stages" },
            { "#PSR_Settings_Range", "Physics range" },
            { "#PSR_Settings_RangeHint", "5–2000 km" },
            { "#PSR_Settings_AutoStage", "Autostaging" },
            { "#PSR_Settings_AutoStageHint", "Trigger stages automatically during descent" },
            { "#PSR_Settings_StageHeight", "Trigger altitude" },
            { "#PSR_Settings_StageHeightHint", "50–70000 m above ground" },
            { "#PSR_Settings_LastStage", "Last stage" },
            { "#PSR_Settings_LastStageHint", "0–100, inclusive" },
            { "#PSR_Settings_Powered", "Powered landing" },
            { "#PSR_Settings_PoweredHint", "Automatic approach and ground contact" },
            { "#PSR_Settings_PoweredToggle", "Landing autopilot" },
            { "#PSR_Settings_LandingSpeed", "Target sink" },
            { "#PSR_Settings_LandingSpeedHint", "0.5–5 m/s" },
            { "#PSR_Settings_ChuteHeight", "Canopy height" },
            { "#PSR_Settings_ChuteHeightHint", "100–20000 m above ground, at the latest" },
            { "#PSR_Settings_Footer", "Changes apply to all tracked stages." },
            { "#PSR_Settings_Save", "Save" },
            { "#PSR_Settings_Diagnostics", "Log diagnostics" },
            { "#PSR_Settings_RangeError", "Please respect the given number ranges." },
            { "#PSR_Settings_SaveFailed", "Saving failed. Details are in KSP.log." },
            { "#PSR_Settings_Saved", "Saved. Applied to running stages." },

            // What the window says about a vessel it did not take over.
            { "#PSR_Notice_WaitingForRocket", "Waiting for the active rocket." },
            { "#PSR_Notice_GuardUnavailable", "Parachute guard unavailable. Details: KSP.log [PhysStageRecovery]." },
            { "#PSR_Notice_ModHalted", "Mod halted after an error. Details: KSP.log [PhysStageRecovery]." },
            { "#PSR_Notice_TrackingFrom", "Tracking separations from: <<1>>" },
            { "#PSR_Notice_BoosterLimit", "Booster limit reached: <<1>>. Further stages stay unchanged." },
            { "#PSR_Notice_Deactivated", "Deactivated: the standard physics range applies again." },
            { "#PSR_Notice_NotCaptured", "Not captured: <<1>> - <<2>>." },
            { "#PSR_Skip_Packed", "not unpacked (packed)" },
            { "#PSR_Skip_Landed", "counts as landed or splashed" },
            { "#PSR_Skip_Prelaunch", "still before launch" },
            { "#PSR_Skip_Crewed", "crewed" },
            { "#PSR_Skip_NotHomeWorld", "not on the home world" },
            { "#PSR_Skip_RootPart", "contains the rocket's root part" },
            { "#PSR_Skip_NotThisRocket", "does not belong to this rocket" },
            { "#PSR_Skip_AlreadyCrashed", "already recorded as a crash" },
            { "#PSR_Skip_Journal", "recovery journal: <<1>>" },
            { "#PSR_Skip_NoLandingMeans", "neither parachutes nor a suitable engine" },
            { "#PSR_Skip_NoControlModule", "no control module on the booster" },
            { "#PSR_Skip_ChuteOpen", "parachute is already open" },
            { "#PSR_Skip_NoChutesAndOff", "no parachutes, and powered landing is switched off" },

            // The status words the recovery journal stores. They are written to the save file in
            // English, so an old save keeps its meaning, and only the display is translated.
            { "#PSR_Journal_Tracking", "tracking" },
            { "#PSR_Journal_Lost", "lost" },
            { "#PSR_Journal_Excluded", "excluded" },
            { "#PSR_Journal_Separated", "separated" },
            { "#PSR_Journal_Touchdown", "touchdown" },
            { "#PSR_Journal_Crashed", "crashed" },
            { "#PSR_Journal_ContactUnconfirmed", "contact unconfirmed" },
            { "#PSR_Journal_Recovered", "recovered" },
            { "#PSR_Journal_Recovering", "being recovered" },
            { "#PSR_Journal_RecoveryError", "recovery error" },

            // On-screen messages and the camera.
            { "#PSR_Screen_Recovered", "PhysStageRecovery: <<1>> recovered" },
            { "#PSR_Screen_Warp", "PhysStageRecovery: boosters in flight - time warp runs as physics warp (up to 4x)." },
            { "#PSR_Screen_WarpEntry", "PhysStageRecovery: time warp slowed - a booster reaches the atmosphere in <<1>> s." },
            { "#PSR_Screen_WarpLanding", "PhysStageRecovery: powered landing in progress - no time warp until touchdown." },
            { "#PSR_Camera_Unavailable", "Camera not available; physics continues." },

            // The landing reserve in the engine's menu.
            { "#PSR_Reserve_Module", "Landing reserve" },
            { "#PSR_Reserve_Percent", "Landing reserve (%)" },
            { "#PSR_Reserve_Info", "Reserve" },
            { "#PSR_Reserve_Status", "Reserve status" },
            { "#PSR_Reserve_Release", "Release reserve" },
            { "#PSR_Reserve_Reached", "Landing reserve reached" },
            { "#PSR_Reserve_Disturbed", "Reserve disturbed (see KSP.log)" },
            { "#PSR_Reserve_NoTank", "no tank on the engine" },
            { "#PSR_Reserve_Tank", "tank" },
            { "#PSR_Reserve_Tanks", "tanks" },
            { "#PSR_Reserve_NoShutdown", "engine cannot be shut down" },
            { "#PSR_Reserve_FromTanks", "<<1>> from <<2>> <<3>>" },
            { "#PSR_Reserve_Approx", ", approx. <<1>>" },
            { "#PSR_Reserve_Released", "released (<<1>>), <<2>> left" },
            { "#PSR_Reserve_NoShutdownNoReserve", "engine cannot be shut down, no reserve" },
            { "#PSR_Reserve_NoTankNoReserve", "no tank on the engine, no reserve" },
            { "#PSR_Reserve_Held", "RESERVE REACHED: <<1>> engine(s) off, <<2>> left (limit <<3>>)" },
            { "#PSR_Reserve_HeldRelight", ", shut down <<1>>x again" },
            { "#PSR_Reserve_Active", "active: <<1>> left (limit <<2>>)" },
            { "#PSR_Reserve_OnlyHome", "only active around Kerbin" },
            { "#PSR_Reserve_ReasonManual", "by hand" },
            { "#PSR_Reserve_ReasonDescending", "already descending" },
            { "#PSR_Reserve_ReasonSeparation", "stage separation" },
            { "#PSR_Reserve_ReasonGround", "touched down" },

            // The guide in the empty window: what the mod does, in eight short blocks.
            { "#PSR_Help_IntroTitle", "What the mod does" },
            { "#PSR_Help_Intro", "Separated, unmanned stages land by themselves: parachutes, landing engine, landing legs, touchdown, recovery. Your rocket keeps flying just as before." },
            { "#PSR_Help_TrackedTitle", "Which stages are taken over" },
            { "#PSR_Help_Tracked", "Every separated, unmanned stage in range around Kerbin that still has closed parachutes or a landing engine with propellant - at most <<1>> at a time. Otherwise the window names the reason." },
            { "#PSR_Help_ChutesTitle", "Parachutes" },
            { "#PSR_Help_Chutes", "Canopies are armed a few seconds before and opened at the height set under Settings (<<1>> above ground) at the latest - a booster falling faster than ~250 m/s opens higher, early enough to brake in time (up to 5 km). With heat protection off, a canopy KSP rates unsafe stays closed." },
            { "#PSR_Help_PoweredTitle", "Powered landing" },
            { "#PSR_Help_Powered", "The autopilot turns the booster around, brakes it with the engine and settles it down over the last metres. It needs a control module on the booster (a probe core, for example), restartable engines and attitude control; airbrakes stay closed." },
            { "#PSR_Help_GearTitle", "Landing legs and brakes" },
            { "#PSR_Help_Gear", "Below 1000 m above ground the landing legs extend during the descent, below 10 m the wheel brakes come on." },
            { "#PSR_Help_AutoStageTitle", "Autostaging" },
            { "#PSR_Help_AutoStage", "Autostaging separates during the descent from the configured altitude down to the configured last stage - at most one stage per second. Your rocket is never staged by it." },
            { "#PSR_Help_RecoveryTitle", "Recovery" },
            { "#PSR_Help_Recovery", "Canopy landings are recovered on ground contact, with no waiting time; a hard impact does not count as a landing. A powered landing is judged when the engine cuts out just above the ground. With auto recovery off the stage stays where it is and can be recovered by hand later." },
            { "#PSR_Help_WarpTitle", "While you fly" },
            { "#PSR_Help_Warp", "While all boosters coast above the atmosphere, normal time warp works and is slowed in time before the first one enters. In the air it runs as physics warp (up to 4x), and it pauses while a powered landing burns. The heat guard (on by default) keeps tracked boosters from breaking up during reentry - your rocket is never touched." }
        };

        // The journal stores these words; this is the tag that displays them.
        public static string JournalTag(string status)
        {
            switch (status)
            {
                case "Tracking": return "#PSR_Journal_Tracking";
                case "Lost": return "#PSR_Journal_Lost";
                case "Excluded": return "#PSR_Journal_Excluded";
                case "Separated": return "#PSR_Journal_Separated";
                case "Touchdown": return "#PSR_Journal_Touchdown";
                case "Crashed": return "#PSR_Journal_Crashed";
                case "ContactUnconfirmed": return "#PSR_Journal_ContactUnconfirmed";
                case "Recovered": return "#PSR_Journal_Recovered";
                case "Recovering": return "#PSR_Journal_Recovering";
                case "RecoveryError": return "#PSR_Journal_RecoveryError";
                default: return null;
            }
        }

        // Replaces the <<1>> placeholders itself, for the case where KSP's language system is not
        // there to do it: a missing Localization folder, or a test run outside the game.
        public static string Substitute(string text, object[] args)
        {
            if (text == null || args == null) return text;
            for (int i = 0; i < args.Length; i++)
                text = text.Replace("<<" + (i + 1) + ">>", args[i] == null ? "" : args[i].ToString());
            return text;
        }
    }
}
