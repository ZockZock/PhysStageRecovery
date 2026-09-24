using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // TIMEWARP input locks block both modes. Intercept on-rails transitions instead,
    // leaving the stock LOW/physics-warp controls and stock safety checks available.
    public static class RailWarpGuard
    {
        private const string Id = "PhysStageRecovery.RailWarp";
        private static Harmony harmony;
        private static Func<bool> tracking;
        private static float nextNotice;
        // Handed in already worded: the guard itself knows nothing about languages, which keeps it
        // testable without KSP's text system.
        private static string notice = "";
        public static void Install(Func<bool> keepPhysics, string message)
        {
            Remove(); tracking = keepPhysics; notice = message; harmony = new Harmony(Id);
            Patch("setRate", "BeforeRate"); Patch("setMode", "BeforeMode");
            Patch("assumeWarpRate", "BeforeAssumeRate");
        }
        private static void Patch(string target, string prefix)
        {
            MethodInfo method = AccessTools.Method(typeof(TimeWarp), target);
            if (method == null) throw new MissingMethodException("TimeWarp." + target);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(RailWarpGuard), prefix));
        }
        public static void Remove()
        {
            tracking = null;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }
        private static bool Active { get { return tracking != null && tracking(); } }
        private static void Notify()
        {
            if (Time.unscaledTime < nextNotice) return;
            nextNotice = Time.unscaledTime + 3;
            ScreenMessages.PostScreenMessage(notice, 3, ScreenMessageStyle.UPPER_CENTER);
        }
        public static bool BeforeRate(TimeWarp __instance, int __0, ref bool __result)
        {
            if (!Active || __0 <= 0 || __instance.Mode != TimeWarp.Modes.HIGH) return true;
            __result = false; Notify(); return false;
        }
        public static bool BeforeMode(TimeWarp.Modes __0, ref bool __result)
        {
            if (!Active || __0 != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate <= 1) return true;
            __result = false; Notify(); return false;
        }
        public static bool BeforeAssumeRate(TimeWarp __instance, float __0)
        {
            if (!Active || __0 <= 1 || __instance.Mode != TimeWarp.Modes.HIGH) return true;
            Notify(); return false;
        }
    }
}
