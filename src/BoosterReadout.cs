using System;
using System.Collections.Generic;
using System.Linq;

namespace BoosterWatch
{
    // Read-only UI snapshot, independent of whether the landing autopilot is enabled.
    // Resource queries run twice per second, never inside OnGUI or the control law.
    public sealed class BoosterReadout
    {
        private readonly DescentAdapter resources = new DescentAdapter();
        private double nextUpdate = double.NegativeInfinity;
        public double SurfaceSpeed = double.NaN, Throttle = double.NaN;
        public double FuelFraction = double.NaN, RemainingDeltaV = double.NaN;
        // The landing reserve configured on this booster's engines, for the fuel bar in the window.
        public readonly ReserveStatus Reserve = new ReserveStatus();

        public void Update(Vessel vessel, double now)
        {
            SurfaceSpeed = vessel.srf_velocity.magnitude;
            Throttle = vessel.ctrlState.mainThrottle;
            if (now < nextUpdate && now >= nextUpdate - 0.5) return;
            nextUpdate = now + 0.5;
            try
            {
                Reserve.Update(vessel);
                // Include empty engines: a dry tank should read zero, not disappear.
                var engines = vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                    .Where(EngineSelection.Suitable).ToList();
                if (engines.Count == 0)
                { FuelFraction = RemainingDeltaV = double.NaN; return; }
                resources.Measure(vessel, engines, 1);
                RemainingDeltaV = resources.AvailableDeltaV;
                var propellants = new HashSet<int>(engines.SelectMany(e => e.propellants)
                    .Where(p => p.ratio > 0).Select(p => p.id));
                double remaining = 0, capacity = 0;
                foreach (Part part in vessel.parts)
                    foreach (PartResource resource in part.Resources)
                    {
                        if (!propellants.Contains(resource.info.id) || resource.info.density <= 0) continue;
                        remaining += Math.Max(0, resource.amount) * resource.info.density;
                        capacity += Math.Max(0, resource.maxAmount) * resource.info.density;
                    }
                FuelFraction = capacity > 0 ? Math.Max(0, Math.Min(1, remaining / capacity)) : double.NaN;
            }
            catch
            {
                // A disappearing/staging part must not interrupt the flight controller for a UI value.
                FuelFraction = RemainingDeltaV = double.NaN;
            }
        }
    }
}
