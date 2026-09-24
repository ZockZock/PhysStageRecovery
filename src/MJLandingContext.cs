// KSP adapter for the vendored MechJeb algorithms; no MechJeb assembly or part is required.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch.MechJebPort
{
    internal interface IDescentSpeedPolicy { double MaxAllowedSpeed(Vector3d pos, Vector3d vel); }
    internal enum AttitudeReference { INERTIAL, ORBIT_HORIZONTAL, SURFACE_NORTH, SURFACE_VELOCITY }
    internal sealed class TorquePair { public Vector3d Positive; }
    internal sealed class VesselState
    {
        public Vector3d CoM, SurfaceVelocity, Up, Forward, OrbitalVelocity, GravityForce;
        public Vector3d TorqueAvailable;
        public readonly Vector3d TorqueDifferentialThrottle = Vector3d.zero;
        public readonly TorquePair TorqueGimbal = new TorquePair();
        public double AltitudeBottom, AltitudeASL, AltitudeTrue, LocalGravity, ThrustAvailable;
        public double MaxThrustAcceleration, LimitedMaxThrustAcceleration, DeltaT;
        public double SpeedSurface { get { return SurfaceVelocity.magnitude; } }
        public double SpeedOrbital { get { return OrbitalVelocity.magnitude; } }
        public double SpeedVertical { get { return Vector3d.Dot(SurfaceVelocity, Up); } }
        public double SpeedSurfaceHorizontal { get { return Vector3d.Exclude(Up, SurfaceVelocity).magnitude; } }
    }
    internal sealed class MechJebCore
    {
        public readonly Vessel Vessel;
        public readonly VesselState VesselState = new VesselState();
        public readonly MechJebModuleAttitudeController Attitude;
        public readonly MechJebModuleThrustController Thrust;
        public readonly LandingController Landing;
        public MechJebCore(Vessel vessel)
        {
            Vessel = vessel; Attitude = new MechJebModuleAttitudeController(this);
            Thrust = new MechJebModuleThrustController(this); Landing = new LandingController(this);
        }
    }
    internal abstract class AutopilotStep
    {
        protected readonly MechJebCore Core;
        protected Vessel Vessel { get { return Core.Vessel; } }
        protected VesselState VesselState { get { return Core.VesselState; } }
        protected CelestialBody MainBody { get { return Vessel.mainBody; } }
        protected Orbit Orbit { get { return Vessel.orbit; } }
        public string Status = "";
        protected AutopilotStep(MechJebCore core) { Core = core; }
        public virtual AutopilotStep Drive(FlightCtrlState s) { return this; }
        public virtual AutopilotStep OnFixedUpdate() { return this; }
    }
    internal sealed class LandingController
    {
        private readonly MechJebCore core;
        private AutopilotStep step;
        private int tick, dragTick = -1;
        private double dragSum;
        public double TouchdownSpeed = 0.5;
        // MechJeb assigns this in StartLanding. The port refreshes it on every tick instead,
        // because the readings that define it (thrust, drag, mass) only exist in the flight loop.
        public IDescentSpeedPolicy DescentSpeedPolicy;
        // Readings of the decision above, for the landing check log line only. They change nothing
        // about the flight; the guidance uses the same values internally either way.
        public double LastEndAltitude = double.NaN, LastDragCoefficient = double.NaN;
        public bool LastUseAtmosphere;
        public bool BrakingEnvelopeTriggered { get; private set; }
        public string Status { get { return step == null ? "Aufgesetzt" : step.Status; } }
        // True while the ported FinalDescent step flies the approach. The landing gear belongs to
        // that phase: above it the booster is still coasting or braking, and a booster that has just
        // been picked up after decoupling is not in a final descent at all.
        public bool InFinalDescent { get { return step is Landing.FinalDescent; } }
        public LandingController(MechJebCore c)
        {
            core = c; DescentSpeedPolicy = PickDescentSpeedPolicy();
            step = new Landing.UntargetedDeorbit(c);
        }
        public void Drive(FlightCtrlState s)
        {
            if (step == null) return;
            tick++;
            DescentSpeedPolicy = PickDescentSpeedPolicy();
            LastUseAtmosphere = UseAtmosphereToBrake();
            LastEndAltitude = DecelerationEndAltitude();
            LastDragCoefficient = DragSum();
            step = step.Drive(s);
        }
        public void StopLanding() { core.Thrust.RequestActiveThrottle(0); step = null; }

        // MechJebModuleLandingAutopilot._landingAltitude: the terrain altitude of the landing
        // site. The port never lands at a target, so the site is wherever the booster is now.
        public double LandingAltitude()
        {
            Vessel vessel = core.Vessel;
            if (vessel == null || vessel.mainBody == null || vessel.parts == null) return 0;
            Vector3d position = core.VesselState.CoM;
            if (!PortMath.IsFinite(position.sqrMagnitude) || position.sqrMagnitude <= 0) return 0;
            return vessel.mainBody.TerrainAltitude(position);
        }

        public bool NeedsBraking()
        {
            VesselState state = core.VesselState;
            // Account for the hull bottom as well as the centre of mass. No gear/module flag
            // influences this decision. The final guidance also probes terrain downrange.
            double terrain = LandingAltitude() + Math.Max(0, state.AltitudeTrue - state.AltitudeBottom);
            bool due = BrakingEnvelope.NeedsBraking(state.CoM - core.Vessel.mainBody.position,
                state.SurfaceVelocity, core.Vessel.mainBody.Radius + terrain,
                state.LocalGravity, state.LimitedMaxThrustAcceleration, state.DeltaT);
            if (due) BrakingEnvelopeTriggered = true;
            return due;
        }

        // MechJebModuleLandingAutopilot.DecelerationEndAltitude, unchanged.
        public double DecelerationEndAltitude()
        {
            double landingAltitude = LandingAltitude();
            //if the atmosphere is thin, the deceleration burn should end
            //500 meters above the landing site to allow for a controlled final descent
            if (!UseAtmosphereToBrake()) return 200 + landingAltitude;

            // if the atmosphere is thick, deceleration (meaning freefall through the atmosphere)
            // should end a safe height above the landing site in order to allow braking from terminal velocity
            double landingSiteDragLength = core.Vessel.mainBody.DragLength(landingAltitude, DragSum(), Mass());

            return 1.1 * landingSiteDragLength + landingAltitude;
        }

        //On planets with thick enough atmospheres, we shouldn't do a deceleration burn. Rather,
        //we should let the atmosphere decelerate us and only burn during the final descent to
        //ensure a safe touchdown speed. How do we tell if the atmosphere is thick enough? We check
        //to see if there is an altitude within the atmosphere for which the characteristic distance
        //over which drag slows the ship is smaller than the altitude above the terrain. If so, we can
        //expect to get slowed to near terminal velocity before impacting the ground.
        public bool UseAtmosphereToBrake()
        {
            Vessel vessel = core.Vessel;
            if (vessel == null || vessel.mainBody == null || vessel.parts == null) return false;
            double landingSiteDragLength = vessel.mainBody.DragLength(LandingAltitude(), DragSum(), Mass());
            return vessel.mainBody.RealMaxAtmosphereAltitude() > 0 &&
                landingSiteDragLength < 0.7 * vessel.mainBody.RealMaxAtmosphereAltitude();
        }

        private double Mass()
        {
            double mass = core.Vessel == null ? 0 : core.Vessel.GetTotalMass();
            return PortMath.IsFinite(mass) && mass > 0 ? mass : 1;
        }

        // Current drag cubes, memoised for the current physics tick: the
        // decision methods above call each other several times per tick.
        private double DragSum()
        {
            if (dragTick != tick) { dragTick = tick; dragSum = VesselAverageDrag(); }
            return dragSum;
        }

        private IDescentSpeedPolicy PickDescentSpeedPolicy()
        {
            // PickDescentSpeedPolicy in MechJebModuleLandingAutopilot. Without usable thrust above
            // local gravity no policy can produce a finite limit and both of them would return NaN
            // or a negative root; the deceleration steps then treat the limit as absent and leave
            // the descent to drag, the final ramp and the TWR < 1 case in FinalDescent.
            double thrust = core.VesselState.LimitedMaxThrustAcceleration;
            if (!PortMath.IsFinite(thrust) || thrust <= 1.01 * core.VesselState.LocalGravity) return null;
            double terrainRadius = core.Vessel.mainBody.Radius + DecelerationEndAltitude();
            if (UseAtmosphereToBrake())
                return new PoweredCoastDescentSpeedPolicy(terrainRadius, core.Vessel.mainBody.GeeASL * 9.81, thrust);
            return new SafeDescentSpeedPolicy(terrainRadius, core.Vessel.mainBody.GeeASL * 9.81, thrust);
        }

        // Average of all six current cube faces; the old assignment accidentally retained only
        // the last face. Never add a guessed future parachute contribution to this estimate.
        private double VesselAverageDrag()
        {
            Vessel vessel = core.Vessel;
            if (vessel == null || vessel.parts == null) return 0;
            float dragCoef = 0;
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part p = vessel.parts[i];
                if (p.DragCubes.None || p.ShieldedFromAirstream) continue;
                float partAreaDrag = 0;
                for (int f = 0; f < 6; f++)
                    partAreaDrag += p.DragCubes.WeightedDrag[f] * p.DragCubes.AreaOccluded[f];
                dragCoef += partAreaDrag / 6;
            }
            return dragCoef * PhysicsGlobals.DragCubeMultiplier;
        }

        // MechJebModuleLandingAutopilot.MaxAllowedSpeed / MaxAllowedSpeedAfterDt, unchanged.
        public double MaxAllowedSpeed()
        {
            if (DescentSpeedPolicy == null) return double.MaxValue;
            Vector3d position = core.VesselState.CoM - core.Vessel.mainBody.position;
            if (!PortMath.IsFinite(position.sqrMagnitude) || position.sqrMagnitude <= 0) return double.MaxValue;
            double speed = DescentSpeedPolicy.MaxAllowedSpeed(position, core.VesselState.SurfaceVelocity);
            return PortMath.IsFinite(speed) ? Math.Max(0, speed) : double.MaxValue;
        }

        public double MaxAllowedSpeedAfterDt(double dt)
        {
            if (DescentSpeedPolicy == null) return double.MaxValue;
            Vector3d position = core.VesselState.CoM + core.VesselState.OrbitalVelocity * dt - core.Vessel.mainBody.position;
            if (!PortMath.IsFinite(position.sqrMagnitude) || position.sqrMagnitude <= 0) return double.MaxValue;
            double speed = DescentSpeedPolicy.MaxAllowedSpeed(position,
                core.VesselState.SurfaceVelocity + dt * core.VesselState.GravityForce);
            return PortMath.IsFinite(speed) ? Math.Max(0, speed) : double.MaxValue;
        }

        // The fastest fall a full-thrust retrograde burn can still stop before the terrain - the
        // gravity-turn search MechJeb uses for its vacuum policy. The atmospheric coast policy has no
        // limit at all above the deceleration end altitude, because it assumes the air will do the
        // braking. That assumption fails for a booster arriving from a high orbit: one came in at
        // 2000 m/s, coasted with the engines dark down to 26 km and could no longer be stopped.
        public double DragFreeAllowedSpeed()
        {
            VesselState s = core.VesselState;
            double thrust = s.LimitedMaxThrustAcceleration;
            // No thrust above local gravity means no burn can help; the limit then does not exist.
            if (!PortMath.IsFinite(thrust) || thrust <= 1.01 * s.LocalGravity) return double.MaxValue;
            double terrainRadius = core.Vessel.mainBody.Radius + LandingAltitude()
                + Math.Max(0, s.AltitudeTrue - s.AltitudeBottom);
            var policy = new GravityTurnDescentSpeedPolicy(terrainRadius, s.LocalGravity, thrust);
            double limit = policy.MaxAllowedSpeed(s.CoM - core.Vessel.mainBody.position, s.SurfaceVelocity);
            return PortMath.IsFinite(limit) ? Math.Max(0, limit) : double.MaxValue;
        }
    }
    internal partial class MechJebModuleThrustController
    {
        public enum TMode { OFF, KEEP_ORBITAL, KEEP_SURFACE, KEEP_VERTICAL, DIRECT }
        private readonly MechJebCore Core;
        private VesselState VesselState { get { return Core.VesselState; } }
        private CelestialBody MainBody { get { return Core.Vessel.mainBody; } }
        private readonly PIDController _pid = new PIDController(0.05, 0.000001, 0.05);
        private float _transPrevThrust;
        private TMode mode;
        public TMode Tmode
        {
            get { return mode; }
            set
            {
                if (mode == value) return;
                mode = value;
                // MechJeb resets this in OnUpdate after its Tmode setter marks a change.
                // PSR has only the per-physics-tick adapter, so reset at the change itself.
                // Do not carry integral/derivative history between surface and vertical speed.
                _pid.Reset();
            }
        }
        public bool TransKillH;
        public bool TerminalSettling;
        public float TransSpdAct, TargetThrottle;
        // MechJeb defaults for the optional minimum/differential throttle limiters.
        public bool LimiterMinThrottle { get { return false; } }
        public double MinThrottle { get { return 0; } }
        public bool DifferentialThrottle { get { return false; } }
        public MechJebModuleThrustController(MechJebCore c) { Core = c; }
        public void RequestActiveThrottle(float throttle) { TargetThrottle = throttle; }
        private void Print(string text) { }
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
