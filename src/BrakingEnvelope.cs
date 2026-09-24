using System;
using BoosterWatch.MechJebPort;

namespace BoosterWatch
{
    // The atmospheric drag estimate is not a trajectory prediction. Never let it postpone
    // the powered approach beyond the retrograde stopping envelope, even with no landing gear
    // or with parachutes that have not opened. This only selects the phase; FinalDescent still
    // commands the actual burn and the adapter still requires real thrust and attitude authority.
    internal static class BrakingEnvelope
    {
        public static bool NeedsBraking(Vector3d position, Vector3d velocity, double terrainRadius,
            double gravity, double thrustAcceleration, double dt)
        {
            if (!PortMath.IsFinite(position.sqrMagnitude) || position.sqrMagnitude <= 0
                || !PortMath.IsFinite(velocity.sqrMagnitude) || !PortMath.IsFinite(terrainRadius)
                || !PortMath.IsFinite(gravity) || gravity <= 0
                || !PortMath.IsFinite(thrustAcceleration) || thrustAcceleration <= 0
                || !PortMath.IsFinite(dt) || dt <= 0) return false;
            if (Vector3d.Dot(velocity, position.normalized) >= 0) return false;
            // A weak engine must try to brake immediately, not wait for a nonexistent finite
            // stopping distance. FinalDescent has an explicit low-TWR branch.
            if (thrustAcceleration <= 1.01 * gravity) return true;
            // Keep room for the last 300 m ramp, hull extent and control/engine response.
            // Predict one second (or two physics ticks) ahead, with no credit for future drag.
            double delay = Math.Max(1, 2 * dt);
            Vector3d acceleration = -gravity * position.normalized;
            Vector3d predictedPosition = position + velocity * delay + 0.5 * acceleration * delay * delay;
            Vector3d predictedVelocity = velocity + acceleration * delay;
            var policy = new GravityTurnDescentSpeedPolicy(terrainRadius + 200, gravity, thrustAcceleration);
            double limit = policy.MaxAllowedSpeed(predictedPosition, predictedVelocity);
            return PortMath.IsFinite(limit) && predictedVelocity.magnitude >= limit;
        }
    }
}
