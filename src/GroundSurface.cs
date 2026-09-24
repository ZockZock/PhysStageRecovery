using System;

namespace BoosterWatch
{
    internal static class GroundSurface
    {
        // KSP 1.12.5 layer 10 is Scaled Scenery. Its miniature planet colliders live in
        // scaled-space coordinates, not the vessel's metre-based flight coordinates.
        // Flight log regression: Kerbin on L10 at 21.88 m while radar altitude is 68 km.
        public const int ScaledSceneryLayer = 10;
        public const int WorldLayerMask = ~(1 << ScaledSceneryLayer);
        // Allow local mesh differences and buildings (including the VAB), but never let
        // a nearby proxy collider replace terrain tens of kilometres below the vessel.
        public const double MaxSurfaceOffset = 250;

        public static bool IsPlausibleHit(double proceduralDistance, double hitDistance)
        {
            return !double.IsNaN(proceduralDistance) && !double.IsInfinity(proceduralDistance)
                && !double.IsNaN(hitDistance) && !double.IsInfinity(hitDistance)
                && hitDistance >= 0 && Math.Abs(proceduralDistance - hitDistance) <= MaxSurfaceOffset;
        }

        public static bool IsWorldSurface(int layer, bool trigger, bool vesselPart)
        {
            return layer >= 0 && layer < 32 && (WorldLayerMask & (1 << layer)) != 0
                && !trigger && !vesselPart;
        }

        public static double Clearance(double proceduralClearance, double hullBottom,
            bool worldSurface, double hitDistance)
        {
            // Buildings and the real terrain mesh still take precedence when closer than PQS.
            // A rejected scaled-space hit must not affect altitude OR suppress height contact.
            return worldSurface && IsPlausibleHit(proceduralClearance - hullBottom, hitDistance)
                ? Math.Min(proceduralClearance, hitDistance + hullBottom)
                : proceduralClearance;
        }
    }
}
