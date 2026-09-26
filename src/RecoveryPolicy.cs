using System;

namespace BoosterWatch
{
    // Kept independent of Unity so the destructive recovery decision can be tested.
    public sealed class RecoveryLimits
    {
        public double SinkSpeed = 8;
        public double HorizontalSpeed = 3;
        public double TotalSpeed = 9;
        public double StableSeconds = 3;
        public double Acceleration = 0.75;
        public double AngularSpeed = 0.5;
    }

    public struct DescentSample
    {
        public double Time, Clearance, Sink, Horizontal, Angular;
        // Slope under the projected touchdown point [deg], from the flight-path scan. 0 when the
        // booster is coming down on the spot or nothing could be sampled.
        public double SlopeDegrees;
        // Clearance straight down, before the look-ahead along the flight path lowered it. The
        // cutoff decision needs this one: "still in the air" is about the ground below.
        public double GroundClearance;
        // Lowest point of the hull below the vessel origin, positive downwards [m].
        public double HullDepth;
        public bool PhysicsActive, ChutesOpen, Eligible, TerrainKnown, HasThrust, PoweredControlled;
    }

    public sealed class RecoveryDecision
    {
        // Descent evidence only. Recovery is exclusively authorized by ground contact.
        public bool StableDescent;
        public double StableSeconds;
        public double PredictedImpactSpeed;
        public string Reason;
    }

    public sealed class RecoveryPolicy
    {
        private DescentSample previous;
        private bool hasPrevious;
        private double stableSeconds;

        public void Reset() { hasPrevious = false; stableSeconds = 0; }

        public RecoveryDecision Evaluate(DescentSample sample, RecoveryLimits limits)
        {
            RecoveryDecision result = new RecoveryDecision { Reason = "Sinkflug wird gemessen" };
            double dt = hasPrevious ? sample.Time - previous.Time : 0;
            bool continuous = hasPrevious && dt > 0 && dt <= 0.5;
            double acceleration = continuous ? (sample.Sink - previous.Sink) / dt : 0;
            bool valid = Finite(sample.Time) && Finite(sample.Clearance) && Finite(sample.Sink)
                && Finite(sample.Horizontal) && Finite(sample.Angular);
            bool slow = sample.Sink >= 0 && sample.Sink <= limits.SinkSpeed
                && sample.Horizontal >= 0 && sample.Horizontal <= limits.HorizontalSpeed
                && sample.Angular >= 0 && sample.Angular <= limits.AngularSpeed
                && sample.Sink * sample.Sink + sample.Horizontal * sample.Horizontal <= limits.TotalSpeed * limits.TotalSpeed;
            bool braking = sample.PoweredControlled && sample.HasThrust || sample.ChutesOpen && !sample.HasThrust;
            bool stable = valid && sample.Eligible && sample.PhysicsActive && sample.TerrainKnown
                && braking && sample.Clearance >= 0 && slow;
            if (hasPrevious && sample.PoweredControlled != previous.PoweredControlled) continuous = false;

            if (!stable || !continuous || Math.Abs(acceleration) > limits.Acceleration)
                stableSeconds = 0;
            else
                stableSeconds += dt;

            result.StableSeconds = stableSeconds;
            result.PredictedImpactSpeed = Math.Sqrt(sample.Sink * sample.Sink
                + 2 * Math.Max(0, acceleration) * Math.Max(0, sample.Clearance)
                + sample.Horizontal * sample.Horizontal);
            if (!valid) result.Reason = "Messwerte ungueltig";
            else if (!sample.Eligible) result.Reason = "Keine automatische Bergung erlaubt";
            else if (!sample.PhysicsActive) result.Reason = "Warte auf aktive Physik";
            else if (!sample.TerrainKnown) result.Reason = "Bodenhoehe nicht verfuegbar";
            else if (!sample.ChutesOpen && !sample.PoweredControlled) result.Reason = "Keine bestaetigte Fallschirm- oder Triebwerksbremsung";
            else if (!braking) result.Reason = "Triebwerksschub ohne bestaetigte Landeregelung";
            else if (sample.Clearance < 0) result.Reason = "Bodenkontakt wird geprueft";
            else if (sample.Sink > limits.SinkSpeed) result.Reason = "Sinken " + sample.Sink.ToString("0.0") + " > Grenze " + limits.SinkSpeed.ToString("0.0") + " m/s";
            else if (sample.Horizontal > limits.HorizontalSpeed) result.Reason = "Seitwaerts zu schnell (Grenze " + limits.HorizontalSpeed.ToString("0.0") + " m/s)";
            else if (sample.Angular > limits.AngularSpeed) result.Reason = "Booster dreht sich zu schnell";
            else if (!slow) result.Reason = "Steigflug oder Gesamtgeschwindigkeit zu hoch";
            else if (stableSeconds < limits.StableSeconds) result.Reason = "Pruefe stabilen Sinkflug";
            else if (result.PredictedImpactSpeed > limits.TotalSpeed) result.Reason = "Erwarteter Aufprall zu schnell";
            else { result.StableDescent = true; result.Reason = "Stabiler Sinkflug - warte auf Bodenkontakt"; }

            // A bad sample must break the sequence, including a packed/unpacked transition.
            hasPrevious = stable;
            previous = sample;
            return result;
        }

        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
