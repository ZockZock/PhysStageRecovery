using System;
using System.Collections.Generic;

namespace BoosterWatch
{
    // What the engines of the rocket being flown have configured as a landing reserve, and whether it is
    // still armed. Read by the overlay that draws the reserved share onto the stock fuel gauge.
    public sealed class ReserveStatus
    {
        // True when at least one engine of the vessel has a reserve set.
        public bool Configured;
        // Share of the engine's own tank capacity that is held back (0..1); NaN when nothing is set.
        public double Reserve = double.NaN;
        // How full those own tanks are (0..1); NaN when the engines have no tank of their own.
        public double Remaining = double.NaN;
        // Still armed. Once the reserve has been released (stage separation, landing, by hand) the
        // fuel belongs to the landing and the hatch has no business on the gauge any more.
        public bool Armed;
        public string Engine = "";
        public int Engines;

        public void Update(Vessel vessel)
        {
            Configured = false; Armed = false;
            Reserve = double.NaN; Remaining = double.NaN;
            Engine = ""; Engines = 0;
            if (vessel == null || vessel.parts == null) return;
            double best = 0;
            bool released = false;
            for (int p = 0; p < vessel.parts.Count; p++)
            {
                List<ModuleFuelReserve> modules = vessel.parts[p].FindModulesImplementing<ModuleFuelReserve>();
                for (int i = 0; i < modules.Count; i++)
                {
                    ModuleFuelReserve module = modules[i];
                    if (module == null || module.reservePercent <= 0) continue;
                    Engines++;
                    released |= module.reserveReleased;
                    // One stage can have several engines with a reserve; the strongest one is what the
                    // gauge is marked against.
                    double percent = FuelReserve.ClampPercent(module.reservePercent);
                    if (percent <= best) continue;
                    best = percent;
                    Reserve = percent / 100;
                    Remaining = module.OwnTankShare;
                    Engine = module.EngineTitle;
                }
            }
            Configured = Engines > 0 && !double.IsNaN(Reserve);
            Armed = Configured && !released;
        }
    }
}
