// KSP adapter for the vendored MechJeb attitude controller; no MechJeb assembly or part is required.
//
// Bis 0.9.39 lag hier auch die portierte MechJeb-Landekette (LandingController, Schubregler,
// UntargetedDeorbit/CoastToDeceleration/DecelerationBurn/FinalDescent, guidanceMode = legacy).
// Geflogen wird seit Langem nur noch das vorhersagende Landegesetz (Guidance/); die alte Kette
// ist entfernt. Geblieben ist, was dieses Gesetz braucht: die Lageregelung (BetterController)
// und ihr Messwert-Container.
using System;
using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    internal enum AttitudeReference { INERTIAL, ORBIT_HORIZONTAL, SURFACE_NORTH, SURFACE_VELOCITY }
    internal sealed class VesselState
    {
        public Vector3d CoM, SurfaceVelocity, Up, Forward, OrbitalVelocity;
        public Vector3d TorqueAvailable;
        public double AltitudeBottom, AltitudeTrue, LocalGravity, ThrustAvailable;
        public double LimitedMaxThrustAcceleration, DeltaT;
    }
    internal sealed class MechJebCore
    {
        public readonly Vessel Vessel;
        public readonly VesselState VesselState = new VesselState();
        public readonly MechJebModuleAttitudeController Attitude;
        public MechJebCore(Vessel vessel)
        {
            Vessel = vessel; Attitude = new MechJebModuleAttitudeController(this);
        }
    }
    internal abstract class BaseAttitudeController
    {
        protected readonly MechJebModuleAttitudeController Ac;
        protected BaseAttitudeController(MechJebModuleAttitudeController c) { Ac = c; }
        public abstract void DrivePre(FlightCtrlState s, out Vector3d act, out Vector3d deltaEuler);
        public virtual void OnModuleEnabled() { }
        public abstract void Reset();
        public abstract void Reset(int i);
    }
    internal sealed class MechJebModuleAttitudeController
    {
        private readonly MechJebCore core;
        private readonly BetterController controller;
        private Vector3d direction;
        public Vessel Vessel { get { return core.Vessel; } }
        public VesselState VesselState { get { return core.VesselState; } }
        public Vector3d torque { get { return VesselState.TorqueAvailable; } }
        public readonly Vector3d AxisControl = new Vector3d(1, 0, 1);
        public readonly Vector3d ActuationControl = Vector3d.one;
        public readonly Vector3d OmegaTarget = new Vector3d(double.NaN, double.NaN, double.NaN);
        public QuaternionD RequestedAttitude;
        public QuaternionD CurrentAttitude { get { return (QuaternionD)Vessel.ReferenceTransform.rotation * MathExtensions.Euler(-90, 0, 0); } }
        public Vector3d MomentOfInertia { get { return Vessel.MOI; } }
        public Vector3d AngularVelocity { get { return Vessel.angularVelocityD; } }
        public double attitudeError { get { return attitudeAngleFromTarget(); } }
        public MechJebModuleAttitudeController(MechJebCore c)
        { core = c; controller = new BetterController(this); controller.OnModuleEnabled(); }
        public double attitudeAngleFromTarget() { return Vector3d.Angle(VesselState.Forward, direction); }
        public void attitudeTo(Vector3d target, AttitudeReference reference, object consumer)
        {
            switch (reference)
            {
                case AttitudeReference.ORBIT_HORIZONTAL: direction = -Vector3d.Exclude(VesselState.Up, VesselState.OrbitalVelocity).normalized; break;
                case AttitudeReference.SURFACE_NORTH: direction = VesselState.Up; break;
                case AttitudeReference.SURFACE_VELOCITY: direction = -VesselState.SurfaceVelocity.normalized; break;
                default: direction = target.normalized; break;
            }
            if (direction.sqrMagnitude < 0.1) direction = VesselState.Up;
            // The direction-only MechJeb API disables roll control. Preserve the current roll
            // reference while mapping KSP's nose (+Y) to MechJeb's attitude frame (+Z).
            Vector3 dir = direction, up = -Vessel.ReferenceTransform.forward;
            Vector3.OrthoNormalize(ref dir, ref up);
            RequestedAttitude = QuaternionD.LookRotation(dir, up);
        }
        public double Tilt { get { return Vector3d.Angle(direction, VesselState.Up); } }
        public void Drive(FlightCtrlState s)
        {
            Vector3d act, delta;
            controller.DrivePre(s, out act, out delta);
            s.pitch = (float)PortMath.Clamp(act.x, -1, 1);
            s.roll = 0;
            s.yaw = (float)PortMath.Clamp(act.z, -1, 1);
        }
    }
}
