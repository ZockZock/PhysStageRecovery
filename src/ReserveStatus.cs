using System;
using System.Collections.Generic;

namespace BoosterWatch
{
    // What the engines of one vessel have configured as a landing reserve, and where that reserve
    // stands right now. One place for the flight window, which shows it for the rocket being flown
    // and for every tracked booster.
    public sealed class ReserveStatus
    {
        // True when at least one engine of the vessel has a reserve set.
        public bool Configured;
        // Share of the engine's own tank capacity that is held back (0..1); NaN when nothing is set.
        public double Reserve = double.NaN;
        // How full those own tanks are (0..1); NaN when the engines have no tank of their own.
        public double Remaining = double.NaN;
        // The reserve is armed, is currently holding the engines back, or has been released.
        public bool Armed, Holding, Released;
        public string Engine = "", Info = "", Status = "";
        public int Engines;

        public void Update(Vessel vessel)
        {
            Configured = false; Armed = false; Holding = false; Released = false;
            Reserve = double.NaN; Remaining = double.NaN;
            Engine = ""; Info = ""; Status = ""; Engines = 0;
            if (vessel == null || vessel.parts == null) return;
            double best = 0;
            bool released = false, holding = false;
            for (int p = 0; p < vessel.parts.Count; p++)
            {
                List<ModuleFuelReserve> modules = vessel.parts[p].FindModulesImplementing<ModuleFuelReserve>();
                for (int i = 0; i < modules.Count; i++)
                {
                    ModuleFuelReserve module = modules[i];
                    if (module == null || module.reservePercent <= 0) continue;
                    Engines++;
                    // One stage can have several engines with a reserve; the strongest one is what the
                    // bar is drawn against, and any engine still armed or holding counts for the state.
                    released |= module.reserveReleased;
                    holding |= module.IsHolding;
                    double percent = FuelReserve.ClampPercent(module.reservePercent);
                    if (percent <= best) continue;
                    best = percent;
                    Reserve = percent / 100;
                    Remaining = module.OwnTankShare;
                    Engine = module.EngineTitle;
                    Info = module.reserveInfo;
                    Status = module.reserveStatus;
                }
            }
            Configured = Engines > 0 && !double.IsNaN(Reserve);
            if (!Configured) return;
            Released = released;
            Holding = holding;
            Armed = !released;
            if (Status.Length == 0)
                Status = Holding ? "Vorhalt erreicht" : Released ? "freigegeben" : "aktiv";
        }

        // "25 % = 4.89 t aus 2 Tanks, ca. 780 m/s" - what the reserve is worth, as the engine menu
        // shows it.
        public string Worth
        {
            get
            {
                if (!Configured) return "";
                if (Info.Length > 0) return Info;
                return FuelReserve.PercentText(100 * Reserve);
            }
        }

        public string StateText
        {
            get
            {
                if (!Configured) return "";
                string text = Status.Length > 0 ? Status
                    : (Holding ? "VORHALT ERREICHT" : Released ? "freigegeben" : "aktiv")
                        + ": Rest " + FuelReserve.ShareText(Remaining);
                if (Engines > 1) text += " an " + Engines + " Triebwerken";
                return text;
            }
        }
    }
}
