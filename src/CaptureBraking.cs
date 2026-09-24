using System;

namespace BoosterWatch.Guidance
{
    // One acceleration vector must remove both velocity components before capture.
    // Called by flight and prediction; atmospheric drag is credited per component.
    internal static class CaptureBraking
    {
        public static void Command(DescentState state, DescentConfig config, double cutoff,
            out double vertical, out double lateral, out bool emergency)
        {
            double sink = Math.Max(0, state.Sink), side = state.LateralSpeed;
            double thrust = Math.Max(0, state.ThrustAcceleration), gravity = Math.Max(0, state.Gravity);
            double speed = Math.Sqrt(sink * sink + side * side);
            double drag = state.DragValid ? Math.Max(0, state.DragAcceleration) : 0;
            drag = Math.Min(drag, 5 * gravity);
            double dragUp = speed > 1e-9 ? drag * sink / speed : 0;
            double dragSide = speed > 1e-9 ? drag * side / speed : 0;
            double targetSink = Math.Max(config.Terminal.TouchdownSpeed, config.Predictor.CaptureSpeed);
            double distance = Math.Max(0.5, state.Clearance - Math.Max(cutoff + 1, config.Predictor.CaptureAltitude));
            // Time for a constant-deceleration arrival at the capture speed. The sideways
            // component gets the SAME deadline, rather than waiting for the sink-rate ladder.
            double time = Math.Max(0.25, 2 * distance / Math.Max(1, sink + targetSink));
            vertical = Math.Max(0, gravity - dragUp + (sink - targetSink) / time);
            // Aim inside the capture envelope, not exactly at its acceptance boundary.
            // Otherwise steering losses leave every candidate slightly outside and the
            // planner selects a long best-effort burn instead of a short feasible one.
            double targetSide = Math.Min(config.Terminal.LateralSpeed, 0.5 * config.Predictor.CaptureLateralSpeed);
            lateral = Math.Max(0, (side - targetSide) / time - dragSide);
            // A slow residual drift is not a reason to tip horizontally at the end of a
            // successful burn. Support weight and use the existing terminal tilt limit.
            if (state.Clearance <= Math.Max(config.Predictor.CaptureAltitude, config.Terminal.Altitude)
                && sink <= targetSink + 3 && lateral > 0)
            {
                vertical = Math.Max(vertical, Math.Max(0, gravity - dragUp));
                lateral = Math.Min(lateral, vertical * Math.Tan(config.Terminal.MaxTiltDegrees * Math.PI / 180));
            }

            // A late entry must not keep a thrust reserve or spend its vertical stopping
            // authority on lateral steering. Protect ground clearance first; remaining thrust
            // can still brake the drift. This does not bypass the adapter's attitude gate.
            double groundDistance = Math.Max(0.5, state.Clearance - cutoff);
            double safeSink = config.Terminal.TouchdownSpeed;
            double groundLift = Math.Max(0, gravity - dragUp
                + Math.Max(0, sink * sink - safeSink * safeSink) / (2 * groundDistance));
            double requested = Math.Sqrt(vertical * vertical + lateral * lateral);
            double reserved = thrust * (1 - config.Predictor.ThrustReserve);
            emergency = requested > reserved || groundLift > reserved;
            double budget = emergency ? thrust : reserved;
            if (requested > budget || groundLift > budget)
            {
                double minimumUp = Math.Min(budget, groundLift);
                if (requested > 1e-9)
                {
                    vertical *= budget / requested;
                    lateral *= budget / requested;
                }
                vertical = Math.Min(budget, Math.Max(minimumUp, vertical));
                lateral = Math.Min(lateral, Math.Sqrt(Math.Max(0, budget * budget - vertical * vertical)));
            }
            if (emergency)
            {
                vertical = Math.Max(vertical, Math.Min(budget, groundLift));
                lateral = Math.Min(lateral, Math.Sqrt(Math.Max(0, budget * budget - vertical * vertical)));
            }
        }
    }
}
