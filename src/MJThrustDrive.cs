// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using System;
using UnityEngine;
namespace BoosterWatch.MechJebPort { internal partial class MechJebModuleThrustController { public void Drive(FlightCtrlState s) {
            if (Tmode != TMode.OFF && VesselState.ThrustAvailable > 0)
            {
                double spd = 0;

                switch (Tmode)
                {
                    case TMode.KEEP_ORBITAL:
                        spd = VesselState.SpeedOrbital;
                        break;
                    case TMode.KEEP_SURFACE:
                        spd = VesselState.SpeedSurface;
                        break;
                    case TMode.KEEP_VERTICAL:
                        spd = VesselState.SpeedVertical;
                        if (TransKillH)
                        {
                            var hsdir = Vector3.ProjectOnPlane(VesselState.SurfaceVelocity, VesselState.Up);
                            Vector3 dir = -hsdir + VesselState.Up * Math.Max(Math.Abs(spd), 20 * MainBody.GeeASL);
                            Vector3d rot;
                            if (Math.Min(VesselState.AltitudeASL, VesselState.AltitudeTrue) > 5000 &&
                                hsdir.magnitude > Math.Max(Math.Abs(spd), 100 * MainBody.GeeASL) * 2)
                            {
                                Tmode = TMode.DIRECT;
                                TransSpdAct = 100;
                                rot = -hsdir;
                            }
                            else
                            {
                                // Once slow and near the ground, keep the same bounded drift
                                // correction on both sides of vertical=0 to avoid attitude jumps.
                                if (TerminalSettling || (VesselState.AltitudeBottom <= 500 && VesselState.SpeedVertical >= 0))
                                {
                                    double maxTilt = Math.Tan(30 * Math.PI / 180);
                                    dir = -hsdir + VesselState.Up * Math.Max(
                                        Math.Max(Math.Abs(spd), 20 * MainBody.GeeASL), hsdir.magnitude / maxTilt);
                                }
                                rot = dir.normalized;
                            }

                            Core.Attitude.attitudeTo(rot, AttitudeReference.INERTIAL, null);
                        }

                        break;
                }

                double tErr = (TransSpdAct - spd) / VesselState.MaxThrustAcceleration;
                if ((Tmode == TMode.KEEP_ORBITAL && Vector3d.Dot(VesselState.Forward, VesselState.OrbitalVelocity) < 0) ||
                    (Tmode == TMode.KEEP_SURFACE && Vector3d.Dot(VesselState.Forward, VesselState.SurfaceVelocity) < 0))
                {
                    //allow thrust to declerate
                    tErr *= -1;
                }

                double tAct = _pid.Compute(tErr) * (TimeWarp.fixedDeltaTime / 0.02); // PSR: preserve gain during physics warp

                if (Tmode != TMode.KEEP_VERTICAL
                    || !TransKillH
                    || Core.Attitude.attitudeError < 2
                    || (Math.Min(VesselState.AltitudeASL, VesselState.AltitudeTrue) < 1000 && Core.Attitude.attitudeError < 90))
                {
                    if (Tmode == TMode.DIRECT)
                    {
                        _transPrevThrust = TargetThrottle = TransSpdAct / 100.0F;
                    }
                    else
                    {
                        _transPrevThrust = TargetThrottle = Mathf.Clamp01(_transPrevThrust + (float)tAct);
                    }
                }
                else
                {
                    bool useGimbal = VesselState.TorqueGimbal.Positive.x > VesselState.TorqueAvailable.x * 10 ||
                        VesselState.TorqueGimbal.Positive.z > VesselState.TorqueAvailable.z * 10;

                    bool useDiffThrottle = VesselState.TorqueDifferentialThrottle.x > VesselState.TorqueAvailable.x * 10 ||
                        VesselState.TorqueDifferentialThrottle.z > VesselState.TorqueAvailable.z * 10;

                    if (Core.Attitude.attitudeError >= 2 && (useGimbal || (useDiffThrottle && Core.Thrust.DifferentialThrottle)))
                    {
                        _transPrevThrust = TargetThrottle = 0.1F;
                        Print(" targetThrottle = 0.1F");
                    }
                    else
                    {
                        _transPrevThrust = TargetThrottle = 0;
                    }
                }
            }

s.mainThrottle = Mathf.Clamp01(TargetThrottle);
} } }
