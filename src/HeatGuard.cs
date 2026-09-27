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
    // The guard sets exactly those two limits on the parts of tracked boosters according to the
    // heat difficulty (HeatPolicy): lifted (Easy), 20 % above the original (Normal) or left alone
    // (Realistic). The original values go back when the booster is released or the player flies
    // it. Other vessels, the active rocket included, are never touched - so this is deliberately
    // not the global CheatOptions.IgnoreMaxTemperature switch.
    //
    // It also measures how close the hottest part is to the limit that destroys it, for the window,
    // the log and the entry burn.
    public sealed class HeatGuard
    {
        // Keyed by the part itself, not by the vessel it is on: after an auto-stage separation the
        // parts are spread over two vessels, and restoring only the old one left the others at
        // 100000 K for good (and the next tracker recorded that as their "original" limit).
        private readonly Dictionary<Part, double[]> originals = new Dictionary<Part, double[]>();
        private readonly HashSet<Part> raised = new HashSet<Part>();

        // How many parts currently carry raised limits. Diagnostic only.
        public int Protected { get { return raised.Count; } }
        public HeatMode Mode { get; private set; }
        // Hottest part as a share of the limit that destroys it in the current mode (Easy: of its
        // original limit). NaN until measured.
        public double Ratio { get; private set; } = double.NaN;
        // The same, but without the inside of a part whose engine is running: an engine heats itself
        // while it burns, and that must not look like reentry heat to the entry burn.
        public double ReentryRatio { get; private set; } = double.NaN;
        // The limit [K] of the part that sets ReentryRatio, as it counts in the current mode.
        public double ReentryLimit { get; private set; } = double.NaN;
        public string HottestPart { get; private set; } = "";
        // Highest Ratio seen while tracked, with where it happened.
        public double PeakRatio { get; private set; }
        public double PeakAltitude { get; private set; } = double.NaN;
        public double PeakSpeed { get; private set; } = double.NaN;
        public string PeakPart { get; private set; } = "";

        // Applies or removes the limits for one vessel. Safe to call on every measurement tick and
        // never throws: this runs inside the flight loop. active = false restores the originals
        // (the player flies this vessel now).
        public void Update(Vessel vessel, HeatMode mode, bool active)
        {
            try
            {
                if (vessel == null || vessel.parts == null) return;
                if (!active) { Restore(vessel); return; }
                if (mode != Mode) Restore(vessel);
                Mode = mode;
                foreach (Part part in vessel.parts)
                {
                    if (part == null || part.flightID == 0) continue;
                    double[] limits = Originals(part);
                    if (mode == HeatMode.Realistic) continue;
                    double core = HeatPolicy.Limit(mode, limits[0]), skin = HeatPolicy.Limit(mode, limits[1]);
                    if (part.maxTemp < core) part.maxTemp = core;
                    if (part.skinMaxTemp < skin) part.skinMaxTemp = skin;
                    raised.Add(part);
                }
                Measure(vessel);
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard failed: " + e.Message); }
        }

        // Updates Ratio and ReentryRatio. Cheap enough for every physics tick.
        public void Measure(Vessel vessel)
        {
            try
            {
                if (vessel == null || vessel.parts == null) return;
                double worst = 0, reentry = 0, reentryLimit = double.NaN; string hottest = "";
                bool any = false;
                foreach (Part part in vessel.parts)
                {
                    if (part == null || part.flightID == 0) continue;
                    double[] limits = Originals(part);
                    double core = HeatPolicy.Ratio(Mode, part.temperature, limits[0]);
                    double skin = HeatPolicy.Ratio(Mode, part.skinTemperature, limits[1]);
                    if (!Finite(core) && !Finite(skin)) continue;
                    double partWorst = Math.Max(Finite(core) ? core : 0, Finite(skin) ? skin : 0);
                    any = true;
                    if (partWorst > worst) { worst = partWorst; hottest = part.partInfo != null ? part.partInfo.title : part.name; }
                    double partReentry = Finite(skin) ? skin : 0, partLimit = limits[1];
                    if (!Burning(part) && Finite(core) && core > partReentry) { partReentry = core; partLimit = limits[0]; }
                    if (partReentry > reentry) { reentry = partReentry; reentryLimit = partLimit * Factor(Mode); }
                }
                Ratio = any ? worst : double.NaN;
                ReentryRatio = any ? reentry : double.NaN;
                ReentryLimit = reentryLimit;
                HottestPart = hottest;
                if (any && worst > PeakRatio)
                {
                    PeakRatio = worst; PeakPart = hottest;
                    PeakAltitude = vessel.altitude; PeakSpeed = vessel.srf_velocity.magnitude;
                }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat measurement failed: " + e.Message); }
        }

        private static double Factor(HeatMode mode)
        {
            double factor = HeatPolicy.LimitFactor(mode);
            return double.IsInfinity(factor) ? 1 : factor;
        }

        private static bool Burning(Part part)
        {
            foreach (ModuleEngines engine in part.FindModulesImplementing<ModuleEngines>())
                if (engine.EngineIgnited && engine.finalThrust > 0.01f) return true;
            return false;
        }

        private double[] Originals(Part part)
        {
            double[] limits;
            if (!originals.TryGetValue(part, out limits))
            {
                limits = new[] { Original(part.maxTemp, part, false), Original(part.skinMaxTemp, part, true) };
                originals[part] = limits;
            }
            return limits;
        }

        // A value above the part's stock limit is not an original: a part split off another tracked
        // booster still carries that guard's limit. The part's prefab knows the stock one.
        private static double Original(double current, Part part, bool skin)
        {
            Part prefab = part.partInfo != null ? part.partInfo.partPrefab : null;
            if (prefab == null) return current;
            double stock = skin ? prefab.skinMaxTemp : prefab.maxTemp;
            return stock > 0 && current > stock ? stock : current;
        }

        // Puts the stock limits back. A part that is hotter than its original limit right now
        // explodes on the next check, which is what a stricter mode means.
        public void Restore(Vessel vessel)
        {
            try
            {
                foreach (Part part in raised)
                {
                    // Every raised part that still exists, whichever vessel it is on now.
                    double[] limits;
                    if (part == null || !originals.TryGetValue(part, out limits)) continue;
                    part.maxTemp = limits[0]; part.skinMaxTemp = limits[1];
                }
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Heat guard restore failed: " + e.Message); }
            raised.Clear();
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
