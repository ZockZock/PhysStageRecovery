using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // Staging calls Deploy() independently of our automation. Guard the actual stock
    // opening decision instead of polling/disarming after the canopy has already opened.
    public static class ParachuteGuard
    {
        private const string PatchId = "BoosterWatch.ParachuteOpening";
        private static Harmony harmony;
        private static BoosterWatchFlight owner;
        private static MethodInfo target, updateTarget;

        public static void Install(BoosterWatchFlight flight)
        {
            Remove();
            target = AccessTools.Method(typeof(ModuleParachute), "PassedAdditionalDeploymentChecks", Type.EmptyTypes);
            updateTarget = AccessTools.Method(typeof(ModuleParachute), "FixedUpdate", Type.EmptyTypes);
            if (target == null || target.ReturnType != typeof(bool) || updateTarget == null)
                throw new MissingMethodException("KSP ModuleParachute opening checks not found");
            owner = flight;
            harmony = new Harmony(PatchId);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ParachuteGuard), "AfterDeploymentChecks"));
            harmony.Patch(updateTarget, prefix: new HarmonyMethod(typeof(ParachuteGuard), "BeforeFixedUpdate"));
            Debug.Log("[PhysStageRecovery] Stock parachute opening guard installed.");
        }

        public static void Remove()
        {
            owner = null;
            if (harmony != null && target != null) harmony.Unpatch(target, HarmonyPatchType.All, PatchId);
            if (harmony != null && updateTarget != null) harmony.Unpatch(updateTarget, HarmonyPatchType.All, PatchId);
            harmony = null; target = null; updateTarget = null;
        }

        public static void BeforeFixedUpdate(ModuleParachute __instance)
        {
            if (owner == null || __instance.deploymentState != ModuleParachute.deploymentStates.ACTIVE) return;
            // Run before the ACTIVE -> SEMIDEPLOYED transition, even if the chute was
            // armed by the staging system. Do not skip thermal or other stock updates.
            if (owner.BlockParachuteOpening(__instance))
            {
                Debug.Log("[PhysStageRecovery] Deferred staged chute until safe descent: part=" + __instance.part.flightID);
                __instance.Disarm();
            }
        }

        public static void AfterDeploymentChecks(ModuleParachute __instance, ref bool __result)
        {
            if (!__result || owner == null) return;
            // Never un-deploy or cut an already open canopy; all normal stock checks still run.
            if (__instance.deploymentState != ModuleParachute.deploymentStates.STOWED
                && __instance.deploymentState != ModuleParachute.deploymentStates.ACTIVE) return;
            try { if (owner.BlockParachuteOpening(__instance)) __result = false; }
            catch (Exception e)
            {
                __result = false;
                Debug.LogError("[PhysStageRecovery] Parachute guard could not verify deployment: " + e);
            }
        }
    }
}
