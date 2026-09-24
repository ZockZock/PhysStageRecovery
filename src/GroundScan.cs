using System;

namespace BoosterWatch.Guidance
{
    // What the ground ahead of the booster looks like.
    //
    // A landing height measured only straight down answers the wrong question while the booster is
    // still moving. A booster descending towards a rising slope has less ground under it than its
    // own radar altitude says, and the difference grows with the horizontal speed - at 50 m/s of
    // drift, a 10 % slope under a two-second horizon is ten metres of ground the autopilot has not
    // seen yet. Sampling along the flight path instead of straight down is what makes the profile
    // aware of the slope before the booster is over it.
    public static class GroundScan
    {
        // Samples along the flight path. Enough to resolve a ridge without costing anything on the
        // physics tick.
        public const int Samples = 8;
        // Seconds of travel the scan looks ahead. Two seconds is about where the descent-rate
        // controller can still react to what it sees.
        public const double HorizonSeconds = 2;
        // Metres of ground the scan is willing to look past. Further out the procedural surface is
        // too coarse to say anything useful about a slope.
        public const double MaximumLookahead = 400;
        // Slower than this the booster is landing on the spot, and only the height below it counts.
        public const double MinimumDrift = 0.5;

        // How far ahead the scan looked on the last call, for the log line.
        public static double LastLookahead { get; private set; }

        // The height of the terrain at a world position, expressed as altitude above the datum.
        public delegate double HeightAt(Vector3d position);

        // The lowest ground along the flight path, returned as the clearance of the hull bottom
        // above it - the same quantity and the same sign as the straight-down measurement, so the
        // two can be compared directly and the smaller one used.
        //
        //   origin          hull origin in flight coordinates
        //   originAltitude  its altitude above the datum
        //   up              local vertical (unit)
        //   drift           horizontal direction of travel (unit, may be zero)
        //   horizontalSpeed speed along drift [m/s]
        //   hullOffset      hull bottom relative to the origin, positive downwards [m]
        public static bool AlongFlightPath(Vector3d origin, double originAltitude, Vector3d up,
            Vector3d drift, double horizontalSpeed, double hullOffset, HeightAt heightAt,
            out double clearance, out double slopeDegrees)
        {
            clearance = double.NaN;
            slopeDegrees = 0;
            LastLookahead = 0;
            if (heightAt == null || !Finite(originAltitude)) return false;

            double lookahead = Math.Min(MaximumLookahead, Math.Max(0, horizontalSpeed) * HorizonSeconds);
            if (lookahead < 1 || drift.sqrMagnitude < 0.5)
            {
                // Effectively straight down: the ground under the booster is the whole answer, and
                // the slope under it is the slope it is about to land on.
                return TryMeasure(origin, originAltitude, hullOffset, heightAt, out clearance)
                    && TrySlope(origin, originAltitude, up, drift, 0, heightAt, out slopeDegrees);
            }

            LastLookahead = lookahead;
            double step = lookahead / Samples;
            double lowest = double.PositiveInfinity;
            for (int i = 0; i <= Samples; i++)
            {
                Vector3d probe = origin + drift * (i * step);
                double sampled = heightAt(probe);
                if (!Finite(sampled)) return false;
                // The probe sits at the same height as the origin plus whatever the drift direction
                // contributes along local vertical, which for a horizontal drift is nothing.
                double probeAltitude = originAltitude + Vector3d.Dot(probe - origin, up);
                double bottom = probeAltitude - sampled - hullOffset;
                if (bottom < lowest) lowest = bottom;
            }
            if (!Finite(lowest)) return false;
            clearance = lowest;
            // The slope is measured where the booster is actually going to touch down, which is the
            // far end of the scan rather than underneath it.
            if (!TrySlope(origin, originAltitude, up, drift, lookahead, heightAt, out slopeDegrees))
                slopeDegrees = 0;
            return true;
        }

        private static bool TryMeasure(Vector3d position, double originAltitude, double hullOffset,
            HeightAt heightAt, out double clearance)
        {
            double sampled = heightAt(position);
            clearance = double.NaN;
            if (!Finite(sampled)) return false;
            clearance = originAltitude - sampled - hullOffset;
            return true;
        }

        // Slope under the contact point, as an angle in degrees. The two probes sit either side of
        // the projected touchdown point along the direction of travel, so this is the slope the
        // booster will actually meet, not the average slope of the terrain around it.
        private static bool TrySlope(Vector3d origin, double originAltitude, Vector3d up, Vector3d drift,
            double distance, HeightAt heightAt, out double degrees)
        {
            degrees = 0;
            if (drift.sqrMagnitude < 0.5) return false;
            double span = Math.Max(15, Math.Min(60, distance > 0 ? distance * 0.25 : 15));
            Vector3d centre = origin + drift * Math.Max(0, distance);
            double ahead = heightAt(centre + drift * span);
            double behind = heightAt(centre - drift * span);
            if (!Finite(ahead) || !Finite(behind)) return false;
            double angle = Math.Atan2(ahead - behind, 2 * span) * 180 / Math.PI;
            // A slope steeper than 45 degrees is not something the booster can settle on anyway, and
            // a reading that steep usually means the procedural mesh is unreliable there.
            degrees = Math.Abs(angle) > 45 ? 0 : Math.Abs(angle);
            return true;
        }

        // The height at which the engines have to be cut, given the slope under the touchdown
        // point. On a slope the booster lands on one edge of its base before its centre reaches the
        // surface, so cutting at a fixed height digs that edge into the ground.
        public static double CutoffHeight(double baseCutoff, double slopeDegrees, double hullRadius,
            double heelDegrees)
        {
            double slope = Math.Tan(Math.Max(0, slopeDegrees) * Math.PI / 180);
            double heel = Math.Tan(Math.Max(0, heelDegrees) * Math.PI / 180);
            return baseCutoff + Math.Max(0, hullRadius) * (slope + heel);
        }

        // Hull radius estimated from the parts, used for the slope allowance above. This is the
        // same "how far out does the booster reach" question the part colliders answer.
        public static double HullRadius(System.Collections.Generic.IEnumerable<Vector3d> partOffsets,
            Vector3d up)
        {
            if (partOffsets == null) return 0;
            double radius = 0;
            foreach (Vector3d offset in partOffsets)
            {
                Vector3d horizontal = Vector3d.Exclude(up, offset);
                double reach = horizontal.magnitude;
                if (reach > radius) radius = reach;
            }
            return radius;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
