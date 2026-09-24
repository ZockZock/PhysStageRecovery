// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
// MechJeb2/LandingAutopilot/KillHorizontalVelocity.cs. Only the PredictionReady wait is removed;
// without MechJeb's reentry simulation there is nothing to wait for. A vessel pointing exactly
// straight up has no horizontal pointing direction and is handed to FinalDescent instead of
// hovering forever. The hover law and the hand-over are otherwise unchanged.
using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    namespace Landing
    {
        internal class KillHorizontalVelocity : AutopilotStep
        {
            public KillHorizontalVelocity(MechJebCore core) : base(core)
            {
            }

            public override AutopilotStep Drive(FlightCtrlState s)
            {
                Core.Thrust.Tmode = MechJebModuleThrustController.TMode.OFF;

                Vector3d horizontalPointingDirection = Vector3d.Exclude(VesselState.Up, VesselState.Forward).normalized;
                if (horizontalPointingDirection.sqrMagnitude <= 0.01
                    || Vector3d.Dot(horizontalPointingDirection, VesselState.SurfaceVelocity) > 0)
                {
                    Core.Thrust.RequestActiveThrottle(0.0f);
                    Core.Attitude.attitudeTo(Vector3.up, AttitudeReference.SURFACE_NORTH, Core.Landing);
                    return new FinalDescent(Core);
                }

                //control thrust to control vertical speed:
                const double DESIRED_SPEED = 0; //hover until horizontal velocity is killed
                double controlledSpeed = Vector3d.Dot(VesselState.SurfaceVelocity, VesselState.Up);
                double speedError = DESIRED_SPEED - controlledSpeed;
                const double SPEED_CORRECTION_TIME_CONSTANT = 1.0;
                double desiredAccel = speedError / SPEED_CORRECTION_TIME_CONSTANT;
                double minAccel = -VesselState.LocalGravity;
                double maxAccel = -VesselState.LocalGravity + Vector3d.Dot(VesselState.Forward, VesselState.Up) * VesselState.MaxThrustAcceleration;
                if (maxAccel - minAccel > 0)
                {
                    Core.Thrust.RequestActiveThrottle(Mathf.Clamp((float)((desiredAccel - minAccel) / (maxAccel - minAccel)), 0.0f, 1.0f));
                }
                else
                {
                    Core.Thrust.RequestActiveThrottle(0.0f);
                }

                //angle up and slightly away from vertical:
                Vector3d desiredThrustVector = (VesselState.Up + 0.2 * horizontalPointingDirection).normalized;

                Core.Attitude.attitudeTo(desiredThrustVector, AttitudeReference.INERTIAL, Core.Landing);

                Status = "Seitendrift ausgleichen"; //"Killing horizontal velocity before final descent"

                return this;
            }
        }
    }
}
