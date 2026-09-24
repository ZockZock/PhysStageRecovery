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

        private readonly Dictionary<uint, double[]> originals = new Dictionary<uint, double[]>();

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
                    if (!originals.ContainsKey(part.flightID))
                        originals[part.flightID] = new[] { part.maxTemp, part.skinMaxTemp };
                    if (part.maxTemp < ImmuneTemperature) part.maxTemp = ImmuneTemperature;
                    if (part.skinMaxTemp < ImmuneTemperature) part.skinMaxTemp = ImmuneTemperature;
                }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard failed: " + e.Message); }
        }

        // Puts the stock limits back. A part that is hotter than its original limit right now
        // explodes on the next check, which is what switching the option off means.
        public void Restore(Vessel vessel)
        {
            try
            {
                if (originals.Count == 0) return;
                if (vessel != null && vessel.parts != null)
                    foreach (Part part in vessel.parts)
                    {
                        if (part == null) continue;
                        double[] values;
                        if (!originals.TryGetValue(part.flightID, out values)) continue;
                        part.maxTemp = values[0]; part.skinMaxTemp = values[1];
                        originals.Remove(part.flightID);
                    }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard restore failed: " + e.Message); }
            // Parts that left the vessel in the meantime keep their raised limits; they are debris
            // and no longer tracked, so the record is dropped instead of growing without bound.
            originals.Clear();
        }
    }
}
