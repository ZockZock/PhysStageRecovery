using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // Zeitraffer, solange Booster mit Physik fliegen.
    //
    // Der normale Zeitraffer (on rails) packt jedes Schiff - die Booster verloeren ihre Physik. Er
    // wird deshalb umgeleitet: wer ihn anfordert (Taste ".", die Pfeile oben links), bekommt den
    // Physikwarp mit derselben Stufe, hoechstens der hoechsten Physikstufe (Stock: 4x). Weitere
    // Tastendruecke laufen dann ganz normal im Physikwarp weiter.
    //
    // Waehrend eine Triebwerkslandung brennt, gibt es gar keinen Zeitraffer: mit vierfachem Zeitschritt
    // kommt die Lageregelung in den letzten Metern nicht mehr mit. Ein laufender Warp wird dann auf 1x
    // gesetzt (BoosterWatchFlight), neue Anforderungen werden abgewiesen.
    public static class RailWarpGuard
    {
        private const string Id = "PhysStageRecovery.RailWarp";
        private static Harmony harmony;
        private static Func<bool> tracking, landing;
        private static float nextNotice;
        private static MethodInfo setRate, setMode;
        private static bool redirecting;
        // Handed in already worded: the guard itself knows nothing about languages, which keeps it
        // testable without KSP's text system.
        private static string notice = "", landingNotice = "";

        public static void Install(Func<bool> keepPhysics, string message, Func<bool> landingBurn = null,
            string landingMessage = "")
        {
            Remove(); tracking = keepPhysics; landing = landingBurn; notice = message; landingNotice = landingMessage;
            harmony = new Harmony(Id);
            setRate = AccessTools.Method(typeof(TimeWarp), "setRate");
            setMode = AccessTools.Method(typeof(TimeWarp), "setMode");
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
            tracking = null; landing = null; redirecting = false;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }
        private static bool Active { get { return tracking != null && tracking(); } }
        public static bool LandingBurn { get { return Active && landing != null && landing(); } }
        private static void Notify(string text)
        {
            if (Time.unscaledTime < nextNotice || string.IsNullOrEmpty(text)) return;
            nextNotice = Time.unscaledTime + 3;
            ScreenMessages.PostScreenMessage(text, 3, ScreenMessageStyle.UPPER_CENTER);
        }

        // Stufe im Physikwarp fuer eine angeforderte Stufe des normalen Zeitraffers.
        public static int PhysicsIndex(int requested, int physicsRates)
        {
            if (requested <= 0 || physicsRates <= 1) return 0;
            return Math.Min(requested, physicsRates - 1);
        }

        public static bool BeforeRate(TimeWarp __instance, int __0, bool __1, bool __2, bool __3, bool __4,
            ref bool __result)
        {
            if (!Active || __0 <= 0 || redirecting) return true;
            if (LandingBurn) { __result = false; Notify(landingNotice); return false; }
            if (__instance.Mode != TimeWarp.Modes.HIGH) return true;
            int index = PhysicsIndex(__0, __instance.physicsWarpRates == null ? 0 : __instance.physicsWarpRates.Length);
            __result = false;
            if (index > 0 && setMode != null && setRate != null)
            {
                redirecting = true;
                try
                {
                    setMode.Invoke(__instance, new object[] { TimeWarp.Modes.LOW });
                    if (__instance.Mode == TimeWarp.Modes.LOW)
                        __result = (bool)setRate.Invoke(__instance, new object[] { index, __1, __2, __3, __4 });
                }
                catch (Exception e) { Debug.LogError("[PhysStageRecovery] Physikwarp-Umleitung: " + e.Message); }
                finally { redirecting = false; }
            }
            Notify(notice);
            return false;
        }
        public static bool BeforeMode(TimeWarp.Modes __0, ref bool __result)
        {
            if (!Active || redirecting || __0 != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate <= 1) return true;
            __result = false; Notify(notice); return false;
        }
        public static bool BeforeAssumeRate(TimeWarp __instance, float __0)
        {
            if (!Active || redirecting || __0 <= 1) return true;
            if (LandingBurn) { Notify(landingNotice); return false; }
            if (__instance.Mode != TimeWarp.Modes.HIGH) return true;
            Notify(notice); return false;
        }
    }
}
