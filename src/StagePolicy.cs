using System;
using System.Collections.Generic;

namespace BoosterWatch
{
    public static class StagePolicy
    {
        // KSP stage numbers count down. The lower bound is inclusive.
        public static int Next(IEnumerable<int> stages, int cursor, int lastStage)
        {
            int next = -1;
            foreach (int stage in stages)
                if (stage >= lastStage && stage < cursor && stage > next) next = stage;
            return next;
        }

        public static bool CanFire(bool enabled, bool eligible, double clearance, double sink,
            double triggerHeight, double now, double lastFire)
        {
            return enabled && eligible && RecoveryPolicy.Finite(clearance) && clearance > 0
                && clearance <= triggerHeight && RecoveryPolicy.Finite(sink) && sink >= 1
                && RecoveryPolicy.Finite(now) && now - lastFire >= 1;
        }
    }
}
