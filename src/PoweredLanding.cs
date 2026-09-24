using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BoosterWatch.Guidance;
using BoosterWatch.MechJebPort;

namespace BoosterWatch
{
    // Per-vessel KSP adapter. Guidance, throttle and attitude run on EVERY physics tick.
    //
    // Two landing laws live here. `legacy` is the ported MechJeb state chain; `predictive` is the
    // law in Guidance/, which forecasts the descent and flies a descent-rate profile. The switch is
    // a setting so a flight that goes wrong can be repeated on the old path without a rebuild.
    public sealed class PoweredLanding
    {
        private readonly Vessel vessel;
        private MechJebCore core;
        private Settings settings;
        private bool connected, oldSas, oldRcs, faulted;
        private VesselAutopilot.AutopilotMode oldMode;
        private double lastSampleTime, bottomOffset;
        private readonly HashSet<ModuleEngines> controlledEngines = new HashSet<ModuleEngines>();
        private readonly Vector3d[] torquePositive = new Vector3d[4], torqueNegative = new Vector3d[4];
        // The predictive path: the law, its bridge to the game, and the config they share.
        private DescentConfig descentConfig;
        private DescentAdapter adapter;
        private DescentGuidance descent;
        public string Status = "", StopReason = "";
        public double TiltDegrees { get; private set; }
        // Diagnostics for the landing check log line: how far the nose is from the direction the
        // guidance asked for, and the throttle the guidance requested BEFORE the safety gate
        // below. The gate is the only place that can turn a requested burn into 0 %.
        public double AttitudeError { get; private set; }
        public double CommandedThrottle { get; private set; }
        public double ActualTiltDegrees { get; private set; }
        // The attitude the guidance asked for on the last tick, in world space, and how it stands to
        // the retrograde direction. The gate that opens the entry phase reads the first, and the
        // second is the number that would have shown the coast aiming prograde at a glance.
        private Vector3d aimAxis;
        private bool aimValid;
        public double AimToRetrogradeDegrees { get; private set; }
        // Set when the safety gate below cut a requested burn, and why. Empty while the throttle the
        // law asked for is the throttle the engines get.
        public string ThrottleCutReason = "";
        public Vector3d AvailableTorque { get; private set; }
        public Vector3 ControlInput { get; private set; }
        public bool Predictive { get; private set; }
        // Readings of the predictive law, for the landing check log line only.
        public DescentPhase Phase { get { return descent == null ? DescentPhase.Idle : descent.Phase; } }
        public DescentPrediction Prediction { get { return descent == null ? null : descent.LastPrediction; } }
        public double TargetSink { get; private set; }
        public double FreeAcceleration { get; private set; }
        public double RequiredDeltaV { get; private set; }
        public double AvailableDeltaV { get { return adapter == null ? double.NaN : adapter.AvailableDeltaV; } }
        public double DragCoefficient { get { return adapter == null ? double.NaN : adapter.VesselDragCoefficient; } }
        public bool Unstoppable { get; private set; }
        // Terrain readings of the guidance, for the landing check log line only.
        public double EndAltitude { get { return core == null ? double.NaN : core.Landing.LastEndAltitude; } }
        public double LegacyDragCoefficient { get { return core == null ? double.NaN : core.Landing.LastDragCoefficient; } }
        public bool UsesAtmosphere { get { return core != null && core.Landing.LastUseAtmosphere; } }
        public bool BrakingEnvelopeTriggered { get { return core != null && core.Landing.BrakingEnvelopeTriggered; } }
        public double AvailableAcceleration { get { return core == null ? double.NaN : core.VesselState.LimitedMaxThrustAcceleration; } }
        // The acceleration the engines really deliver, as opposed to AvailableAcceleration (what they
        // could deliver). A booster that spends five seconds at 78 % throttle and loses only 25 m/s is
        // not getting what the guidance assumes - a dry tank or a sputtering engine looks exactly like
        // that in the log otherwise.
        public double ActualAcceleration { get; private set; }
        // The guidance is flying its final descent right now.
        public bool InFinalDescent { get { return core != null && core.Landing.InFinalDescent; } }
        public bool RecoveryReady { get; private set; }
        public bool OwnsControl { get { return connected; } }
        public PoweredLanding(Vessel v) { vessel = v; }
        public static bool Suitable(ModuleEngines e) { return EngineSelection.Suitable(e); }
        internal static bool HasPropellant(ModuleEngines e) { return EngineSelection.HasPropellant(e); }
        private DescentSample trackedSample;
        private bool hasTrackedSample;

