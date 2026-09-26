using System.Linq;

namespace BoosterWatch
{
    // Which engines can actually fly a powered landing.
    //
    // The rules are the same ones the legacy landing path has always used, gathered here so both
    // the old MechJeb chain and the new predictive guidance judge a booster identically. A booster
    // whose only engine fails one of these cannot be landed by any autopilot, and the flight log
    // should say so before the attempt rather than after the impact.
    public static class EngineSelection
    {
        // Axial, throttleable, restartable, single-mode. Anything else cannot be steered or
        // modulated finely enough to hold a descent rate.
        public static bool Suitable(ModuleEngines engine)
        {
            return engine != null && engine.isEnabled && !engine.throttleLocked && !engine.independentThrottle
                && !engine.atmChangeFlow && engine.minThrust <= 0 && engine.throttleMin <= 0
                && !engine.useThrottleIspCurve
                && engine.part.FindModuleImplementing<MultiModeEngine>() == null
                && engine.allowRestart && engine.allowShutdown;
        }

        // Does every propellant this engine burns still have something in the connected tanks?
        public static bool HasPropellant(ModuleEngines engine)
        {
            if (engine.propellants.Count == 0) return false;
            foreach (Propellant propellant in engine.propellants)
            {
                if (propellant.ratio <= 0) continue;
                double amount, capacity;
                engine.part.GetConnectedResourceTotals(propellant.id, propellant.GetFlowMode(), out amount, out capacity, true);
                if (!RecoveryPolicy.Finite(amount) || amount <= 0.0001) return false;
            }
            return true;
        }

    }
}
