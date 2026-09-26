using System;

namespace BoosterWatch
{
    // Independent altitude history prevents a transient/stale velocity value at separation
    // from being interpreted as passing the apex. Starts fresh after loading or a time gap.
    public sealed class DescentGate
    {
        private bool hasSample;
        private double lastTime, lastAltitude, peakAltitude, descendingSeconds;
        public bool Ready { get; private set; }

        public void Reset() { hasSample = false; Ready = false; descendingSeconds = 0; }

        public void Observe(double time, double altitude, double verticalSpeed, bool physicsActive, bool hasThrust)
        {
            if (!physicsActive || hasThrust || !RecoveryPolicy.Finite(time)
                || !RecoveryPolicy.Finite(altitude) || !RecoveryPolicy.Finite(verticalSpeed))
            { Reset(); return; }
            double dt = hasSample ? time - lastTime : 0;
            if (!hasSample || dt <= 0 || dt > 0.5)
            {
                Ready = false; descendingSeconds = 0; peakAltitude = altitude;
            }
            else
            {
                peakAltitude = Math.Max(peakAltitude, altitude);
                double measuredVerticalSpeed = (altitude - lastAltitude) / dt;
                bool descending = verticalSpeed <= -1 && measuredVerticalSpeed <= -0.5;
                descendingSeconds = descending ? descendingSeconds + dt : 0;
                Ready = descendingSeconds >= 2 && peakAltitude - altitude >= 5;
            }
            hasSample = true;
            lastTime = time;
            lastAltitude = altitude;
        }

        public bool AllowsOpening(double time, double verticalSpeed)
        {
            return Ready && RecoveryPolicy.Finite(time) && RecoveryPolicy.Finite(verticalSpeed)
                && time >= lastTime && time - lastTime <= 0.3 && verticalSpeed <= -1;
        }
    }
}