        public void Step(Settings config, DescentSample sample, bool eligible, bool descentConfirmed)
        {
            settings = config; RecoveryReady = false;
            trackedSample = sample; hasTrackedSample = true;
            if (!settings.PoweredLanding || !eligible)
            { Stop("Triebwerkslandung aus / nicht freigegeben"); return; }
            if (faulted) return;
            if (!sample.PhysicsActive || !sample.TerrainKnown || !RecoveryPolicy.Finite(sample.Clearance))
            { Stop("Warte auf Physik und Bodenhoehe"); return; }
            if (!connected && !descentConfirmed)
            { Status = "Landung: warte auf bestaetigten Sinkflug"; return; }
            lastSampleTime = sample.Time;
            bottomOffset = sample.Clearance - (vessel.altitude - vessel.mainBody.TerrainAltitude(vessel.CoMD));
            if (!connected) Connect();
            double gravity = vessel.mainBody.gravParameter / (vessel.CoMD - vessel.mainBody.position).sqrMagnitude;
            double actualThrust = vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                .Sum(e => Math.Max(0, (double)e.finalThrust));
            ActualAcceleration = actualThrust / Math.Max(0.001, vessel.GetTotalMass());
            RecoveryReady = Vector3d.Dot(vessel.ReferenceTransform.up, (vessel.CoMD - vessel.mainBody.position).normalized) >= 0.95
                && actualThrust / Math.Max(0.001, vessel.GetTotalMass()) >= gravity * 0.8
                && sample.Sink >= 0 && sample.Sink <= Math.Min(settings.Limits.SinkSpeed, settings.LandingSpeed + 1)
                && sample.Horizontal <= Math.Min(2, settings.Limits.HorizontalSpeed)
                && sample.Angular <= Math.Min(0.2, settings.Limits.AngularSpeed);
        }
        private void Connect()
        {
            core = new MechJebCore(vessel);
            oldSas = vessel.ActionGroups[KSPActionGroup.SAS]; oldRcs = vessel.ActionGroups[KSPActionGroup.RCS];
            oldMode = vessel.Autopilot.Mode;
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            LandingSystems.SetRcs(vessel, true);
            connected = true; StopReason = "";
            vessel.OnFlyByWire += Control;
            Debug.Log("[PhysStageRecovery] Standalone MechJeb source landing started: " + vessel.id);
        }
        private bool CanControl()
        {
            double now = Planetarium.GetUniversalTime();
            return connected && vessel != null && vessel != FlightGlobals.ActiveVessel && vessel.GetCrewCount() == 0
                && vessel.loaded && !vessel.packed && !vessel.HoldPhysics && !vessel.LandedOrSplashed
                && now >= lastSampleTime && now - lastSampleTime <= 0.5;
        }
        private void Control(FlightCtrlState s)
        {
            if (!CanControl())
            {
                if (vessel != null && vessel != FlightGlobals.ActiveVessel && vessel.GetCrewCount() == 0) s.mainThrottle = 0;
                return;
            }
            try
            {
                MechJebPort.VesselState v = core.VesselState;
                v.CoM = vessel.CoMD; v.Up = (v.CoM - vessel.mainBody.position).normalized;
                v.Forward = vessel.ReferenceTransform.up; v.SurfaceVelocity = vessel.srf_velocity;
                v.OrbitalVelocity = vessel.obt_velocity;
                v.LocalGravity = vessel.mainBody.gravParameter / (v.CoM - vessel.mainBody.position).sqrMagnitude;
                v.GravityForce = -v.Up * v.LocalGravity; v.AltitudeASL = vessel.altitude;
                v.AltitudeTrue = v.AltitudeASL - vessel.mainBody.TerrainAltitude(v.CoM);
                v.AltitudeBottom = v.AltitudeTrue + bottomOffset; v.DeltaT = TimeWarp.fixedDeltaTime;
                if (v.DeltaT <= 0 || !RecoveryPolicy.Finite(v.AltitudeBottom)) { s.mainThrottle = 0; return; }
                List<ModuleEngines> all = vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).ToList();
                List<ModuleEngines> engines = all.Where(Suitable).Where(e => HasPropellant(e)
                    && (!e.part.ShieldedFromAirstream || e.shieldedCanActivate)).ToList();
                bool unsupported = all.Any(e => e.EngineIgnited && !Suitable(e));
                v.ThrustAvailable = 0;
                foreach (ModuleEngines e in engines)
                {
                    if (e.thrustTransforms.Count == 0) continue;
                    Vector3 axis = Vector3.zero;
                    foreach (Transform t in e.thrustTransforms) axis -= t.forward;
                    if (Vector3d.Dot(axis.normalized, v.Forward) < 0.9) { unsupported = true; continue; }
                    double thrust = e.MaxThrustOutputAtm(false, true, (float)(vessel.staticPressurekPa / 101.325),
                        vessel.externalTemperature, vessel.atmDensity);
                    if (RecoveryPolicy.Finite(thrust)) v.ThrustAvailable += Math.Max(0, thrust);
                }
                if (unsupported) v.ThrustAvailable = 0;
                v.MaxThrustAcceleration = v.LimitedMaxThrustAcceleration = v.ThrustAvailable / Math.Max(0.001, vessel.GetTotalMass());
                UpdateTorque(v); core.Landing.TouchdownSpeed = settings.LandingSpeed;
                Predictive = settings.GuidanceMode == GuidanceMode.Predictive;
                if (Predictive) DrivePredictive(s, v, engines, unsupported);
                else
                {
                    core.Landing.Drive(s); core.Thrust.Drive(s); core.Attitude.Drive(s);
                }
                ActualTiltDegrees = Vector3d.Angle(v.Forward, v.Up);
                AvailableTorque = v.TorqueAvailable;
                ControlInput = new Vector3(s.pitch, s.roll, s.yaw);
                AttitudeError = core.Attitude.attitudeError;
                CommandedThrottle = s.mainThrottle;
                // PSR safety gate: never ignite into a tumble and never control unsupported engines.
                // A running burn is kept while the engine still points roughly where the guidance wants
                // it to; past 80 degrees the thrust works against the command, which is what drove a
                // booster into the ground when its attitude error sat at 100 degrees and the throttle
                // stayed at 100 %.
                bool burning = false;
                foreach (ModuleEngines e in engines) if (e.EngineIgnited) { burning = true; break; }
                ThrottleCutReason = "";
                if (unsupported || engines.Count == 0 || core.Attitude.attitudeError > (burning ? 80 : 45)
                    || !RecoveryPolicy.Finite(s.mainThrottle))
                {
                    // Name the gate in the log. A flight that shows short burns and a hard arrival is
                    // a flight whose throttle was cut here, and without the reason the numbers look
                    // like a law that simply stopped asking for thrust.
                    ThrottleCutReason = unsupported ? "Triebwerk nicht steuerbar"
                        : engines.Count == 0 ? "kein Triebwerk"
                        : !RecoveryPolicy.Finite(s.mainThrottle) ? "Drossel ungueltig"
                        : "Lagefehler " + core.Attitude.attitudeError.ToString("0") + " Grad > "
                            + (burning ? 80 : 45);
                    s.mainThrottle = 0;
                }
                // Arm the engines as soon as the law has ordered a burn - and not only when the
                // throttle survives the safety gate below. KSP keeps an engine that was never
                // ignited dark whatever throttle is asked of it, so a booster whose attitude was off
                // at the moment the burn was due never lit at all: the window showed a commanded
                // throttle, the engines showed no plume, and there was no thrust to land on. An
                // engine ignited at zero throttle makes no thrust and burns nothing, so lighting it
                // early costs nothing and removes the whole failure mode.
                bool burnOrdered = Predictive
                    && (Phase == DescentPhase.Burn || Phase == DescentPhase.Terminal
                        || Phase == DescentPhase.Touchdown);
                foreach (ModuleEngines e in engines)
                {
                    controlledEngines.Add(e);
                    if ((s.mainThrottle > 0 || burnOrdered) && !e.EngineIgnited) e.Activate();
                    // An engine can be ignited and still deliver nothing. Two reasons show up in the
                    // flight file and nowhere else: its own throttle never leaves zero (the vessel's
                    // throttle is not reaching it), or its thrust limiter is at zero. The recorded
                    // flight had exactly that - ignited, 53 % commanded, zero acceleration for the
                    // whole descent and no plume. So: make sure the limiter is open, and record what
                    // the engine itself thinks it is doing.
                    if (e.EngineIgnited && (s.mainThrottle > 0 || burnOrdered))
                    {
                        if (e.thrustPercentage < 100) e.thrustPercentage = 100;
                        EngineThrottle = e.currentThrottle;
                        EngineFlameout = e.flameout;
                        EngineIgnited = e.EngineIgnited;
                    }
                }
                // One line with what the booster brought: which engines the law found, whether they
                // are lit and whether they have anything to burn. A new rocket that does not land
                // while the throttle reads fine is a question this answers in one look.
                if (!enginesReported)
                {
                    enginesReported = true;
                    int lit = 0, starved = 0;
                    List<ModuleEngines> allEngines = vessel.parts
                        .SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).ToList();
                    foreach (ModuleEngines e in allEngines)
                    {
                        if (e.EngineIgnited) lit++;
                        if (!HasPropellant(e)) starved++;
                    }
                    Debug.Log("[PhysStageRecovery] Triebwerke " + vessel.id + ": " + allEngines.Count
                        + " im Schiff, " + engines.Count + " nutzbar, " + lit + " gezuendet, "
                        + starved + " ohne Treibstoff");
                }
                DeploySystems(v); vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                TiltDegrees = core.Attitude.Tilt;
                Status = (Predictive ? PredictiveStatus(s) : core.Landing.Status)
                    + (unsupported ? " - ungeeignetes Triebwerk" :
                    engines.Count == 0 ? " - kein nutzbarer Treibstoff/Schub" : " - Schub " + (100 * s.mainThrottle).ToString("0") + "%");
            }
            catch (Exception e)
            {
                s.mainThrottle = 0; Stop("Fehler im Landeautomaten"); faulted = true;
                Status = "Landeautomat angehalten - siehe KSP.log";
                Debug.LogError("[PhysStageRecovery] Standalone landing failed " + vessel.id + ": " + e);
            }
        }
        // One tick of the predictive law. The adapter gathers what KSP knows, the law decides, and
        // the answer is written back into the FlightCtrlState the game is about to apply.
        private void DrivePredictive(FlightCtrlState s, MechJebPort.VesselState state,
            List<ModuleEngines> engines, bool unsupported)
        {
            if (descentConfig == null)
            {
                descentConfig = DescentConfig.Default();
                adapter = new DescentAdapter();
                descentConfig.AirDensity = adapter.AirDensity;
                descentConfig.Gravity = adapter.Gravity;
                descent = new DescentGuidance(descentConfig, new DescentPredictor(descentConfig, adapter));
            }
            ApplySettings(descentConfig);
            double now = Planetarium.GetUniversalTime();
            adapter.RefreshProfile(vessel, now);
            string note;
            List<ModuleEngines> usable = adapter.UsableEngines(vessel, state.Forward, out note);
            // The whole booster, not just the engines the attitude can point: this is what the
            // forecast integrates with.
            adapter.Measure(vessel, usable.Count > 0 ? usable : engines, 1);
            adapter.MeasureDrag(vessel);
            if (!adapter.EnginesUsable)
            {
                s.mainThrottle = 0;
                Status = "Landung: " + (note.Length > 0 ? note : "kein Triebwerk");
                return;
            }
            double clearance = state.AltitudeBottom;
            double trackedSlope = hasTrackedSample ? trackedSample.SlopeDegrees : double.NaN;
            DescentState descentState = adapter.Build(vessel, clearance, now, bottomOffset, trackedSlope);
            // "Aligned" is the gate that lets the guidance leave its startup phase, so it has to ask
            // the right question: is the booster holding the attitude the law is asking for? Asking
            // instead whether it points retrograde or upright looks equivalent and is not - the two
            // can disagree, and when they did, the booster held the commanded attitude perfectly
            // (attitude error zero) while the gate stayed shut for the whole entry.
            bool aligned = Vector3d.Dot(state.Forward, state.Up) > 0.95
                || Vector3d.Angle(state.Forward, -state.SurfaceVelocity.normalized) < 20;
            if (aimValid) aligned = Vector3d.Angle(state.Forward, aimAxis) < 20;
            // The law takes mass in kilogrammes and the adapter measures tonnes, which is the unit
            // KSP works in everywhere else. The conversion belongs here, at the one place the two
            // meet, so the forecast's drag, its mass flow and the tests' harness all agree.
            GuidanceStep step = descent.Step(descentState, 1000 * adapter.Mass, TimeWarp.fixedDeltaTime, aligned);
            if (!step.Valid)
            {
                s.mainThrottle = 0;
                Status = "Landung: Messwerte ungueltig";
                return;
            }
            if (recorder == null)
                recorder = new FlightRecorder(vessel.id.ToString(), DateTime.Now.ToString("MMdd-HHmm"));
            Record(step, descentState, clearance, engines);
            TargetSink = step.TargetSink;
            FreeAcceleration = step.FreeAcceleration;
            RequiredDeltaV = step.RequiredDeltaV;
            Unstoppable = step.Unstoppable || step.Abort;

            Vector3d up, east, north;
            DescentAdapter.HorizonFrame(vessel, out up, out east, out north);
            Vector3d axis;
            DescentAdapter.ThrustAxis(vessel, step, up, east, north, out axis);
            // Remember the command for the next tick's alignment gate, and report how it stands to
            // retrograde: 0 degrees is a booster flying engine-first into the airstream, 180 is one
            // flying nose-first, which is what the coast must never ask for.
            aimAxis = axis.normalized;
            aimValid = aimAxis.sqrMagnitude > 0.5;
            AimToRetrogradeDegrees = Vector3d.Angle(aimAxis, -state.SurfaceVelocity.normalized);
            // The thrust axis becomes the attitude command; the throttle follows from the magnitude
            // of the commanded acceleration over what the engines can deliver at full throttle.
            state.Forward = vessel.ReferenceTransform.up;
            core.Attitude.attitudeTo(axis, AttitudeReference.INERTIAL, descent);
            core.Attitude.Drive(s);
            s.mainThrottle = (float)step.Throttle;
        }

        // Everything the law saw and everything it decided, once per tick, into the flight file.
        //
        // The raw readings and the derived values sit side by side on purpose: the last four flights
        // each needed a launch to answer one question - was the aim retrograde, was the drag real, did
        // the gate cut the throttle, was the sink on the ladder - and every one of them is a column
        // here. A recorder must never disturb the flight, so anything it cannot read becomes an empty
        // field rather than an exception.
        private FlightRecorder recorder;
        // Once per landing, the engine inventory goes to the log.
        private bool enginesReported;
        // What the engine itself reports: its own throttle, its flameout flag, whether it thinks it
        // is lit. In the flight file next to the commanded throttle, these three say whether a
        // booster that produces no thrust is being starved, limited or simply not driven.
        public double EngineThrottle { get; private set; }
        public bool EngineFlameout { get; private set; }
        public bool EngineIgnited { get; private set; }
        private void Record(GuidanceStep step, DescentState descentState, double clearance, List<ModuleEngines> engines)
        {
            if (recorder == null || !recorder.Active) return;
            try
            {
                DescentPrediction prediction = descent.LastPrediction;
                double speed = Math.Sqrt(descentState.VelocityUp * descentState.VelocityUp
                    + descentState.VelocityEast * descentState.VelocityEast
                    + descentState.VelocityNorth * descentState.VelocityNorth);
                double mach = vessel.mainBody.GetSpeedOfSound(vessel.staticPressurekPa,
                    adapter.AirDensityNow(vessel)) > 0
                    ? speed / vessel.mainBody.GetSpeedOfSound(vessel.staticPressurekPa,
                        adapter.AirDensityNow(vessel)) : double.NaN;
                int ignited = 0; bool flameout = false;
                foreach (ModuleEngines e in engines)
                {
                    if (e.EngineIgnited) ignited++;
                    if (e.flameout) flameout = true;
                }
                bool interesting = step.IgnitionDue || step.Phase == DescentPhase.Burn
                    && recorder.Rows > 0 && clearance < LowAltitudeForRecord;
                recorder.Sample(Planetarium.GetUniversalTime(), interesting,
                    FlightRecorder.Row(
                        Planetarium.GetUniversalTime(), TimeWarp.fixedDeltaTime,
                        DescentPhases.Name(step.Phase), step.Coasting ? 1 : 0,
                        descent.IgnitionOrdered ? 1 : 0, step.Abort ? 1 : 0,
                        vessel.altitude, vessel.terrainAltitude, clearance,
                        adapter.LookingAhead ? adapter.GroundClearance : double.NaN,
                        adapter.SlopeDegrees, step.CutoffAltitude,
                        descentState.VelocityUp, descentState.VelocityEast, descentState.VelocityNorth,
                        descentState.Sink, descentState.LateralSpeed, speed, mach,
                        adapter.AirDensityNow(vessel), adapter.AirDensity(vessel.altitude),
                        adapter.DensityScale, vessel.staticPressurekPa, vessel.externalTemperature,
                        adapter.VesselDragCoefficient, adapter.VesselDragCd,
                        adapter.DragAcceleration, adapter.VacuumThrustAcceleration,
                        AvailableAcceleration, adapter.Mass, adapter.AvailableDeltaV,
                        step.TargetSink, step.FreeAcceleration, step.RequiredDeltaV,
                        prediction != null && prediction.Valid ? prediction.TouchdownSpeed : double.NaN,
                        prediction != null && prediction.Valid ? prediction.SpeedMargin : double.NaN,
                        prediction != null && prediction.Valid && prediction.Unstoppable ? 1 : 0,
                        prediction != null && prediction.Valid && prediction.IgnitionNeeded ? 1 : 0,
                        prediction != null && prediction.Valid ? prediction.BurnSeconds : double.NaN,
                        step.AccelerationUp, step.AccelerationEast, step.AccelerationNorth,
                        step.Throttle, ThrottleCutReason,
                        step.Up, step.East, step.North, AimToRetrogradeDegrees, AttitudeError,
                        ActualAcceleration, engines.Count, ignited, flameout ? 1 : 0, EngineThrottle, EngineFlameout ? 1 : 0,
                        vessel.LandedOrSplashed ? 1 : 0, descent.IgnitionReason, Status));
            }
            catch (Exception)
            {
                // never let the recorder break the landing
            }
        }

        private const double LowAltitudeForRecord = 1000;

        // Everything the player can tune lives in settings.cfg; the rest of the law is fixed. The
        // two that matter most are the sink rate it aims for at the ground and how high the vertical
        // final phase begins, because together they decide how soft the landing is and how much
        // propellant it costs.
        private void ApplySettings(DescentConfig config)
        {
            config.Terminal.TouchdownSpeed = Math.Max(0.5, settings.TouchdownSpeed);
            config.Terminal.Altitude = Math.Max(1, settings.TerminalAltitude);
            config.Predictor.CaptureAltitude = Math.Max(10, Math.Min(5000, settings.CaptureAltitude));
            config.Terminal.MaxTiltDegrees = settings.TiltLimit;
            config.Terminal.HighAltitudeTiltDegrees = Math.Min(60, settings.TiltLimit * 2);
            config.Predictor.ThrustReserve = settings.ThrustReserve;
            // The forecast aims for the same speed the profile is anchored at, never above it.
            config.Predictor.TargetTouchdownSpeed = config.Terminal.TouchdownSpeed;
        }

        // One line with every number the predictive law decided on, so a flight can be read back
        // without guessing which value moved first. Written once a second in the last hundred
        // metres and once every five above that, next to the existing Landing check line.
        public string PredictLine(double clearance, double sink, double lateral, double angular)
        {
            if (descent == null || !Predictive) return null;
            DescentPrediction prediction = descent.LastPrediction;
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("[PhysStageRecovery] Landing predict ").Append(vessel.id);
            text.Append(" state=").Append(DescentPhases.Name(descent.Phase));
            if (descent.LastStep.HasValue && descent.LastStep.Value.Coasting) text.Append(" (Coast: Luft bremst)");
            text.Append(" clearance=").Append(Number(clearance, "m"));
            text.Append(" slope=").Append(Number(adapter == null ? double.NaN : adapter.SlopeDegrees, "deg"));
            if (adapter != null && adapter.LookingAhead)
                text.Append(" voraus=").Append(Number(adapter.GroundClearance, "m"));
            text.Append(" sink=").Append(Number(sink, "m/s"));
            text.Append(" lateral=").Append(Number(lateral, "m/s"));
            text.Append(" speed=").Append(Number(Math.Sqrt(sink * sink + lateral * lateral), "m/s"));
            text.Append(" drag=").Append(Number(descent.LastStep.HasValue
                ? descent.LastStep.Value.CoastDrag : double.NaN, "m/s2"));
            text.Append(" rho=").Append(Number(adapter == null ? double.NaN : adapter.DensityScale, "x"));
            // The coast's own decision, which is what an entry has to be read from: the engines stay
            // dark while the whole flown speed is inside the budget the remaining height can pay for.
            if (descent.LastStep.HasValue && descent.LastStep.Value.CoastBudget > 0)
                text.Append(" coastBudget=").Append(Number(descent.LastStep.Value.CoastBudget, "m/s"));
            text.Append(" zielSink=").Append(Number(TargetSink, "m/s"));
            if (descent.LastStep.HasValue)
            {
                text.Append(" vektorBremse=").Append(descent.LastStep.Value.VectorBraking ? "ja" : "nein");
                text.Append(" notbremsung=").Append(descent.LastStep.Value.EmergencyBraking ? "ja" : "nein");
            }
            text.Append(" aFrei=").Append(Number(FreeAcceleration, "m/s2"));
            text.Append(" cmd=").Append(Number(100 * CommandedThrottle, "%"));
            text.Append(" aus=").Append(Number(adapter == null ? double.NaN : adapter.AvailableAcceleration, "m/s2"));
            text.Append(" cut=").Append(Number(LastCutoff, "m"));
            text.Append(" tilt=").Append(Number(TiltDegrees, "deg"));
            text.Append(" lage=").Append(Number(aimValid ? AimToRetrogradeDegrees : double.NaN, "deg"));
            text.Append(" err=").Append(Number(AttitudeError, "deg"));
            text.Append(" torque=").Append(AvailableTorque.ToString("F2"));
            text.Append(" mass=").Append(Number(adapter == null ? double.NaN : adapter.Mass, "t"));
            if (prediction != null && prediction.Valid)
            {
                text.Append(" tdPred=").Append(Number(prediction.TouchdownSpeed, "m/s"));
                text.Append(" planZuendung=").Append(Number(prediction.IgnitionAltitude, "m"));
                text.Append(" zielHoehe=").Append(Number(descentConfig.Predictor.CaptureAltitude, "m"));
                text.Append(" vBeiZielhoehe=").Append(Number(prediction.CaptureSpeed, "m/s"));
                text.Append(" querBeiZielhoehe=").Append(Number(prediction.CaptureLateralSpeed, "m/s"));
                text.Append(" reserve=").Append(Number(prediction.SpeedMargin, "m/s"));
                text.Append(" zuenden=").Append(prediction.IgnitionNeeded ? "ja" : "nein");
                text.Append(" brennt=").Append(Number(prediction.BurnSeconds, "s"));
                text.Append(" dV=").Append(Number(prediction.RequiredDeltaV, "m/s"));
                text.Append(" planMs=").Append(Number(prediction.CalculationMilliseconds, "ms"));
                text.Append(" kandidaten=").Append(prediction.Candidates);
                text.Append(" ersatzplan=").Append(prediction.UsedBestEffort ? "ja" : "nein");
                text.Append(" planAlter=").Append(Number(Planetarium.GetUniversalTime() - prediction.CalculatedAt, "s"));
            }
            else text.Append(" tdPred=--");
            if (prediction != null && !string.IsNullOrEmpty(prediction.FailureReason))
                text.Append(" planFehler=").Append(prediction.FailureReason);
            text.Append(" dVda=").Append(Number(RequiredDeltaV, "m/s"));
            text.Append(" dVvorrat=").Append(Number(AvailableDeltaV, "m/s"));
            if (Unstoppable) text.Append(" NICHT-ABFANGBAR");
            if (ThrottleCutReason.Length > 0) text.Append(" schubAus=").Append(ThrottleCutReason);
            if (descent.Aborted) text.Append(" ABBRUCH sink=").Append(Number(descent.AbortSpeed, "m/s"));
            return text.ToString();
        }

        // One short line for the flight window: what the landing law currently expects to happen.
        // The reserve is the number to watch - it goes negative before something goes wrong - and
        // the predicted touchdown speed is what the booster would arrive with if the burn started
        // right now.
        public string LandingSummary()
        {
            if (descent == null || !Predictive) return null;
            DescentPrediction prediction = descent.LastPrediction;
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append(DescentPhases.Name(descent.Phase));
            text.Append(" · Ziel ").Append(TargetSink.ToString("0.0")).Append(" m/s");
            if (prediction != null && prediction.Valid)
            {
                text.Append(" · voraussichtlich ").Append(prediction.TouchdownSpeed.ToString("0.0")).Append(" m/s");
                text.Append(" · Reserve ").Append(prediction.SpeedMargin.ToString("+0.0;-0.0;0.0")).Append(" m/s");
            }
            text.Append(" · Schub ").Append((100 * CommandedThrottle).ToString("0")).Append(" %");
            if (Unstoppable) text.Append(" · nicht mehr abfangbar");
            return text.ToString();
        }

        private double LastCutoff
        {
            get
            {
                if (descent == null || !descent.LastStep.HasValue) return double.NaN;
                return descent.LastStep.Value.CutoffAltitude;
            }
        }

        private static string Number(double value, string suffix)
        {
            return RecoveryPolicy.Finite(value) ? value.ToString("0.0") + suffix : "--";
        }

        private string PredictiveStatus(FlightCtrlState s)
        {
            if (descent == null) return "Landung: bereit";
            string text = "Landung " + DescentPhases.Name(descent.Phase)
                + ": " + TargetSink.ToString("0.0") + " m/s, Schub " + (100 * s.mainThrottle).ToString("0") + "%";
            if (Unstoppable) text += " - nicht mehr abfangbar";
            if (descent.LastStep.HasValue && descent.LastStep.Value.EmergencyBraking) text += " - Notbremsung";
            return text;
        }

        private void UpdateTorque(MechJebPort.VesselState state)
        {
            state.TorqueAvailable = Vector3d.zero; state.TorqueGimbal.Positive = Vector3d.zero;
            Array.Clear(torquePositive, 0, 4); Array.Clear(torqueNegative, 0, 4);
            Vector3d rcsPositive = Vector3d.zero, rcsNegative = Vector3d.zero;
            foreach (Part part in vessel.parts)
                foreach (PartModule module in part.Modules)
                {
                    ITorqueProvider provider = module as ITorqueProvider;
                    if (!module.isEnabled || provider == null) continue;
                    ModuleRCS rcs = module as ModuleRCS;
                    if (rcs != null)
                    {
                        // MechJeb VesselState.UpdateRCSThrustAndTorque, without the optional
                        // external RCS balancer. Stock GetPotentialTorque does not model nozzles.
                        if (!vessel.ActionGroups[KSPActionGroup.RCS] || part.ShieldedFromAirstream
                            || !rcs.rcsEnabled || rcs.isJustForShow || rcs.flameout || !rcs.rcs_active) continue;
                        Vector3 axes = new Vector3(rcs.enablePitch ? 1 : 0, rcs.enableRoll ? 1 : 0, rcs.enableYaw ? 1 : 0);
                        foreach (Transform t in rcs.thrusterTransforms)
                        {
                            if (!t.gameObject.activeInHierarchy) continue;
                            Vector3d forceDirection = rcs.useZaxis ? -t.forward : -t.up;
                            float power = rcs.thrusterPower * rcs.thrustPercentage * 0.01f;
                            if (FlightInputHandler.fetch != null && FlightInputHandler.fetch.precisionMode)
                            {
                                if (rcs.useLever)
                                { float lever = rcs.GetLeverDistance(t, forceDirection, vessel.CurrentCoM); if (lever > 1) power /= lever; }
                                else power *= rcs.precisionFactor;
                            }
                            Vector3d torqueRcs = Vector3.Scale(vessel.GetTransform().InverseTransformDirection(
                                Vector3.Cross(t.position - vessel.CurrentCoM, forceDirection * power)), axes);
                            for (int i = 0; i < 3; i++)
                                if (torqueRcs[i] >= 0) rcsPositive[i] += torqueRcs[i]; else rcsNegative[i] -= torqueRcs[i];
                        }
                        continue;
                    }
                    Vector3 positive, negative;
                    provider.GetPotentialTorque(out positive, out negative);
                    if (!RecoveryPolicy.Finite(positive.sqrMagnitude) || !RecoveryPolicy.Finite(negative.sqrMagnitude)) continue;
                    int group = module is ModuleReactionWheel ? 0 : module is ModuleControlSurface ? 1 : module is ModuleGimbal ? 2 : 3;
                    // MechJeb Vector6 accumulates the signed torque by provider category.
                    // Wheels/gimbals report identical positive/negative magnitudes in stock KSP.
                    if (group == 0 || group == 2) negative = -negative;
                    for (int i = 0; i < 3; i++)
                    {
                        torquePositive[group][i] += Math.Max(0, positive[i]) + Math.Max(0, negative[i]);
                        torqueNegative[group][i] += Math.Max(0, -positive[i]) + Math.Max(0, -negative[i]);
                    }
                }
            state.TorqueAvailable += Vector3d.Max(rcsPositive, rcsNegative);
            for (int group = 0; group < 4; group++) state.TorqueAvailable += Vector3d.Max(torquePositive[group], torqueNegative[group]);
            state.TorqueGimbal.Positive = torquePositive[2];
        }
        private void DeploySystems(MechJebPort.VesselState state)
        {
            // The landing gear is handled by TrackedBooster.UpdateLandingSystems for both landing
            // paths, so that the same rule decides when it comes down.
            if (state.AltitudeBottom < 10) LandingSystems.SetBrakes(vessel, true);
            if (!settings.AutoArm || !vessel.mainBody.atmosphere || vessel.verticalSpeed >= 0) return;
            foreach (ModuleParachute chute in vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleParachute>()))
            {
                string reason;
                ParachuteDeployment.TryArm(chute, settings.AutoArm, vessel.verticalSpeed < 0, vessel.terrainAltitude, out reason);
            }
        }
        public void Stop(string reason = null, bool keepThrust = false)
        {
            if (descent != null) descent.CancelPrediction();
            if (reason != null) StopReason = reason;
            RecoveryReady = false;
            // No live guidance readings while the controller is detached; a stale tilt or throttle
            // must not look like a current command in the log.
            AttitudeError = double.NaN; CommandedThrottle = double.NaN;
            if (!connected) return;
            connected = false;
            if (vessel == null) return;
            vessel.OnFlyByWire -= Control; core = null;
            // The predictive law holds continuity state - the last commanded lateral acceleration,
            // the phase, the ignition latch. A detached autopilot must not leave any of it behind
            // for the next time it is switched on.
            if (descent != null) descent.Reset();
            // Close the flight file and name it in the log, so a recording is found without having to
            // know where to look for it.
            if (recorder != null)
            {
                Debug.Log("[PhysStageRecovery] " + recorder.Summary());
                recorder = null;
            }
            if (vessel == FlightGlobals.ActiveVessel) return;
            vessel.ctrlState.mainThrottle = 0;
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, oldRcs);
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, oldSas);
            if (oldSas) vessel.Autopilot.Enable(oldMode); else vessel.Autopilot.Disable();
        }
        public void Shutdown()
        {
            Stop("Verfolgung beendet");
            if (vessel == null || vessel == FlightGlobals.ActiveVessel) return;
            vessel.ctrlState.mainThrottle = 0;
            foreach (ModuleEngines e in controlledEngines)
                if (e != null && e.vessel != null && e.vessel != FlightGlobals.ActiveVessel
                    && e.vessel.GetCrewCount() == 0 && e.EngineIgnited) e.Shutdown();
            controlledEngines.Clear(); LandingSystems.SetBrakes(vessel, true);
        }
    }
}
