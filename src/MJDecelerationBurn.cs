// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
// MechJeb2/LandingAutopilot/DecelerationBurn.cs without the warp-to-start and course-correction
// branches: both need the reentry prediction, and the port's untargeted landing has no target to
// correct towards, so the correction term is zero. The throttle law, the speed limit and the
// alignment rule are unchanged. PSR can hand over early at the stopping envelope.
using System;
using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    namespace Landing
    {
        internal class DecelerationBurn : AutopilotStep
        {
            private bool _decelerationBurnTriggered;

            public DecelerationBurn(MechJebCore core) : base(core)
            {
            }

            public override AutopilotStep Drive(FlightCtrlState s)
            {
                if (Core.Landing.NeedsBraking()) return new FinalDescent(Core).Drive(s);

                // Straight throttle commands: the speed loops of the thrust controller stay out of
                // the way during the deceleration burn, exactly as in the upstream step.
                Core.Thrust.Tmode = MechJebModuleThrustController.TMode.OFF;

                if (VesselState.AltitudeASL < Core.Landing.DecelerationEndAltitude() + 5)
                {
                    Core.Thrust.TargetThrottle = 0;
                    if (Core.Landing.UseAtmosphereToBrake())
                        return new FinalDescent(Core);
                    return new KillHorizontalVelocity(Core);
                }

                // Upstream warps to the start of the braking burn while its prediction says the
                // burn is still more than five seconds away. The port has no prediction, so the
                // burn is always considered due and the warp branch is skipped.
                if (!_decelerationBurnTriggered)
                    _decelerationBurnTriggered = true;

                Vector3d desiredThrustVector = -VesselState.SurfaceVelocity.normalized;

                // Core.Landing.ComputeCourseCorrection(false) is zero without a target, so the
                // upstream correction angle is zero as well.
                if (Vector3d.Dot(VesselState.SurfaceVelocity, VesselState.Up) > 0
                    || Vector3d.Dot(VesselState.Forward, desiredThrustVector) < 0.75)
                {
                    Core.Thrust.RequestActiveThrottle(0.0f);
                    Status = "Bremsung: dreht rueckwaerts"; //"Braking"
                }
                else
                {
                    double controlledSpeed =
                        VesselState.SpeedSurface *
                        Math.Sign(Vector3d.Dot(VesselState.SurfaceVelocity, VesselState.Up)); //positive if we are ascending, negative if descending
                    double desiredSpeed = -Core.Landing.MaxAllowedSpeed();
                    double desiredSpeedAfterDt = -Core.Landing.MaxAllowedSpeedAfterDt(VesselState.DeltaT);
                    double minAccel = -VesselState.LocalGravity * Math.Abs(Vector3d.Dot(VesselState.SurfaceVelocity.normalized, VesselState.Up));
                    double maxAccel = VesselState.MaxThrustAcceleration * Vector3d.Dot(VesselState.Forward, -VesselState.SurfaceVelocity.normalized) -
                        VesselState.LocalGravity * Math.Abs(Vector3d.Dot(VesselState.SurfaceVelocity.normalized, VesselState.Up));
                    const double SPEED_CORRECTION_TIME_CONSTANT = 0.3;
                    double speedError = desiredSpeed - controlledSpeed;
                    double desiredAccel = speedError / SPEED_CORRECTION_TIME_CONSTANT + (desiredSpeedAfterDt - desiredSpeed) / VesselState.DeltaT;
                    if (maxAccel - minAccel > 0)
                        Core.Thrust.RequestActiveThrottle(Mathf.Clamp((float)((desiredAccel - minAccel) / (maxAccel - minAccel)), 0.0f, 1.0f));
                    else Core.Thrust.RequestActiveThrottle(0);
                    Status = "Bremsung auf " + (desiredSpeed >= double.MaxValue ? "unbegrenzt" : Math.Abs(desiredSpeed).ToString("F1")) + " m/s"; //"Braking: target speed = " +  + " m/s"
                }

                Core.Attitude.attitudeTo(desiredThrustVector, AttitudeReference.INERTIAL, Core.Landing);

                return this;
            }
        }
    }
}
