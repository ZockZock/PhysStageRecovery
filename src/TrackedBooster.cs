using System;
using System.Collections.Generic;
using System.Linq;
using BoosterWatch.Guidance;
using UnityEngine;

namespace BoosterWatch
{
    // Ausgang eines verfolgten Boosters. None = noch unterwegs.
    public enum BoosterResult { None, Recovered, Landed, LandedUnconfirmed, Crashed, Destroyed, Ended }

    public sealed class TrackedBooster
    {
        public Vessel Vessel;
        public Guid Id;
        public string Name, Status = "Erfasst";
        public readonly RecoveryPolicy Policy = new RecoveryPolicy();
        public readonly DescentGate Descent = new DescentGate();
        public RecoveryDecision Decision;
        public DescentSample Sample;
        public double Distance, SurfaceAltitude;
        public int OpenChutes, TotalChutes;
        public VesselRanges OriginalRanges, ExtendedRanges;
        public bool Finished;
        public bool ImpactFailed;
        // Wie es fuer diesen Booster ausging, fuer die dauerhafte Anzeige im Kamerafenster.
        public BoosterResult Result;
        // Sinken und Seitenfahrt beim Bodenkontakt / bei der Bergung [m/s].
        public double ResultSink = double.NaN, ResultLateral = double.NaN, ResultFunds;
        public void SetResult(BoosterResult result)
        {
            Result = result;
            ResultSink = Sample.Sink; ResultLateral = Sample.Horizontal;
        }
        // True while the vessel cannot be controlled by KSP at all - no control module, or a probe
        // without a connection. The engines would ignore every throttle command.
        public bool ControlMissing;
        private bool reportedNoControl;
        private double firstMeasure = double.NaN;
        public string GearStatus = "";
        public bool GearRequested;
        // True while the booster is at the exact terrain height but KSP built no ground collider
        // for it to land on. See TouchdownPolicy.HeightContact.
        public bool SyntheticContact;
        // Diagnostics for the terrain probe: the computed lower extent of the hull and the
        // collider that was rejected as implausible, if any.
        public double GroundDepth = double.NaN;
        public string RejectedCollider = "";
        private readonly HeatGuard heat = new HeatGuard();
        private bool heatReported;
        public HeatGuard Heat { get { return heat; } }
        private readonly ControlSurfaceFlow surfaces = new ControlSurfaceFlow();
        // Hoehe ueber Grund, ab der die Schirme scharf werden, und ob sie es schon duerfen.
        public double ChuteArmHeight = double.NaN;
        public bool ChuteArmAllowed = true;
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        public double LastContactTime = double.NaN;
        public readonly HashSet<uint> ContactParts = new HashSet<uint>();
        public TouchdownOutcome TouchdownOutcome;
        // Entscheidung beim Abschalten des Triebwerks in Schnitthoehe (CutoffPolicy). Einmal
        // gefaellt, gilt sie fuer den Bodenkontakt - was in den letzten 1,5 m passiert, zaehlt nicht.
        public TouchdownOutcome CutoffVerdict = TouchdownOutcome.Unconfirmed;
        public double CutoffTime = double.NaN;
        public string CutoffReason = "";
        public readonly HashSet<uint> CutoffParts = new HashSet<uint>();
        public readonly HashSet<uint> KnownParts = new HashSet<uint>();
        public readonly PoweredLanding Landing;
        // Eigener Boden unter dem Booster, solange KSP weit weg keinen baut (GroundPatchPolicy).
        private readonly GroundPatch groundPatch = new GroundPatch();
        public readonly BoosterReadout Readout = new BoosterReadout();
        public string StageStatus = "";
        public Part Anchor;
        private double lastStageTime = double.NegativeInfinity;
        private readonly VesselRangeTransition rangeTransition;
        private double nextLandingLog;
        private double lastClearance = double.NaN;
        private string lastChuteNote = "";
        // Ground track of the previous measurement, for the horizontal speed cross-check.
        private readonly GroundTrack groundTrack = new GroundTrack();

