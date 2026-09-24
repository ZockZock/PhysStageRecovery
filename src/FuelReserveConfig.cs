using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // KSP takes a part's modules from its part config. A module that is only attached to the part
    // objects afterwards does not survive the next rebuild of that part - and a launched vessel is
    // built exactly that way (ProtoPartSnapshot.CreatePart -> Instantiate(prefab), and Part.LoadModule
    // only fills values into modules the new part already has). That is why a reserve set in the VAB
    // was gone on the launch pad. The reserve therefore belongs in the part config, like every stock
    // module, and since this mod ships no ModuleManager patch it puts the MODULE node in itself while
    // the part database is parsed.
    public static class FuelReserveConfig
    {
        private const string PatchId = "BoosterWatch.FuelReserveConfig";
        private static Harmony harmony;
        private static MethodInfo target;

        public static void Install()
        {
            if (harmony != null) return;
            target = AccessTools.Method(typeof(PartLoader), "ParsePart");
            if (target == null) throw new MissingMethodException("KSP PartLoader.ParsePart not found");
            harmony = new Harmony(PatchId);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(FuelReserveConfig), "BeforeParsePart"));
            Debug.Log("[PhysStageRecovery] Lande-Vorhalt als Bauteilmodul eingetragen (" + target.Name + ").");
        }

        public static void Remove()
        {
            if (harmony != null && target != null) harmony.Unpatch(target, HarmonyPatchType.All, PatchId);
            harmony = null; target = null;
        }

        // Runs before KSP parses the part, so the module ends up in the part like a stock one: on the
        // prefab, in the craft file and in the save.
        public static void BeforeParsePart(ConfigNode node)
        {
            if (node == null || node.name != "PART") return;
            if (HasModule(node, ModuleFuelReserve.ModuleName)) return;
            if (!HasEngine(node)) return;
            ConfigNode module = node.AddNode("MODULE");
            module.AddValue("name", ModuleFuelReserve.ModuleName);
        }

        private static bool HasModule(ConfigNode node, string name)
        {
            foreach (ConfigNode module in node.GetNodes("MODULE"))
                if (module.GetValue("name") == name) return true;
            return false;
        }

        // Every part that has an engine gets the menu. Whether the engine can actually hold a reserve
        // is decided per engine (allowShutdown); the module hides the slider where it cannot.
        private static bool HasEngine(ConfigNode node)
        {
            foreach (ConfigNode module in node.GetNodes("MODULE"))
            {
                string name = module.GetValue("name");
                if (string.IsNullOrEmpty(name)) continue;
                if (name == "ModuleEngines" || name == "ModuleEnginesFX") return true;
                Type type = AssemblyLoader.GetClassByName(typeof(PartModule), name);
                if (type != null && typeof(ModuleEngines).IsAssignableFrom(type)) return true;
            }
            return false;
        }
    }

    // The patch has to be in place before the part database is compiled, which is what the Instantly
    // start state is for.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class FuelReserveConfigInstaller : MonoBehaviour
    {
        private void Awake()
        {
            try { FuelReserveConfig.Install(); }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Lande-Vorhalt konnte nicht in die Bauteile eingetragen werden: " + e);
            }
        }
    }

    // Waits for the finished part database and reports the result: one line per start, so a missing
    // slider is never a guess.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class FuelReserveConfigCheck : MonoBehaviour
    {
        private void Start()
        {
            int engines = 0, withReserve = 0;
            foreach (AvailablePart available in PartLoader.LoadedPartsList)
            {
                if (available == null || available.partPrefab == null) continue;
                if (available.partPrefab.FindModulesImplementing<ModuleEngines>().Count == 0) continue;
                engines++;
                if (available.partPrefab.FindModuleImplementing<ModuleFuelReserve>() != null) withReserve++;
            }
            Debug.Log("[PhysStageRecovery] Lande-Vorhalt: " + withReserve + " von " + engines
                + " Triebwerksteilen mit Regler.");
            if (engines > 0 && withReserve < engines)
                Debug.LogError("[PhysStageRecovery] Lande-Vorhalt fehlt an " + (engines - withReserve)
                    + " Triebwerksteilen; ein dort gesetzter Vorhalt ueberlebt Werkstatt und Startrampe nicht.");
        }
    }
}
