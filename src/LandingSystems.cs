using System.Linq;
using ModuleWheels;
using UnityEngine;

namespace BoosterWatch
{
    // Deploys stock landing gear and applies wheel brakes through KSP's normal action
    // groups, so every gear and brake implementation keeps working unchanged.
    public static class LandingSystems
    {
        public static bool HasGear(Vessel v)
        {
            return v != null && v.loaded && v.parts.Any(p =>
                p.FindModulesImplementing<ModuleWheelDeployment>().Count > 0
                || p.FindModulesImplementing<ModuleAnimatorLandingGear>().Count > 0
                || p.FindModulesImplementing<ModuleWheelBase>().Count > 0);
        }

        public static bool HasBrakes(Vessel v)
        {
            return v != null && v.loaded && v.parts.Any(p => p.FindModulesImplementing<ModuleWheelBrakes>().Count > 0);
        }

        public static bool GearDown(Vessel v) { return v != null && v.ActionGroups[KSPActionGroup.Gear]; }
        public static bool BrakesOn(Vessel v) { return v != null && v.ActionGroups[KSPActionGroup.Brakes]; }

        // Lower everything that can be lowered. Safe to call every tick.
        public static bool DeployGear(Vessel v, out string state)
        {
            state = "";
            if (v == null || !v.loaded || v.packed) { state = "nicht geladen"; return false; }
            if (!HasGear(v)) { state = "kein Landebein vorhanden"; return false; }
            v.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
            // MechJeb also deploys individual modules: the group can already be on while
            // a leg introduced by staging is still retracted.
            foreach (ModuleWheelDeployment gear in v.parts.SelectMany(p => p.FindModulesImplementing<ModuleWheelDeployment>()))
                if (gear.fsm != null && (gear.fsm.CurrentState == gear.st_retracted || gear.fsm.CurrentState == gear.st_retracting))
                    gear.EventToggle();
            state = "Landebeine ausgefahren";
            return true;
        }

        public static bool SetBrakes(Vessel v, bool on)
        {
            if (v == null || !v.loaded || v.packed) return false;
            if (!HasBrakes(v)) return false;
            v.ActionGroups.SetGroup(KSPActionGroup.Brakes, on);
            return true;
        }

        public static bool HasRcs(Vessel v)
        {
            return v != null && v.loaded && v.parts.Any(p => p.FindModulesImplementing<ModuleRCS>().Count > 0);
        }

        // KSP deletes a vessel that goes on rails inside an atmosphere while it is not landed, and
        // far from the active vessel there is no ground collider that would ever make KSP agree that
        // the booster landed. A booster that was landed but deliberately not recovered was therefore
        // unloaded and destroyed within the same second. Register the contact with KSP and pack the
        // vessel right away, before anything can recompute the landing state from the missing
        // collider. The booster then rests in the flight state as a landed vessel and stays
        // recoverable by hand.
        public static bool HandOverLanded(Vessel v, bool splashed)
        {
            if (v == null || v.LandedOrSplashed) return false;
            v.Splashed = splashed;
            v.Landed = !splashed;
            v.situation = splashed ? Vessel.Situations.SPLASHED : Vessel.Situations.LANDED;
            v.GoOnRails();
            Debug.Log("[PhysStageRecovery] Landed booster handed to the game " + v.id
                + " splashed=" + splashed + " landedNow=" + v.LandedOrSplashed + " packed=" + v.packed
                + " situation=" + v.situation);
            return true;
        }

        // RCS adds attitude authority exactly when the engine is throttled down for the final
        // hover, which is where a long booster is hardest to hold upright.
        public static bool SetRcs(Vessel v, bool on)
        {
            if (v == null || !v.loaded || v.packed) return false;
            if (!HasRcs(v)) return false;
            v.ActionGroups.SetGroup(KSPActionGroup.RCS, on);
            return true;
        }
    }
}
