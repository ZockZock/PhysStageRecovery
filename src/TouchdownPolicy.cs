using System;

namespace BoosterWatch
{
    public enum TouchdownOutcome { Unconfirmed, Safe, Crashed }

    public static class TouchdownPolicy
    {
        // A collider bottom a little below the procedural surface is normal at contact: landing
        // gear compresses and the terrain raycast differs from the PQS height. Only a clearly
        // buried booster counts as buried.
        public const double BuriedClearance = -2;

        // Use the last AIRBORNE measurement: contact can zero KSP's velocity in one tick.
        //
        // Only the vertical speed and the rotation decide. Sideways drift was rejecting landings that
        // the game accepts without harm - a booster under four canopies came down at 8 m/s while
        // drifting 175 m/s sideways, which is the planet's own surface speed and which canopies barely
        // remove, and every such splashdown was logged as a crash. Damage, burial and the braking
        // proof still count.
        public static TouchdownOutcome Evaluate(DescentSample before, RecoveryDecision decision,
            double now, RecoveryLimits limits, bool damage)
        {
            if (damage) return TouchdownOutcome.Crashed;
            double age = now - before.Time;
            if (!RecoveryPolicy.Finite(age) || age < 0 || age > 0.5 || !before.PhysicsActive
                || !before.TerrainKnown || !RecoveryPolicy.Finite(before.Clearance)
                || !RecoveryPolicy.Finite(before.Sink) || !RecoveryPolicy.Finite(before.Horizontal)
                || !RecoveryPolicy.Finite(before.Angular)) return TouchdownOutcome.Unconfirmed;
            if (before.Sink > limits.SinkSpeed || before.Angular > limits.AngularSpeed
                || before.Clearance < BuriedClearance)
                return TouchdownOutcome.Crashed;
            bool braking = before.PoweredControlled && before.HasThrust || before.ChutesOpen && !before.HasThrust;
            return before.Sink >= 0 && braking && decision != null
                && decision.StableSeconds >= limits.StableSeconds
                && RecoveryPolicy.Finite(decision.PredictedImpactSpeed) && decision.PredictedImpactSpeed <= limits.TotalSpeed
                ? TouchdownOutcome.Safe : TouchdownOutcome.Unconfirmed;
        }

        // A real touchdown counts as recovered immediately. Every "not too fast" limit is already
        // part of the contact classification above: sink, sideways speed, rotation, total speed,
        // being buried, damaged or having lost parts all turn the contact into a crash. There is
        // deliberately no resting time.
        public static bool RecoveredOnContact(bool landed, TouchdownOutcome outcome)
        {
            return landed && outcome != TouchdownOutcome.Crashed;
        }

        // KSP builds terrain colliders around the active vessel only. A tracked booster that is
        // hundreds of kilometres away is still simulated, but it has no ground to land on: it
        // falls straight through the visible terrain, and a landing autopilot that keeps flying it
        // down at its touchdown speed buries it metres deep. Where no world collider exists the
        // exact procedural height is the only ground reference left, so reaching it counts as
        // ground contact. This is a fallback, not a replacement: as soon as KSP did build a
        // collider below the vessel, the physical contact decides.
        public static bool HeightContact(bool physicsActive, bool terrainKnown, bool worldCollider, double clearance)
        {
            return physicsActive && terrainKnown && !worldCollider
                && RecoveryPolicy.Finite(clearance) && clearance <= 0;
        }
    }
}
