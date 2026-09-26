using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // KSP destroys a part in Part._CheckPartTemp as soon as its temperature or its skin
    // temperature passes maxTemp / skinMaxTemp; a separate reentry damage module does not exist
    // in 1.12. A booster that holds a retrograde attitude for the whole reentry presents its
    // smallest cross-section, heats up far more than a tumbling one, and can be torn apart by
    // that single check.
    //
    // The guard raises exactly those two limits on the parts of tracked boosters and puts the
    // original values back when the option is switched off or the booster is released. Other
    // vessels, the active rocket included, are never touched - so this is deliberately not the
    // global CheatOptions.IgnoreMaxTemperature switch.
    public sealed class HeatGuard
    {
        // Far above any atmospheric entry the stock game produces on the home world.
        private const double ImmuneTemperature = 100000;

        // Keyed by the part itself, not by the vessel it is on: after an auto-stage separation the
        // parts are spread over two vessels, and restoring only the old one left the others at
        // 100000 K for good (and the next tracker recorded that as their "original" limit).
        private readonly Dictionary<Part, double[]> originals = new Dictionary<Part, double[]>();

        // How many parts currently carry raised limits. Diagnostic only.
        public int Protected { get { return originals.Count; } }

        // Applies or removes the guard for one vessel. Safe to call on every measurement tick and
        // never throws: this runs inside the flight loop.
        public void Update(Vessel vessel, bool enabled)
        {
            try
            {
                if (vessel == null || vessel.parts == null) return;
                if (!enabled) { Restore(vessel); return; }
                foreach (Part part in vessel.parts)
                {
                    if (part == null || part.flightID == 0) continue;
                    if (!originals.ContainsKey(part))
                        originals[part] = new[] { Original(part.maxTemp, part, false), Original(part.skinMaxTemp, part, true) };
                    if (part.maxTemp < ImmuneTemperature) part.maxTemp = ImmuneTemperature;
                    if (part.skinMaxTemp < ImmuneTemperature) part.skinMaxTemp = ImmuneTemperature;
                }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard failed: " + e.Message); }
        }

        // A value that is already raised is not an original: a part split off another tracked booster
        // still carries that guard's limit. The part's prefab knows the stock one.
        private static double Original(double current, Part part, bool skin)
        {
            if (current < ImmuneTemperature) return current;
            Part prefab = part.partInfo != null ? part.partInfo.partPrefab : null;
            if (prefab == null) return current;
            return skin ? prefab.skinMaxTemp : prefab.maxTemp;
        }

        // Puts the stock limits back. A part that is hotter than its original limit right now
        // explodes on the next check, which is what switching the option off means.
        public void Restore(Vessel vessel)
        {
            try
            {
                foreach (KeyValuePair<Part, double[]> entry in originals)
                {
                    // Every recorded part that still exists, whichever vessel it is on now.
                    if (entry.Key == null) continue;
                    entry.Key.maxTemp = entry.Value[0]; entry.Key.skinMaxTemp = entry.Value[1];
                }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard restore failed: " + e.Message); }
            originals.Clear();
        }
    }
}
