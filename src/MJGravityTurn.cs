// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using System;
namespace BoosterWatch.MechJebPort {
    internal class GravityTurnDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly double _terrainRadius;
        private readonly double _g;
        private readonly double _thrust;

        public GravityTurnDescentSpeedPolicy(double terrainRadius, double g, double thrust)
        {
            _terrainRadius = terrainRadius;
            _g = g;
            _thrust = thrust;
        }

        public double MaxAllowedSpeed(Vector3d pos, Vector3d vel)
        {
            //do a binary search for the max speed that avoids death
            double maxFallDistance = pos.magnitude - _terrainRadius;

            double lowerBound = 0;
            double upperBound = 1.1 * vel.magnitude;

            while (upperBound - lowerBound > 0.1)
            {
                double test = (upperBound + lowerBound) / 2;
                if (GravityTurnFallDistance(pos, test * vel.normalized) < maxFallDistance) lowerBound = test;
                else upperBound = test;
            }

            return 0.95 * ((upperBound + lowerBound) / 2);
        }

        private double GravityTurnFallDistance(Vector3d x, Vector3d v)
        {
            double startRadius = x.magnitude;

            const int STEPS = 10;
            for (int i = 0; i < STEPS; i++)
            {
                Vector3d gVec = -_g * x.normalized;
                Vector3d thrustVec = -_thrust * v.normalized;
                double dt = 1.0 / (STEPS - i) * (v.magnitude / _thrust);
                Vector3d newV = v + dt * (thrustVec + gVec);
                x += dt * (v + newV) / 2;
                v = newV;
            }

            double endRadius = x.magnitude;

            endRadius -= v.sqrMagnitude / (2 * (_thrust - _g));

            return startRadius - endRadius;
        }
    }

    // MechJeb2 SafeDescentSpeedPolicy, unchanged. Used when there is no atmosphere to brake in.
    internal class SafeDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly double _terrainRadius;
        private readonly double _g;
        private readonly double _thrust;

        public SafeDescentSpeedPolicy(double terrainRadius, double g, double thrust)
        {
            _terrainRadius = terrainRadius;
            _g = g;
            _thrust = thrust;
        }

        public double MaxAllowedSpeed(Vector3d pos, Vector3d vel)
        {
            double altitude = pos.magnitude - _terrainRadius;
            return 0.9 * Math.Sqrt(2 * (_thrust - _g) * altitude);
        }
    }

    // MechJeb2 PoweredCoastDescentSpeedPolicy, unchanged. Keeps the float fields - and therefore
    // the rounding of the upstream arithmetic - on purpose.
    internal class PoweredCoastDescentSpeedPolicy : IDescentSpeedPolicy
    {
        private readonly float _terrainRadius;
        private readonly float _g;
        private readonly float _thrust;

        public PoweredCoastDescentSpeedPolicy(double terrainRadius, double g, double thrust)
        {
            _terrainRadius = (float)terrainRadius;
            _g = (float)g;
            _thrust = (float)thrust;
        }

        public double MaxAllowedSpeed(Vector3d pos, Vector3d vel)
        {
            if (_terrainRadius < pos.magnitude)
                return double.MaxValue;

            double vSpeed = Vector3d.Dot(vel, pos.normalized);
            double toF = (vSpeed + Math.Sqrt(vSpeed * vSpeed + 2 * _g * (pos.magnitude - _terrainRadius))) / _g;

            return 0.8 * (_thrust - _g) * toF;
        }
    }
}