        public TrackedBooster(Vessel vessel, Settings settings)
        {
            Vessel = vessel; Id = vessel.id; Name = vessel.vesselName;
            Landing = new PoweredLanding(vessel);
            Landing.Heat = heat;
            foreach (Part part in vessel.parts) KnownParts.Add(part.flightID);
            Anchor = vessel.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleCommand>() != null)
                ?? vessel.parts.FirstOrDefault(p => p.FindModulesImplementing<ModuleEngines>().Any(PoweredLanding.Suitable))
                ?? vessel.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleParachute>() != null) ?? vessel.rootPart;
            OriginalRanges = vessel.vesselRanges;
            rangeTransition = new VesselRangeTransition(OriginalRanges);
            RequestRange(settings.PhysicsRange);
        }

        public void RequestRange(float range)
        {
            rangeTransition.Request(Vessel.vesselRanges, range);
            ExtendedRanges = rangeTransition.Current;
            Vessel.vesselRanges = ExtendedRanges;
        }

        public void ExtendUnpack()
        {
            rangeTransition.Advance();
        }

        public void Restore()
        {
            Landing.Shutdown();
            groundPatch.Dispose();
            // Put the stock temperature limits back before the booster leaves the tracked set.
            heat.Restore(Vessel);
            if (heat.PeakRatio > 0 && !heatReported)
            {
                heatReported = true;
                EntryBurnPolicy burn = Landing.EntryBurn.Policy;
                Debug.Log("[PhysStageRecovery] Hitze " + Id + ": Spitze " + (100 * heat.PeakRatio).ToString("0")
                    + " % der Grenze (" + HeatPolicy.Key(heat.Mode) + ", " + heat.PeakPart + ") bei "
                    + (heat.PeakAltitude / 1000).ToString("0.0") + " km, " + heat.PeakSpeed.ToString("0") + " m/s"
                    + (burn != null && burn.Burns > 0 ? "; Eintrittsburn " + burn.Burns + "x, " + burn.Spent.ToString("0") + " m/s" : "")
                    + ".");
            }
            surfaces.Restore();
            if (Vessel != null && ReferenceEquals(Vessel.vesselRanges, ExtendedRanges))
                Vessel.vesselRanges = OriginalRanges;
        }

        public void Measure(Settings settings, bool recoveryEnabled)
        {
            Vessel v = Vessel;
            // Runs for packed vessels too, so a booster that reenters while it is not the physics
            // focus still has its protection in place before KSP checks it.
            // Never on the vessel the player flies (a tracked booster can be switched to).
            heat.Update(v, settings.HeatMode, v != FlightGlobals.ActiveVessel);
            // Steuerflaechen im Rueckwaertsflug umkehren (ControlSurfaceFlow). Nur mit Physik: ein
            // gepackter Booster hat keine Luft, und dort bleibt der letzte Stand.
            if (v == FlightGlobals.ActiveVessel) surfaces.Restore();
            else if (v.loaded && !v.packed) surfaces.Update(v, true, Id.ToString());
            KnownParts.Clear();
            foreach (Part part in v.parts) KnownParts.Add(part.flightID);
            Distance = Vector3d.Distance(v.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D());
            bool physics = v.loaded && !v.packed && !v.HoldPhysics;
            // KSP's engines only answer a controllable vessel (see TrackingAcceptance), so a booster
            // without a control module cannot be flown - the autopilot would command thrust that never
            // arrives. Re-read every tick: a probe can lose and regain its connection. The first second
            // is left alone on purpose: KSP computes the flag in Vessel.LateUpdate, and a stage
            // separated in this very frame still carries the value from before it existed. A stage
            // that has no control module at all is already refused when it is scanned.
            double now = Planetarium.GetUniversalTime();
            // The previous Sample is still in place here (and empty on the first call, Time = 0),
            // so the clock is read directly: with Sample.Time the grace second was one tick long.
            if (double.IsNaN(firstMeasure)) firstMeasure = now;
            ControlMissing = !v.IsControllable && now - firstMeasure > 1.0;
            if (ControlMissing && settings.PoweredLanding && !reportedNoControl)
            {
                reportedNoControl = true;
                Debug.Log("[PhysStageRecovery] Kein Kontrollmodul an " + v.id + " (" + v.vesselName
                    + ") - Triebwerkslandung nicht moeglich, es bleibt bei Fallschirmen.");
            }
            Sample = new DescentSample { Time = now,
                PhysicsActive = physics, Clearance = double.NaN,
                Eligible = !ImpactFailed && recoveryEnabled && v != FlightGlobals.ActiveVessel && v.GetCrewCount() == 0
                    && v.mainBody.isHomeWorld && !v.LandedOrSplashed && Distance <= settings.PhysicsRange };
            if (!physics) { Landing.Stop("physik inaktiv"); Descent.Reset(); groundPatch.Dispose(); Decision = Policy.Evaluate(Sample, settings.Limits); Status = Decision.Reason; return; }

            Readout.Update(v, Sample.Time);

            OpenChutes = 0; TotalChutes = 0;
            List<ModuleParachute> chutes = new List<ModuleParachute>();
            foreach (Part p in v.parts)
            {
                foreach (ModuleEngines engine in p.FindModulesImplementing<ModuleEngines>())
                    if (engine.finalThrust > 0.001f) Sample.HasThrust = true;
                chutes.AddRange(p.FindModulesImplementing<ModuleParachute>());
            }
            double altitude = (v.CoMD - v.mainBody.position).magnitude - v.mainBody.Radius;
            Descent.Observe(Sample.Time, altitude, v.verticalSpeed, physics, Sample.HasThrust);
            // Schirme erst kurz vor der Oeffnungshoehe scharf (ParachuteDeployment.ArmingHeight).
            double groundBelow = v.mainBody.ocean ? Math.Max(0, v.terrainAltitude) : v.terrainAltitude;
            double fallSpeed = v.verticalSpeed < 0 ? v.srf_velocity.magnitude : 0;
            ChuteArmHeight = ParachuteDeployment.ArmingHeight(settings.ChuteHeight, fallSpeed);
            ChuteArmAllowed = !(altitude - groundBelow > ChuteArmHeight && altitude > ChuteArmHeight);
            foreach (ModuleParachute chute in chutes)
                {
                    TotalChutes++;
                    // The opening height is a setting, not an arming action: it has to reach canopies
                    // that the staging system armed as well, or those open at the stock 1000 m above
                    // the sea while the mod's own open 3000 m above the ground - one cluster, two
                    // heights. Arming itself still goes through the normal KSP module.
                    if (v != FlightGlobals.ActiveVessel && v.mainBody.atmosphere && settings.AutoArm)
                    {
                        // The opening height is a setting; it applies to running boosters at once.
                        ParachuteDeployment.OpenAboveGround = (float)settings.ChuteHeight;
                        // Die ganze Fahrt, solange er faellt: der Schirm bremst beide Anteile.
                        double chuteSink = v.verticalSpeed < 0 ? v.srf_velocity.magnitude : 0;
                        if (ParachuteDeployment.SetOpeningHeight(chute, v.terrainAltitude, chuteSink) > 0)
                        {
                            // The canopy has to survive the early opening it is asked for.
                            if (settings.HeatImmune) ParachuteDeployment.Protect(chute);
                            string reason;
                            // One descent gate for arming AND forced opening. The forced opening used
                            // a bare "vertical speed < 0" and so skipped the DescentGate that exists to
                            // keep canopies shut on a stage that only just separated.
                            bool descending = Landing.OwnsControl ? v.verticalSpeed < 0
                                : Descent.AllowsOpening(Sample.Time, v.verticalSpeed);
                            if (!ChuteArmAllowed)
                                reason = "wartet bis " + ChuteArmHeight.ToString("0") + " m ueber Grund";
                            else if (ParachuteDeployment.TryArm(chute, settings.AutoArm, descending,
                                v.terrainAltitude, out reason))
                            {
                                Debug.Log("[PhysStageRecovery] Arming chute after confirmed descent: vessel=" + Id
                                    + " part=" + chute.part.flightID + " vertical=" + v.verticalSpeed + " altitude=" + altitude
                                    + " safe=" + chute.deploymentSafeState
                                    + " oeffnetBei=" + chute.deployAltitude.ToString("0") + " (Boden "
                                    + v.terrainAltitude.ToString("0") + ")");
                            }
                            else if (reason != lastChuteNote && reason != "Automatik aus")
                            {
                                // One line per reason so a log always shows why a canopy is still closed.
                                lastChuteNote = reason;
                                Debug.Log("[PhysStageRecovery] Chute closed: vessel=" + Id + " part=" + chute.part.flightID
                                    + " " + reason + " safe=" + chute.deploymentSafeState + " speed="
                                    + v.srf_velocity.magnitude.ToString("0") + " q=" + v.dynamicPressurekPa.ToString("0.0")
                                    + " oeffnetBei=" + chute.deployAltitude.ToString("0")
                                    + " altitude=" + altitude.ToString("0"));
                            }
                            // KSP's state machine postpones the opening on its own, so the mod opens the
                            // canopy once its rule says it is due. From here KSP inflates it.
                            // Without the heat protection KSP's own safety verdict stands: a canopy it
                            // rates unsafe would be burnt off the moment it is forced open.
                            bool mayOpen = chute.isEnabled
                                && (settings.HeatImmune || ParachuteDeployment.Allowed(chute.deploymentSafeState));
                            string openReason;
                            if (mayOpen && ParachuteDeployment.TryOpen(chute, settings.AutoArm, descending, altitude,
                                v.terrainAltitude, chuteSink, out openReason))
                                Debug.Log("[PhysStageRecovery] Chute opened: vessel=" + Id + " part=" + chute.part.flightID
                                    + " altitude=" + altitude.ToString("0") + " Boden=" + v.terrainAltitude.ToString("0")
                                    + " sink=" + chuteSink.ToString("0") + " oeffnetBei=" + chute.deployAltitude.ToString("0")
                                    + " ueberGrund=" + (altitude - v.terrainAltitude).ToString("0"));
                        }
                    }
                    if (chute.deploymentState == ModuleParachute.deploymentStates.DEPLOYED) OpenChutes++;
                }
            Sample.ChutesOpen = TotalChutes > 0 && OpenChutes == TotalChutes;
            Sample.Sink = -v.verticalSpeed;
            Sample.Horizontal = HorizontalSpeed(v, Sample.Time);
            Sample.Angular = v.angularVelocity.magnitude;

            CelestialBody body = v.mainBody;
            if (body.pqsController != null)
            {
                Vector3d origin = v.GetWorldPos3D();
                double ground = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(body.GetLatitude(origin), body.GetLongitude(origin))) - body.Radius;
                SurfaceAltitude = body.ocean ? Math.Max(0, ground) : ground;
                Vector3 up = (v.CoM - body.position).normalized;
                GroundDepth = Bottom(v, origin, up, out RejectedCollider);
                Sample.Clearance = (origin - body.position).magnitude - body.Radius - SurfaceAltitude + GroundDepth;
                // A terrain or building collider that KSP did build outranks the procedural height:
                // the coarse mesh can sit below it, and the booster really rests on the mesh.
                double worldDistance;
                bool worldGround = WorldColliderBelow(v, origin, up,
                    Sample.Clearance - GroundDepth, out worldDistance);
                Sample.Clearance = GroundSurface.Clearance(Sample.Clearance, GroundDepth, worldGround, worldDistance);
                Sample.TerrainKnown = RecoveryPolicy.Finite(ground) && RecoveryPolicy.Finite(GroundDepth);
                Sample.GroundClearance = Sample.Clearance;
                Sample.HullDepth = -GroundDepth;
                SyntheticContact = TouchdownPolicy.HeightContact(physics, Sample.TerrainKnown, worldGround, Sample.Clearance);
                // Sicherheitsnetz unter fernen Boostern: TerrainDetailBubble laesst KSP dort echtes
                // Gelaende mit Kollidern bauen, aber die feinsten Quads (nur sie haben Kollider) kommen
                // unter Zeitbudget und koennen einem schnellen Booster hinterherhinken. Bis dahin traegt
                // dieses Hoehenfeld. Ueber Wasser ist die Wasseroberflaeche die Referenz.
                bool overWater = body.ocean && ground < 0;
                if (v.mainBody.isHomeWorld && GroundPatchPolicy.Needed(Distance, Sample.Clearance, overWater))
                    groundPatch.Refresh(v, Sample.Time, Sample.Clearance);
                else
                    groundPatch.Dispose();
                // Liegt echtes Gelaende unter dem Booster, tritt das Hoehenfeld ganz zurueck (Bild und,
                // nach einigen Messungen Bestaetigung, auch der Kollider) - es ist bilinear auf 31 m
                // und kann bis zu einem Meter ueber dem echten Boden liegen.
                groundPatch.SetRealGround(worldGround);
                // The ground ahead of the booster, along the direction it is actually travelling, from
                // the procedural surface; the smaller of the two clearances is the one the descent
                // has to respect.
                Sample.SlopeDegrees = 0;
                Vector3d drift = Vector3d.Exclude(up, v.srf_velocity);
                double driftSpeed = drift.magnitude;
                if (Sample.TerrainKnown && driftSpeed > GroundScan.MinimumDrift)
                {
                    double ahead, slope;
                    var heightAt = DescentAdapter.TerrainHeightAt(v);
                    if (GroundScan.AlongFlightPath(origin, v.altitude, up, drift / driftSpeed, driftSpeed,
                        Sample.HullDepth, heightAt, out ahead, out slope))
                    {
                        Sample.SlopeDegrees = slope;
                        if (ahead < Sample.Clearance) Sample.Clearance = ahead;
                    }
                }
            }
            bool automationEligible = v != FlightGlobals.ActiveVessel && v.GetCrewCount() == 0
                && v.mainBody.isHomeWorld && !v.LandedOrSplashed && Distance <= settings.PhysicsRange;
            Landing.Step(settings, Sample, automationEligible && !ImpactFailed && !ControlMissing,
                Descent.AllowsOpening(Sample.Time, v.verticalSpeed));
            Sample.PoweredControlled = Landing.RecoveryReady;
            CaptureCutoff(settings);
            Decision = Policy.Evaluate(Sample, settings.Limits);
            Status = Distance > settings.PhysicsRange ? "Ausserhalb der eingestellten Reichweite" : Decision.Reason;
            if (settings.AutoArm && OpenChutes == 0 && TotalChutes > 0 && !Descent.Ready)
                Status = "Schirme gesperrt: warte auf bestaetigten Sinkflug";
            if (Decision.StableDescent)
                Status = "Aufsetzen abwarten: Bergung erst nach Bodenkontakt";
            UpdateLandingSystems(settings);
            if (Sample.TerrainKnown && Sample.Time >= nextLandingLog)
            {
                // One line per second on final approach, one per five seconds higher up, so the
            // drift and the guidance state can be followed over the whole descent.
            bool low = Sample.Clearance < 100;
                nextLandingLog = Sample.Time + (low ? 1 : 5);
                // The predictive law gets its own line with every number it decided on, so a flight
                // can be read back without guessing which value moved first.
                string predict = Landing.PredictLine(Sample.Clearance, Sample.Sink, Sample.Horizontal, Sample.Angular);
                if (predict != null) Debug.Log(predict);
                Debug.Log("[PhysStageRecovery] Landing check " + Id + " clearance=" + Sample.Clearance
                    + " sink=" + Sample.Sink + " horizontal=" + Sample.Horizontal + " angular=" + Sample.Angular
                    + " stable=" + Decision.StableSeconds + " chutes=" + OpenChutes + "/" + TotalChutes
                    + " gear=" + GearStatus + " tilt=" + Landing.TiltDegrees.ToString("0") + "deg"
                    + " nose=" + Number(Landing.ActualTiltDegrees, "deg")
                    + " control=" + Landing.ControlInput.ToString("F2") + " torque=" + Landing.AvailableTorque.ToString("F2")
                    // err and cmd separate the two ways a commanded burn becomes 0 %: the guidance
                    // never asked for thrust, or the safety gate in PoweredLanding removed it.
                    + " err=" + Number(Landing.AttitudeError, "deg") + " cmd=" + Number(100 * Landing.CommandedThrottle, "%")
                    + " heat=" + heat.Protected + " hitze=" + Number(100 * heat.Ratio, "%") + " tiefe=" + Number(GroundDepth, "m")
                    // The speed the braking works against is the whole surface velocity, not just the
                    // sink rate; gesamt makes that visible next to sink and horizontal.
                    + " gesamt=" + Number(Math.Sqrt(Sample.Sink * Sample.Sink + Sample.Horizontal * Sample.Horizontal), "m/s")
                    + " cda=" + Number(Landing.DragCoefficient, "")
                    + " thrustAcc=" + Number(Landing.AvailableAcceleration, "m/s2")
                    + " istAcc=" + Number(Landing.ActualAcceleration, "m/s2")
                    + (SyntheticContact ? " KONTAKT=hoehe" : "")
                    + (groundPatch.Exists ? " BODEN=eigen" : "")
                    + (RejectedCollider.Length > 0 ? " verworfen=" + RejectedCollider : "")
                    + " guidance=" + (Landing.Status.Length > 0 ? Landing.Status : "-")
                    + (Landing.StopReason.Length > 0 ? " | gestoppt: " + Landing.StopReason : "")
                    + " distance=" + Distance + " terrain=" + SurfaceAltitude + " result=" + Status);
            }
        }

        // Der erste Tick, in dem die Landeregelung in "Aufsetzen" steht, ist der Abschaltmoment.
        private void CaptureCutoff(Settings settings)
        {
            if (!double.IsNaN(CutoffTime) || Landing.Phase != DescentPhase.Touchdown || !Landing.OwnsControl) return;
            CutoffTime = Sample.Time;
            CutoffVerdict = CutoffPolicy.Evaluate(Sample.Sink, Sample.Horizontal, Sample.Angular,
                Landing.ActualTiltDegrees, Sample.GroundClearance, settings.Limits, settings.TiltLimit, out CutoffReason);
            CutoffParts.Clear();
            foreach (uint id in KnownParts) CutoffParts.Add(id);
            Debug.Log("[PhysStageRecovery] Triebwerk aus bei " + Number(Sample.GroundClearance, " m") + ": sink="
                + Number(Sample.Sink, "m/s") + " seitlich=" + Number(Sample.Horizontal, "m/s")
                + " drehung=" + Sample.Angular.ToString("0.00") + " neigung=" + Number(Landing.ActualTiltDegrees, "deg")
                + " -> " + (CutoffVerdict == TouchdownOutcome.Safe ? "wird geborgen"
                    : CutoffVerdict == TouchdownOutcome.Crashed ? "keine Bergung (" + CutoffReason + ")"
                    : "unklar (" + CutoffReason + "), Kontakt entscheidet"));
        }

        private static string Number(double value, string suffix)
        {
            return RecoveryPolicy.Finite(value) ? value.ToString("0.0") + suffix : "--";
        }

        // KSP's horizontal speed reading is wrong for a booster far from the active vessel: after a
        // floating origin shift one that was coming down under four canopies at 8 m/s read 175 m/s
        // sideways - exactly the planet's own surface speed - and was logged as a crash instead of
        // being recovered. The vessel's own ground track cannot be fooled that way, so when the two
        // disagree the smaller one is used. The comparison window and the arithmetic live in
        // GroundTrack, where they are tested: the old version refreshed its reference on every tick
        // and therefore never reached the comparison at all.
        private double HorizontalSpeed(Vessel v, double time)
        {
            Vector3d position = v.GetWorldPos3D();
            double latitude = v.mainBody.GetLatitude(position), longitude = v.mainBody.GetLongitude(position);
            // Radius of the booster's own position, not the planet's: the arc it flies at 40 km is
            // 6 % longer than the one on the ground below.
            return groundTrack.Resolve(v.horizontalSrfSpeed, latitude, longitude, time,
                (position - v.mainBody.position).magnitude);
        }

        // Conservative lower extent of the physical part colliders, not the vessel centre.
        //
        // A collider that reaches implausibly far below the vessel is not a hull collider but a
        // proxy of some kind. One such collider in a real flight reported the bottom of a small
        // booster as 22 m below its origin, which made the clearance read 22 m below the terrain
        // for the whole descent, held the autopilot in a 0.5 m/s descent through the ground and
        // finally turned a soft touchdown into a crash. Part POSITIONS are always the real
        // attachment points, so they bound what a hull collider can be; anything deeper is
        // skipped and named in the terrain probe.
        private double Bottom(Vessel v, Vector3d origin, Vector3 up, out string rejected)
        {
            rejected = "";
            double radius = 0;
            foreach (Part p in v.parts) radius = Math.Max(radius, Vector3.Distance(p.transform.position, origin));
            double limit = radius + 5, worst = -limit;
            double bottom = double.PositiveInfinity;
            foreach (Part p in v.parts)
                foreach (Collider collider in p.GetPartColliders())
                {
                    if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                    Bounds b = collider.bounds;
                    double halfHeight = Math.Abs(up.x) * b.extents.x + Math.Abs(up.y) * b.extents.y + Math.Abs(up.z) * b.extents.z;
                    double offset = Vector3.Dot(b.center - (Vector3)origin, up) - halfHeight;
                    if (offset < -limit)
                    {
                        if (offset <= worst) { worst = offset; rejected = p.name + "/" + offset.ToString("0.0") + "m"; }
                        continue;
                    }
                    bottom = Math.Min(bottom, offset);
                }
            // Vessels without usable colliders still need a sane lower extent.
            return RecoveryPolicy.Finite(bottom) ? bottom : -radius;
        }

        // Is there world geometry (terrain, scenery, a building) below the vessel? Vessel parts are
        // ignored, own and foreign alike, so a parachute of the main rocket can never be mistaken
        // for ground. KSP builds terrain colliders around the active vessel only, so far away this
        // finds nothing - which is exactly the case the height contact covers.
        private bool WorldColliderBelow(Vessel v, Vector3d origin, Vector3 up, double proceduralDistance, out double distance)
        {
            distance = double.PositiveInfinity;
            if (!RecoveryPolicy.Finite(proceduralDistance)) return false;
            double maxDistance = Math.Max(100, proceduralDistance + GroundSurface.MaxSurfaceOffset);
            // No candidate in this ray's range can be plausible when the real terrain is
            // further away. Avoid querying local/scaled scenery high above the surface.
            if (proceduralDistance > 30000 + GroundSurface.MaxSurfaceOffset) return false;
            int count = Physics.RaycastNonAlloc((Vector3)origin, -up, groundHits,
                (float)Math.Min(30000, maxDistance), GroundSurface.WorldLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = groundHits[i].collider;
                // Der eigene Boden zaehlt hier nicht. Sonst waere er fuer den Mod "fremder Boden",
                // die Hoehenerkennung fiele aus, und eine Stufe, die auf unserem Netz steht, wuerde
                // nie als aufgesetzt erkannt: keine Bergung, weiter offene Schirme, und weil das
                // Netz nicht mehr nachgefuehrt wird, rutscht sie bis ueber den Rand.
                if (collider == null || collider == groundPatch.Surface) continue;
                if (!GroundSurface.IsWorldSurface(collider.gameObject.layer,
                    collider.isTrigger, collider.GetComponentInParent<Part>() != null)) continue;
                if (!GroundSurface.IsPlausibleHit(proceduralDistance, groundHits[i].distance)) continue;
                if (groundHits[i].distance >= distance) continue;
                distance = groundHits[i].distance;
            }
            return RecoveryPolicy.Finite(distance);
        }

        // Landing gear and wheel brakes are used for both landing paths: the powered burn and
        // the parachute descent. This is the single place that lowers the gear, so both paths obey
        // the same rule.
        private void UpdateLandingSystems(Settings settings)
        {
            Vessel v = Vessel;
            if (v == null || !v.loaded || v.packed) return;
            double clearance = Sample.Clearance;
            double previous = lastClearance;
            lastClearance = clearance;
            if (!Sample.TerrainKnown || !RecoveryPolicy.Finite(clearance)) return;
            // The gear hangs on height and descent only, so every booster design behaves the same:
            // below 1000 m above ground and sinking, at any speed. A heavy booster is still fast at
            // 1000 m and brakes late, so a speed limit here would drop the legs only just above the
            // ground. The descent is read from the height trend instead of the reported sink rate:
            // a vessel the game has just created can report a rate that does not match its motion at
            // all, and one real booster was lowered onto its gear at decoupling that way.
            bool sinking = Sample.Sink > 0
                && (!RecoveryPolicy.Finite(previous) || clearance < previous);
            if (!GearRequested && clearance <= 1000 && sinking)
            {
                GearRequested = true;
                LandingSystems.DeployGear(v, out GearStatus);
                if (LandingSystems.HasBrakes(v)) GearStatus += ", Bremsen vorhanden";
                Debug.Log("[PhysStageRecovery] Gear " + Id + " " + GearStatus
                    + " clearance=" + clearance.ToString("0.0") + " sink=" + Sample.Sink.ToString("0.0"));
            }
            if (clearance < 10 && Sample.Sink > 0) LandingSystems.SetBrakes(v, true);
        }

        public bool AutoStage(Settings settings, JournalEntry entry)
        {
            if (entry == null) return false;
            if (entry.StageCursor < 0) entry.StageCursor = Math.Max(0, Vessel.currentStage);
            bool eligible = !Finished && Vessel != FlightGlobals.ActiveVessel && Vessel.loaded && !Vessel.packed
                && !Vessel.HoldPhysics && !Vessel.LandedOrSplashed && Vessel.GetCrewCount() == 0
                && Vessel.mainBody.isHomeWorld && Distance <= settings.PhysicsRange && Sample.TerrainKnown;
            int stage = StagePolicy.Next(Vessel.parts.Where(p => p.stagingOn && p.State != PartStates.DEAD)
                .Select(p => p.inverseStage), Math.Min(entry.StageCursor, Vessel.currentStage), settings.LastAutoStage);
            StageStatus = !settings.AutoStage ? "Autostage aus" : stage < 0 ? "Autostage: Stufengrenze erreicht"
                : "Autostage: naechste " + stage + ", bis " + settings.LastAutoStage + " bei " + settings.AutoStageHeight.ToString("0") + " m";
            if (stage < 0 || !StagePolicy.CanFire(settings.AutoStage, eligible, Sample.Clearance, Sample.Sink,
                settings.AutoStageHeight, Sample.Time, lastStageTime)) return false;
            Part[] parts = Vessel.parts.Where(p => p.inverseStage == stage && p.stagingOn).ToArray();
            // Commit before calling any module; do not re-fire on exceptions, topology changes or save/load.
            entry.StageCursor = stage; lastStageTime = Sample.Time;
            Vessel.currentStage = stage;
            Landing.Stop("Autostage hat gestagt"); Policy.Reset();
            foreach (Part p in parts)
                if (p != null && p.vessel == Vessel && p.vessel != FlightGlobals.ActiveVessel)
                    p.activate(stage, Vessel);
            KnownParts.Clear();
            Vessel remaining = Anchor != null && Anchor.vessel != null ? Anchor.vessel : Vessel;
            foreach (Part part in remaining.parts) KnownParts.Add(part.flightID);
            StageStatus = "Autostage: Stufe " + stage + " ausgeloest";
            Debug.Log("[PhysStageRecovery] AutoStage vessel=" + Id + " stage=" + stage + " clearance=" + Sample.Clearance);
            return true;
        }
    }
}
