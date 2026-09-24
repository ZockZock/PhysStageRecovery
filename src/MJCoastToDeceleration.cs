// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
// MechJeb2/LandingAutopilot/CoastToDeceleration.cs without the target, prediction, RCS and
// warp branches, which need MechJeb's reentry simulation and its autopilot modules. The
// PSR additionally bounds the coast by a drag-independent stopping envelope.
using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    namespace Landing
    {
        internal class CoastToDeceleration : AutopilotStep
        {
            public CoastToDeceleration(MechJebCore core) : base(core)
            {
            }

            public override AutopilotStep Drive(FlightCtrlState s)
            {
                if (Core.Landing.NeedsBraking()) return new FinalDescent(Core).Drive(s);

                // Straight throttle commands: the speed loops of the thrust controller stay out of
                // the way during the coast, exactly as in the upstream step.
                Core.Thrust.Tmode = MechJebModuleThrustController.TMode.OFF;
                Core.Thrust.TargetThrottle = 0;

                // Second, independent bound: the same drag-free stopping envelope, computed here from
                // the current position instead of a one-second prediction. Either rule may start the
                // burn. The atmospheric limit above is infinite above the deceleration end altitude,
                // so it relies on the air alone - which a booster arriving from a high orbit at
                // 2000 m/s cannot afford.
                double dragFree = Core.Landing.DragFreeAllowedSpeed();
                if (PortMath.IsFinite(dragFree) && VesselState.SpeedSurface > dragFree)
                {
                    Core.Thrust.TargetThrottle = 0;
                    // The atmospheric DecelerationBurn policy may still be unbounded here.
                    // Execute the bounded retrograde approach now instead of returning to it.
                    return new FinalDescent(Core).Drive(s);
                }

                double maxAllowedSpeed = Core.Landing.MaxAllowedSpeed();
                if (VesselState.SpeedSurface > 0.9 * maxAllowedSpeed)
                    return new DecelerationBurn(Core);

                Status = "Warte auf Bremsbeginn: " + VesselState.SpeedSurface.ToString("F0") + " m/s"; //"Coasting toward deceleration burn"

                // If we're already low, skip directly to the Deceleration burn
                if (VesselState.AltitudeASL < Core.Landing.DecelerationEndAltitude() + 5)
                    return new DecelerationBurn(Core);

                // Upstream aims at where the braking burn will start once its prediction exists
                // and at retrograde while drag is already acting (DragAcceleration >= 0.01).
                // Without the reentry simulation the port always uses the drag case, which is the
                // one that holds for a booster coming down through an atmosphere.
                if (VesselState.SurfaceVelocity.sqrMagnitude > 0.01)
                    Core.Attitude.attitudeTo(Vector3.back, AttitudeReference.SURFACE_VELOCITY, Core.Landing);

                return this;
            }
        }
    }
}
