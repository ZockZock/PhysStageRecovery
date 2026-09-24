using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // The reserve menu has to sit on the engine, and KSP takes the modules of a part from its config.
    // This mod ships no ModuleManager patch, so the module is attached to the parts themselves: in the
    // editor as soon as an engine appears, in flight for every loaded vessel. Part.AddModule is the
    // same API KerbalEVA uses for its jetpack, and a module attached that way is saved with the craft
    // and with the game.
    public static class FuelReserveAttach
    {
        public static int Attach(List<Part> parts)
        {
            if (parts == null) return 0;
            int added = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null || part.Modules == null) continue;
                if (part.Modules.Contains(ModuleFuelReserve.ModuleName)) continue;
                if (!CanBeHeld(part)) continue;
                try
                {
                    PartModule module = part.AddModule(ModuleFuelReserve.ModuleName, false);
                    if (module == null) continue;
                    module.isEnabled = true;
                    added++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[PhysStageRecovery] Lande-Vorhalt konnte nicht an " + part.name
                        + " gehaengt werden: " + e);
                }
            }
            return added;
        }

        // Only an engine that can be shut down can hold a reserve; a solid booster that cannot is left
        // without the menu.
        private static bool CanBeHeld(Part part)
        {
            List<ModuleEngines> engines = part.FindModulesImplementing<ModuleEngines>();
            for (int i = 0; i < engines.Count; i++) if (engines[i].allowShutdown) return true;
            return false;
        }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public sealed class FuelReserveEditorAttach : MonoBehaviour
    {
        private float nextScan;
        private int reported;

        private void Start()
        {
            GameEvents.onEditorShipModified.Add(OnShipModified);
            Scan();
        }

        private void OnDestroy() { GameEvents.onEditorShipModified.Remove(OnShipModified); }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            Scan();
        }

        private void OnShipModified(ShipConstruct ship) { Scan(); }

        private void Scan()
        {
            List<Part> parts = EditorLogic.SortedShipList;
            int added = FuelReserveAttach.Attach(parts);
            if (added <= 0) return;
            reported += added;
            if (reported == added)
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schieberegler an " + added
                    + " Triebwerksteil(en) im Editor verfuegbar.");
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class FuelReserveFlightAttach : MonoBehaviour
    {
        private float nextScan;
        private int reported;

        private void Start() { Scan(); }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            Scan();
        }

        private void Scan()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || FlightGlobals.VesselsLoaded == null) return;
            int added = 0;
            for (int i = 0; i < FlightGlobals.VesselsLoaded.Count; i++)
            {
                Vessel vessel = FlightGlobals.VesselsLoaded[i];
                if (vessel == null || vessel.parts == null || vessel.packed) continue;
                added += FuelReserveAttach.Attach(vessel.parts);
            }
            if (added <= 0) return;
            reported += added;
            if (reported == added)
                Debug.Log("[PhysStageRecovery] Lande-Vorhalt: Schieberegler an " + added
                    + " Triebwerksteil(en) im Flug verfuegbar.");
        }
    }
}
