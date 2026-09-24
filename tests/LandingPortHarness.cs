// Fixture for the actual vendored FinalDescent, UntargetedDeorbit and thrust Drive sources.
// Body and vessel readings are injected; no MechJeb DLL is present or loaded.
using System;
using UnityEngine;
public static class TimeWarp { public static float fixedDeltaTime = 0.02f; }
public class FlightCtrlState { public float mainThrottle; }
namespace BoosterWatch.MechJebPort
{
    internal interface IDescentSpeedPolicy { double MaxAllowedSpeed(Vector3d p, Vector3d v); }
    internal enum AttitudeReference { INERTIAL, ORBIT_HORIZONTAL, SURFACE_NORTH, SURFACE_VELOCITY }
    internal class Body { public double Radius = 600000, GeeASL = 1; public Vector3d position; public double TerrainAltitude(Vector3d p) { return 0; } }
    internal class TestVessel { public bool LandedOrSplashed; }
    internal class TestOrbit { public double PeA = -100000; }
    internal class TorquePair { public Vector3d Positive = Vector3d.zero; }
    internal class TestState
    {
        public Vector3d CoM, SurfaceVelocity, Up = Vector3d.up, Forward = Vector3d.up, OrbitalVelocity, GravityForce;
        public Vector3d TorqueAvailable = Vector3d.one, TorqueDifferentialThrottle = Vector3d.zero;
        public TorquePair TorqueGimbal = new TorquePair();
        public double AltitudeBottom, AltitudeASL, AltitudeTrue, LocalGravity = 9.81, ThrustAvailable = 20;
        public double MaxThrustAcceleration = 20, LimitedMaxThrustAcceleration = 20;
        public double DeltaT = 0.02;
        public double SpeedSurface { get { return SurfaceVelocity.magnitude; } }
        public double SpeedOrbital { get { return OrbitalVelocity.magnitude; } }
        public double SpeedVertical { get { return Vector3d.Dot(SurfaceVelocity, Up); } }
        public double SpeedSurfaceHorizontal { get { return Vector3d.Exclude(Up, SurfaceVelocity).magnitude; } }
    }
    internal class MechJebCore
    {
        public readonly TestVessel Vessel = new TestVessel();
        public readonly TestState VesselState = new TestState();
        public readonly Body MainBody = new Body();
        public readonly TestOrbit Orbit = new TestOrbit();
        public readonly MechJebModuleAttitudeController Attitude;
        public readonly MechJebModuleThrustController Thrust;
        public readonly TestLanding Landing;
        public MechJebCore() { Attitude = new MechJebModuleAttitudeController(this); Thrust = new MechJebModuleThrustController(this); Landing = new TestLanding(this); }
    }
    internal abstract class AutopilotStep
    {
        protected MechJebCore Core;
        protected TestVessel Vessel { get { return Core.Vessel; } }
        protected TestState VesselState { get { return Core.VesselState; } }
        protected Body MainBody { get { return Core.MainBody; } }
        protected TestOrbit Orbit { get { return Core.Orbit; } }
        public string Status;
        protected AutopilotStep(MechJebCore core) { Core = core; }
        public virtual AutopilotStep Drive(FlightCtrlState s) { return this; }
        public virtual AutopilotStep OnFixedUpdate() { return this; }
    }
    internal class TestLanding
    {
        private MechJebCore core;
        public double TouchdownSpeed = 0.5;
        public bool Stopped;
        // Readings the fixture cannot take from KSP: MechJeb derives these from the drag model and
        // the reentry simulation, which are not part of the port.
        public bool AtmosphereToBrake = true;
        public bool UseBrakingEnvelope = true;
        public double DecelerationEndAltitudeValue = 200;
        public TestLanding(MechJebCore c) { core = c; }
        public double DecelerationEndAltitude() { return DecelerationEndAltitudeValue; }
        public bool UseAtmosphereToBrake() { return AtmosphereToBrake; }
        public bool NeedsBraking()
        {
            return UseBrakingEnvelope && BoosterWatch.BrakingEnvelope.NeedsBraking(
                core.VesselState.CoM - core.MainBody.position, core.VesselState.SurfaceVelocity,
                core.MainBody.Radius + Math.Max(0, core.VesselState.AltitudeTrue - core.VesselState.AltitudeBottom), core.VesselState.LocalGravity,
                core.VesselState.LimitedMaxThrustAcceleration, core.VesselState.DeltaT);
        }
        // Injected: the drag-free stopping envelope, which the real adapter computes from the
        // gravity-turn search over the port's own state.
        public double DragFreeAllowedSpeedValue = double.MaxValue;
        public double DragFreeAllowedSpeed() { return DragFreeAllowedSpeedValue; }
        private IDescentSpeedPolicy Policy()
        {
            if (!(core.VesselState.LimitedMaxThrustAcceleration > 0)) return null;
            double terrainRadius = core.MainBody.Radius + DecelerationEndAltitudeValue;
            if (AtmosphereToBrake)
                return new PoweredCoastDescentSpeedPolicy(terrainRadius, core.MainBody.GeeASL * 9.81,
                    core.VesselState.LimitedMaxThrustAcceleration);
            return new SafeDescentSpeedPolicy(terrainRadius, core.MainBody.GeeASL * 9.81,
                core.VesselState.LimitedMaxThrustAcceleration);
        }
        public double MaxAllowedSpeed()
        {
            IDescentSpeedPolicy policy = Policy();
            return policy == null ? double.MaxValue
                : policy.MaxAllowedSpeed(core.VesselState.CoM - core.MainBody.position, core.VesselState.SurfaceVelocity);
        }
        public double MaxAllowedSpeedAfterDt(double dt)
        {
            IDescentSpeedPolicy policy = Policy();
            return policy == null ? double.MaxValue
                : policy.MaxAllowedSpeed(core.VesselState.CoM + core.VesselState.OrbitalVelocity * dt - core.MainBody.position,
                    core.VesselState.SurfaceVelocity + dt * core.VesselState.GravityForce);
        }
        public void StopLanding() { Stopped = true; core.Thrust.TargetThrottle = 0; }
    }
    internal class TestAttitude
    {
        private MechJebCore core;
        public Vector3d Direction = Vector3d.up;
        public double attitudeError { get { return attitudeAngleFromTarget(); } }
        public double attitudeAngleFromTarget() { return Vector3d.Angle(core.VesselState.Forward, Direction); }
        public TestAttitude(MechJebCore c) { core = c; }
        public void attitudeTo(Vector3d target, AttitudeReference reference, object consumer)
        {
            Direction = reference == AttitudeReference.SURFACE_VELOCITY ? -core.VesselState.SurfaceVelocity.normalized
                : reference == AttitudeReference.SURFACE_NORTH ? core.VesselState.Up
                : reference == AttitudeReference.ORBIT_HORIZONTAL ? -Vector3d.Exclude(core.VesselState.Up, core.VesselState.OrbitalVelocity).normalized
                : target.normalized;
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
    internal class MechJebModuleAttitudeController : TestAttitude
    {
        public readonly TestState VesselState;
        public Vector3d torque = new Vector3d(2,2,2);
        public Vector3d AxisControl = new Vector3d(1,0,1), ActuationControl = Vector3d.one;
        public Vector3d OmegaTarget = new Vector3d(double.NaN,double.NaN,double.NaN);
        public Vector3d MomentOfInertia = new Vector3d(5,5,5), AngularVelocity;
        public QuaternionD CurrentAttitude = QuaternionD.identity, RequestedAttitude = QuaternionD.identity;
        public MechJebModuleAttitudeController(MechJebCore c) : base(c) { VesselState = c.VesselState; }
    }
    internal partial class MechJebModuleThrustController
    {
        public enum TMode { OFF, KEEP_ORBITAL, KEEP_SURFACE, KEEP_VERTICAL, DIRECT }
        private readonly MechJebCore Core;
        private TestState VesselState { get { return Core.VesselState; } }
        private Body MainBody { get { return Core.MainBody; } }
        private readonly PIDController _pid = new PIDController(0.05, 0.000001, 0.05);
        private float _transPrevThrust;
        public TMode Tmode;
        public bool TransKillH;
        // Used by the vendored FinalDescent in MechJeb to pick the terminal sink ramp. The harness
        // only records it; nothing in the test geometry depends on it.
        public bool TerminalSettling;
        public float TransSpdAct, TargetThrottle;
        public bool LimiterMinThrottle { get { return false; } }
        public double MinThrottle { get { return 0; } }
        public bool DifferentialThrottle { get { return false; } }
        public MechJebModuleThrustController(MechJebCore c) { Core = c; }
        public void RequestActiveThrottle(float throttle) { TargetThrottle = throttle; }
        private void Print(string text) { }
    }
}

