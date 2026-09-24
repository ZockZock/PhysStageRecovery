// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using System;

using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    namespace Landing
    {
        internal class FinalDescent : AutopilotStep
        {
            private IDescentSpeedPolicy _aggressivePolicy;
            private bool settling;

            public FinalDescent(MechJebCore core) : base(core)
            {
            }

            public override AutopilotStep OnFixedUpdate()
            {
                return this;
                
            }

            public override AutopilotStep Drive(FlightCtrlState s)
            {
                if (Vessel.LandedOrSplashed)
                {
                    Core.Landing.StopLanding();
                    return null;
                }

                // TODO perhaps we should pop the parachutes at this point, or at least consider it depending on the altitude.

                double minalt = Math.Min(VesselState.AltitudeBottom, Math.Min(VesselState.AltitudeASL, VesselState.AltitudeTrue));

                // Re-evaluate every physics tick. A transient low reading must never latch
                // the unconditional horizontal braking branch for the rest of the flight.
                bool terminalDescent = minalt <= 300;
                // Capture the slow final approach once, rather than switching at every
                // crossing of horizontal=5 or vertical=0. This state is altitude-bounded:
                // a bad low sample cannot latch terminal guidance high above the terrain.
                if (minalt > 500) settling = false;
                if (terminalDescent && (VesselState.SpeedSurface < 40
                    || VesselState.SpeedSurfaceHorizontal < 5 || VesselState.SpeedVertical >= 0))
                    settling = true;
                Core.Thrust.TerminalSettling = terminalDescent && settling;
                if (terminalDescent)
                {
                    // Fast entries still brake both velocity components retrograde. Once slow,
                    // keep damping lateral drift without chasing a nearly horizontal retrograde
                    // vector at the top of a bounce (flight log: 88 degrees at 103 m).
                    bool canUseVertical = settling
                        || VesselState.LimitedMaxThrustAcceleration <= VesselState.LocalGravity;
                    if (canUseVertical)
                    {
                        double deceleration = Math.Max(0, 0.8 * VesselState.LimitedMaxThrustAcceleration - VesselState.LocalGravity);
                        double entrySpeed = 0.9 * Math.Sqrt(2 * deceleration * 300);
                        double sinkTarget = Math.Max(Core.Landing.TouchdownSpeed,
                            entrySpeed * PortMath.Clamp(minalt / 300, 0, 1));
                        if (settling)
                        {
                            // Leave time to slew and remove remaining drift before touchdown.
                            // The existing TransKillH loop continues to control lateral velocity.
                            double driftAcceleration = Math.Max(0.1, VesselState.LocalGravity * Math.Tan(Math.PI / 6));
                            double settleTime = 3 + VesselState.SpeedSurfaceHorizontal / driftAcceleration;
                            sinkTarget = Math.Min(sinkTarget, Math.Max(Core.Landing.TouchdownSpeed,
                                Math.Min(20, Math.Max(0, minalt) / (2 * settleTime))));
                        }
                        Core.Thrust.Tmode = MechJebModuleThrustController.TMode.KEEP_VERTICAL;
                        Core.Thrust.TransKillH = true;
                        Core.Thrust.TransSpdAct = VesselState.LimitedMaxThrustAcceleration <= VesselState.LocalGravity
                            ? 0 : (float)-sinkTarget;
                    }
                    else
                    {
                        Core.Thrust.Tmode = MechJebModuleThrustController.TMode.OFF;
                        Core.Thrust.TransKillH = false;
                        Core.Attitude.attitudeTo(Vector3d.back, AttitudeReference.SURFACE_VELOCITY, null);
                        Core.Thrust.RequestActiveThrottle(1.0f);
                    }
                }
                else if (VesselState.LimitedMaxThrustAcceleration < VesselState.GravityForce.magnitude)
                {
                    Core.Thrust.Tmode = MechJebModuleThrustController.TMode.KEEP_VERTICAL;
                    Core.Thrust.TransKillH = true;
                    Core.Thrust.TransSpdAct = 0;
                }
                else if (minalt > 300)
                {
                    if (VesselState.SurfaceVelocity.magnitude > 5 && Vector3d.Angle(VesselState.SurfaceVelocity, VesselState.Up) < 80)
                    {
                        // if we have positive vertical velocity, point up and follow min thrust limiter:
                        Core.Attitude.attitudeTo(Vector3d.up, AttitudeReference.SURFACE_NORTH, null);
                        Core.Thrust.Tmode = MechJebModuleThrustController.TMode.DIRECT;
                        Core.Thrust.TransSpdAct = Core.Thrust.LimiterMinThrottle ? 100 * (float)Core.Thrust.MinThrottle : 0;
                    }
                    else if (VesselState.SurfaceVelocity.magnitude > 5 && Vector3d.Angle(VesselState.Forward, -VesselState.SurfaceVelocity) > 45)
                    {
                        // if we're not facing approximately retrograde, turn to point retrograde and follow min thrust limiter:
                        Core.Attitude.attitudeTo(Vector3d.back, AttitudeReference.SURFACE_VELOCITY, null);
                        Core.Thrust.Tmode = MechJebModuleThrustController.TMode.DIRECT;
                        Core.Thrust.TransSpdAct = Core.Thrust.LimiterMinThrottle ? 100 * (float)Core.Thrust.MinThrottle : 0;
                    }
                    else
                    {
                        //if we're above 300m, point retrograde and control surface velocity:
                        Core.Attitude.attitudeTo(Vector3d.back, AttitudeReference.SURFACE_VELOCITY, null);

                        Core.Thrust.Tmode = MechJebModuleThrustController.TMode.KEEP_SURFACE;

                        //core.thrust.trans_spd_act = (float)Math.Sqrt((vesselState.maxThrustAccel - vesselState.gravityForce.magnitude) * 2 * minalt) * 0.90F;
                        Vector3d estimatedLandingPosition = VesselState.CoM + VesselState.SurfaceVelocity.sqrMagnitude /
                            (2 * VesselState.LimitedMaxThrustAcceleration) * VesselState.SurfaceVelocity.normalized;
                        double terrainRadius = MainBody.Radius + Math.Max(
                            MainBody.TerrainAltitude(estimatedLandingPosition),
                            VesselState.AltitudeASL - minalt);
                        _aggressivePolicy =
                            new ApproachDescentSpeedPolicy(terrainRadius, VesselState.LocalGravity,
                                VesselState.LimitedMaxThrustAcceleration);
                        Core.Thrust.TransSpdAct =
                            (float)_aggressivePolicy.MaxAllowedSpeed(VesselState.CoM - MainBody.position, VesselState.SurfaceVelocity);
                    }
                }
                Status = (Core.Thrust.TerminalSettling ? "Ausregeln: " : "Endanflug: ")
                    + VesselState.AltitudeBottom.ToString("F0") + " m";

                // ComputeCourseCorrection doesn't work close to the ground
                

                return this;
            }
        }
    }
}
