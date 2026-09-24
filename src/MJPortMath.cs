// KSP adapter and math helpers for the MechJeb source port. See THIRD_PARTY.md.
using System;
using UnityEngine;
namespace BoosterWatch.MechJebPort {
    internal static class PortMath
    {
        public const double EPS = 2.2204460492503131e-16;
        public static bool IsFinite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static double Clamp(double n, double lo, double hi) { return Math.Max(lo, Math.Min(hi, n)); }
        public static double Clamp01(double n) { return Clamp(n, 0, 1); }
        public static double Deg2Rad(double n) { return n * Math.PI / 180; }
        public static double Rad2Deg(double n) { return n * 180 / Math.PI; }
        public static double Clamp2Pi(double n) { n %= 2 * Math.PI; n = n < 0 ? n + 2 * Math.PI : n; return n >= 2 * Math.PI ? 0 : n; }
        public static double ClampPi(double n) { n = Clamp2Pi(n); return n > Math.PI ? n - 2 * Math.PI : n; }
        public static double SafeAcos(double n) { return Math.Acos(Clamp(n, -1, 1)); }
        public static double TerrainAltitude(this CelestialBody body, Vector3d position)
        {
            if (body.pqsController == null) return 0;
            double h = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(body.GetLatitude(position), body.GetLongitude(position))) - body.Radius;
            return body.ocean ? Math.Max(0, h) : h;
        }
        // MechJeb CelestialBodyExtensions.DragLength, unchanged. b has units of inverse length, so
        // 1/b is the distance over which the air takes a significant part of the speed away.
        public static double DragLength(this CelestialBody body, Vector3d pos, double dragCoeff, double mass)
        {
            double airDensity = FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(pos, body),
                FlightGlobals.getExternalTemperature(pos, body));
            if (airDensity <= 0) return double.MaxValue;
            return mass / (0.0005 * PhysicsGlobals.DragMultiplier * airDensity * dragCoeff);
        }

        public static double DragLength(this CelestialBody body, double altitudeASL, double dragCoeff, double mass)
        {
            return body.DragLength(body.GetWorldSurfacePosition(0, 0, altitudeASL), dragCoeff, mass);
        }

        // MechJeb CelestialBodyExtensions.RealMaxAtmosphereAltitude, unchanged.
        public static double RealMaxAtmosphereAltitude(this CelestialBody body)
        {
            return !body.atmosphere ? 0 : body.atmosphereDepth;
        }

        // MechJeb CelestialBodyExtensions.AltitudeForPressure, unchanged bisection.
        public static double AltitudeForPressure(this CelestialBody body, double pressure)
        {
            if (!body.atmosphere) return 0;
            double upperAlt = body.atmosphereDepth, lowerAlt = 0;
            while (upperAlt - lowerAlt > 10)
            {
                double testAlt = (upperAlt + lowerAlt) * 0.5;
                if (FlightGlobals.getStaticPressure(testAlt, body) < pressure) upperAlt = testAlt;
                else lowerAlt = testAlt;
            }
            return (upperAlt + lowerAlt) * 0.5;
        }
    }

}
