using System;
using System.Collections.Generic;

namespace BoosterWatch
{
    // Separate physics/load phases preserve KSP's load-before-unpack requirement,
    // including when the user decreases a range while boosters are already tracked.
    public sealed class VesselRangeTransition
    {
        private readonly VesselRanges original;
        private float requested;
        private int phase;
        public VesselRanges Current { get; private set; }
        public bool Complete { get { return phase >= 3; } }
        public VesselRangeTransition(VesselRanges baseline) { original = baseline; }

        public void Request(VesselRanges current, float range)
        {
            requested = range;
            Current = new VesselRanges(current);
            foreach (VesselRanges.Situation s in Situations(Current))
            {
                s.load = Math.Max(s.load, range + 2000);
                s.unload = Math.Max(s.unload, range + 4000);
            }
            phase = 0;
        }

        public void Advance()
        {
            if (Complete || phase++ == 0) return;
            var baselines = new List<VesselRanges.Situation>(Situations(original));
            int i = 0;
            foreach (VesselRanges.Situation s in Situations(Current))
            {
                VesselRanges.Situation baseline = baselines[i++];
                if (phase == 2)
                {
                    s.unpack = Math.Max(baseline.unpack, requested);
                    s.pack = Math.Max(baseline.pack, requested + 1000);
                }
                else
                {
                    s.load = Math.Max(baseline.load, requested + 2000);
                    s.unload = Math.Max(baseline.unload, requested + 4000);
                }
            }
        }

        private static IEnumerable<VesselRanges.Situation> Situations(VesselRanges r)
        {
            yield return r.flying; yield return r.subOrbital; yield return r.orbit;
            yield return r.escaping; yield return r.landed; yield return r.splashed; yield return r.prelaunch;
        }
    }
}
