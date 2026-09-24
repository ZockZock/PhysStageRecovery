using System;

namespace BoosterWatch.MechJebPort
{
    // Evaluate the actual retrograde trajectory with a thrust reserve. A scalar vertical
    // stopping-distance bound applied to TOTAL speed overbrakes shallow orbital entries.
    internal sealed class ApproachDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly GravityTurnDescentSpeedPolicy gravityTurn;

        public ApproachDescentSpeedPolicy(double terrainRadius, double gravity, double thrust)
        {
            // Keep a positive net acceleration for the gravity-turn solver even at low TWR.
            double reservedThrust = Math.Max(0.8 * thrust, gravity + 0.1 * (thrust - gravity));
            gravityTurn = new GravityTurnDescentSpeedPolicy(terrainRadius, gravity, reservedThrust);
        }

        public double MaxAllowedSpeed(Vector3d position, Vector3d velocity)
        {
            return gravityTurn.MaxAllowedSpeed(position, velocity);
        }
    }
}
