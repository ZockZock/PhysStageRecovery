using System;
using System.Collections.Generic;

namespace BoosterWatch
{
    // Window visibility is deliberately not restored between flights: only a new eligible
    // separation may interrupt the player. Restoring tracking from a save stays quiet.
    public sealed class WindowOpenPolicy
    {
        private readonly HashSet<Guid> observed = new HashSet<Guid>();
        public bool Observe(Guid vessel, bool newSeparation, bool landable, bool autoOpen, bool enabled)
        {
            if (!observed.Add(vessel)) return false;
            return newSeparation && landable && autoOpen && enabled;
        }
    }
}
