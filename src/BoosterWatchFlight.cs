using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using KSP.UI.Screens;
using UnityEngine;

namespace BoosterWatch
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed partial class BoosterWatchFlight : MonoBehaviour
    {
        private readonly List<TrackedBooster> boosters = new List<TrackedBooster>();
        private readonly HashSet<uint> family = new HashSet<uint>();
        private readonly HashSet<uint> failedParts = new HashSet<uint>();
        private readonly HashSet<uint> attachedBoosterChutes = new HashSet<uint>();
        private readonly BoosterCamera cameraFeed = new BoosterCamera();
        private Settings settings;
        private Guid originId;
        private uint originRoot;
        private bool initialized, enabledMod = true, autoRecovery = true, visible = false, cameraEnabled = true;
        private bool uiVisible = true, faulted, warpLocked;
        private bool resizing, geometryDirty, cameraDirty;
        private float cameraSaveAt;
        private readonly WindowOpenPolicy windowOpenPolicy = new WindowOpenPolicy();
        private Rect cameraViewport;
        private Vector2 resizeMouse, resizeSize;
        private float nextToolbarAttempt;
        private bool optionsLoaded;
        private bool settingsOpen;
        private string rangeInput, settingsMessage = "";
        private string stageHeightInput, lastStageInput, landingSpeedInput, chuteHeightInput;
        private bool autoStageInput, poweredInput;
        private Vector2 settingsScroll;
        private float nextScan;
        private double nextMeasure;
        private bool gateLogged;
        private string skipNotice = "";
        private readonly HashSet<Guid> reportedSkips = new HashSet<Guid>();
        private int selection;
        private Rect window = new Rect(35, 95, 480, 570);
        private ApplicationLauncherButton toolbar;
        private Texture2D icon;
        private string notice = "Warte auf die aktive Rakete.";
        private const string HoverLock = "BoosterWatch.Hover", WarpLock = "BoosterWatch.Warp";
        // Degrees of camera turn per unit of the mouse axis, which is what the stock camera feels
        // like. One wheel notch is 0.1, so a notch changes the distance by eight percent.
        private const float CameraTurnSpeed = 4f;

        public void Start()
        {
            settings = Settings.Load();
            enabledMod = settings.ModEnabled; autoRecovery = settings.AutoRecovery; cameraEnabled = settings.CameraEnabled;
            cameraFeed.Distance = settings.CameraDistance; cameraFeed.Heading = settings.CameraHeading; cameraFeed.Pitch = settings.CameraPitch;
            window = new Rect(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
            rangeInput = (settings.PhysicsRange / 1000).ToString("0.##", CultureInfo.InvariantCulture);
            stageHeightInput = settings.AutoStageHeight.ToString("0", CultureInfo.InvariantCulture);
            lastStageInput = settings.LastAutoStage.ToString(CultureInfo.InvariantCulture);
            landingSpeedInput = settings.LandingSpeed.ToString("0.##", CultureInfo.InvariantCulture);
            chuteHeightInput = settings.ChuteHeight.ToString("0", CultureInfo.InvariantCulture);
            ParachuteDeployment.OpenAboveGround = (float)settings.ChuteHeight;
            autoStageInput = settings.AutoStage; poweredInput = settings.PoweredLanding;
            try { ParachuteGuard.Install(this); RailWarpGuard.Install(TrackingPhysics); }
            catch (Exception e)
            {
                faulted = true;
                notice = "Fallschirmschutz nicht verfuegbar. Details: KSP.log [PhysStageRecovery].";
                Debug.LogError("[PhysStageRecovery] Unable to install parachute guard: " + e);
            }
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
            GameEvents.onGUIApplicationLauncherReady.Add(AddToolbar);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveToolbar);
            GameEvents.onCrash.Add(OnCrash);
            GameEvents.onCrashSplashdown.Add(OnCrash);
            AddToolbar();
            Debug.Log("[PhysStageRecovery] 0.9.17 started; physics range " + settings.PhysicsRange + " m.");
        }

        private void AddToolbar()
        {
            if (toolbar != null || !ApplicationLauncher.Ready || ApplicationLauncher.Instance == null) return;
            icon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            for (int y = 0; y < 38; ++y)
                for (int x = 0; x < 38; ++x)
                {
                    bool canopy = y >= 23 && (x - 19) * (x - 19) + (y - 23) * (y - 23) < 225;
                    bool stringLeft = y > 8 && y < 23 && Math.Abs(x - (19 - (y - 8))) < 1.4;
                    bool stringRight = y > 8 && y < 23 && Math.Abs(x - (19 + (y - 8))) < 1.4;
                    bool booster = x >= 16 && x <= 22 && y >= 3 && y <= 10;
                    icon.SetPixel(x, y, canopy || stringLeft || stringRight || booster
                        ? new Color(0.3f, 0.95f, 0.8f, 1) : Color.clear);
                }
            icon.Apply();
            toolbar = ApplicationLauncher.Instance.AddModApplication(() => SetVisible(true), () => SetVisible(false),
                () => ScreenMessages.PostScreenMessage("PhysStageRecovery", 1, ScreenMessageStyle.UPPER_RIGHT),
                null, null, null, ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW, icon);
            toolbar.gameObject.name = "PhysStageRecovery";
            toolbar.Enable(false);
            if (visible) toolbar.SetTrue(false); else toolbar.SetFalse(false);
            Debug.Log("[PhysStageRecovery] Flight sidebar button registered.");
        }

        private void RemoveToolbar()
        {
            if (toolbar != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbar);
            toolbar = null;
            if (icon != null) Destroy(icon);
            icon = null;
        }

        private void HideUI() { uiVisible = false; InputLockManager.RemoveControlLock(HoverLock); }
        private void ShowUI() { uiVisible = true; }

        public void Update()
        {
            if (toolbar == null && Time.unscaledTime >= nextToolbarAttempt)
            { nextToolbarAttempt = Time.unscaledTime + 1; AddToolbar(); }
            if (Input.GetKeyDown(KeyCode.B) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                SetVisible(!visible);
            if (geometryDirty && !resizing && !Input.GetMouseButton(0)) SaveGeometry();
            if (!visible || !uiVisible || FlightDriver.Pause) InputLockManager.RemoveControlLock(HoverLock);
            HandleCameraInput();
            if (cameraDirty && !Input.GetMouseButton(1) && Time.unscaledTime >= cameraSaveAt) SaveGeometry();
            if (TrackingPhysics())
            {
                warpLocked = true;
                if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex != 0) TimeWarp.SetRate(0, true);
            }
            else ReleaseWarp();
        }

        // The pointer sits on the mod window: the feed camera takes the mouse, the stock camera keeps
        // it everywhere else. Hovering the window already locks the stock camera controls (see OnGUI),
        // so the two can never turn at the same time.
        private bool MouseOverWindow()
        {
            if (!visible || !uiVisible || FlightDriver.Pause) return false;
            return window.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        private void HandleCameraInput()
        {
            if (!cameraEnabled || boosters.Count == 0 || settingsOpen || !MouseOverWindow()) return;
            Vector2 mouse = new Vector2(Input.mousePosition.x - window.x, Screen.height - Input.mousePosition.y - window.y);
            if (!cameraViewport.Contains(mouse)) return;
            float oldDistance = cameraFeed.Distance, oldHeading = cameraFeed.Heading, oldPitch = cameraFeed.Pitch;
            if (Input.GetMouseButton(1))
                cameraFeed.Turn(Input.GetAxis("Mouse X") * CameraTurnSpeed, Input.GetAxis("Mouse Y") * CameraTurnSpeed);
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f) cameraFeed.Zoom(wheel);
            if (oldDistance != cameraFeed.Distance || oldHeading != cameraFeed.Heading || oldPitch != cameraFeed.Pitch)
            { cameraDirty = true; cameraSaveAt = Time.unscaledTime + 0.4f; }
        }

        private bool TrackingPhysics() { return enabledMod && !faulted && boosters.Any(b => !b.Finished && b.Vessel != null); }

        private void SetVisible(bool value)
        {
            visible = value;
            if (toolbar != null) { if (value) toolbar.SetTrue(false); else toolbar.SetFalse(false); }
            if (!value) { resizing = false; SaveGeometry(); InputLockManager.RemoveControlLock(HoverLock); }
        }

        private void SaveGeometry()
        {
            if ((!geometryDirty && !cameraDirty) || settings == null) return;
            settings.WindowWidth = window.width; settings.WindowHeight = window.height;
            settings.WindowX = window.x; settings.WindowY = window.y;
            settings.CameraDistance = cameraFeed.Distance; settings.CameraHeading = cameraFeed.Heading; settings.CameraPitch = cameraFeed.Pitch;
            if (settings.Save()) { geometryDirty = false; cameraDirty = false; }
        }

        private void ResizeWindow()
        {
            Event e = Event.current;
            Rect grip = new Rect(window.xMax - 24, window.yMax - 24, 24, 24);
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                resizing = true; resizeMouse = e.mousePosition;
                resizeSize = new Vector2(window.width, window.height); e.Use();
            }
            else if (resizing && e.type == EventType.MouseDrag)
            {
                Vector2 size = resizeSize + e.mousePosition - resizeMouse;
                window.width = Mathf.Clamp(size.x, Mathf.Min(480, Screen.width), Screen.width);
                window.height = Mathf.Clamp(size.y, Mathf.Min(540, Screen.height), Screen.height);
                geometryDirty = true; e.Use();
            }
            else if (resizing && e.rawType == EventType.MouseUp)
            { resizing = false; geometryDirty = true; e.Use(); }
        }

        public void FixedUpdate()
        {
            if (!optionsLoaded && RecoveryJournal.Instance != null)
            {
                if (!settings.HasBehaviorSettings)
                {
                    enabledMod = RecoveryJournal.Instance.Enabled; autoRecovery = RecoveryJournal.Instance.AutoRecovery;
                    settings.SetBehavior(enabledMod, autoRecovery); settings.Save();
                }
                else
                { RecoveryJournal.Instance.Enabled = enabledMod; RecoveryJournal.Instance.AutoRecovery = autoRecovery; }
                optionsLoaded = true;
            }
            if (!enabledMod || faulted || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready
                || FlightGlobals.ActiveVessel == null || FlightDriver.Pause) return;
            if (!gateLogged)
            {
                // One line per flight so a missing detection can be diagnosed from KSP.log.
                gateLogged = true;
                Debug.Log("[PhysStageRecovery] Gate: mod=" + enabledMod + " faulted=" + faulted
                    + " journal=" + (RecoveryJournal.Instance != null)
                    + " scenarioEnabled=" + (RecoveryJournal.Instance == null ? "?" : RecoveryJournal.Instance.Enabled.ToString())
                    + " settings=" + (settings != null) + " loadedVessels=" + FlightGlobals.VesselsLoaded.Count
                    + " poweredLanding=" + (settings == null ? "?" : settings.PoweredLanding.ToString())
                    + " autoArm=" + (settings == null ? "?" : settings.AutoArm.ToString()));
            }
            try
            {
                if (!initialized) InitializeMission();
                FollowStageSeparations();
                // Always keep the range change on consecutive physics ticks.
                foreach (TrackedBooster b in boosters)
                    if (!b.Finished && b.Vessel != null) b.ExtendUnpack(settings);
                if (Time.unscaledTime >= nextScan)
                {
                    nextScan = Time.unscaledTime + 0.2f;
                    ScanVessels();
                }
                double now = Planetarium.GetUniversalTime();
                if (now < nextMeasure) return;
                nextMeasure = now + 0.1;
                foreach (TrackedBooster b in boosters.ToArray())
                {
                    if (b.Finished) continue;
                    if (b.Vessel == null || b.Vessel.state == Vessel.State.DEAD)
                    {
                        b.Restore();
                        b.Finished = true; b.Status = "Nicht mehr vorhanden (keine Bergung)";
                        SetJournalStatus(b, "Lost"); continue;
                    }
                    // KSP reports a real physical contact; the tracked booster adds the case where
                    // KSP built no ground collider at all and the exact terrain height was reached.
                    bool groundContact = b.Vessel.LandedOrSplashed || b.SyntheticContact;
                    if (groundContact)
                    {
                        // Capture impact evidence ONCE. Re-reading the airborne sample after it
                        // gets stale must not turn a crash into an unconfirmed/successful contact.
                        if (double.IsNaN(b.LastContactTime))
                        {
                            b.TouchdownOutcome = TouchdownPolicy.Evaluate(b.Sample, b.Decision, now, settings.Limits,
                                b.ImpactFailed);
                            b.ContactParts.Clear();
                            foreach (uint id in b.KnownParts) b.ContactParts.Add(id);
                            // Measurement only: the height comparison at the exact moment of contact,
                            // which is where a coarse distant collider would show up.
                            Debug.Log("[PhysStageRecovery] " + TerrainProbe.Report(b.Vessel, b.GroundDepth, b.RejectedCollider));
                        }
                        bool livePhysics = b.Vessel.loaded && !b.Vessel.packed && !b.Vessel.HoldPhysics;
                        bool intact = !livePhysics || b.ContactParts.SetEquals(b.Vessel.parts.Select(p => p.flightID));
                        if (!intact || b.ImpactFailed) b.TouchdownOutcome = TouchdownOutcome.Crashed;
                        if (b.TouchdownOutcome == TouchdownOutcome.Crashed)
                        { FinishContact(b, b.TouchdownOutcome); continue; }
                        // Real touchdown: the booster is down and the contact was not a crash, so
                        // it is recovered right away. No resting time is required. The guidance
                        // must stop commanding thrust as soon as it is down, or it would lift off.
                        b.Landing.Stop(b.SyntheticContact ? "Bodenhoehe erreicht" : "aufgesetzt", false);
                        b.Status = b.SyntheticContact ? "Aufgesetzt (Bodenhoehe, kein Collider)" : "Aufgesetzt";
                        if (autoRecovery) Recover(b, !b.SyntheticContact); else FinishContact(b, TouchdownOutcome.Safe);
                        continue;
                    }
                    // Back in the air: forget the cached contact evidence.
                    b.LastContactTime = double.NaN; b.ContactParts.Clear();
                    b.TouchdownOutcome = TouchdownOutcome.Unconfirmed;
                    if (b.Vessel.GetCrewCount() != 0 || !b.Vessel.mainBody.isHomeWorld)
                    {
                        b.Finished = true; b.Status = "Verfolgung beendet: Besatzung oder andere Welt";
                        b.Restore(); SetJournalStatus(b, "Excluded"); continue;
                    }
                    b.Measure(settings, autoRecovery);
                    JournalEntry stagingEntry = null;
                    if (RecoveryJournal.Instance != null)
                        RecoveryJournal.Instance.Entries.TryGetValue(b.Id, out stagingEntry);
                    if (b.AutoStage(settings, stagingEntry)) continue;
                    
                }
            }
            catch (Exception e)
            {
                faulted = true;
                notice = "Mod wegen Fehler angehalten. Details: KSP.log [PhysStageRecovery].";
                Debug.LogError("[PhysStageRecovery] Simulation disabled after error: " + e);
                ReleaseWarp();
                foreach (TrackedBooster b in boosters) b.Restore();
            }
        }

        private void InitializeMission()
        {
            Vessel active = FlightGlobals.ActiveVessel;
            if (!active.loaded || active.packed || active.rootPart == null) return;
            originId = active.id;
            originRoot = active.rootPart.flightID;
            foreach (Part p in active.parts) if (p.flightID != 0) family.Add(p.flightID);
            foreach (Part p in active.parts)
            {
                if (p.FindModulesImplementing<ModuleParachute>().Count == 0) continue;
                for (Part ancestor = p.parent; ancestor != null; ancestor = ancestor.parent)
                    if (ancestor.FindModuleImplementing<ModuleDecouple>() != null
                        || ancestor.FindModuleImplementing<ModuleAnchoredDecoupler>() != null)
                    { attachedBoosterChutes.Add(p.flightID); break; }
            }
            initialized = true;
            notice = "Verfolge Abtrennungen von: " + active.vesselName;
            RecoveryJournal journal = RecoveryJournal.Instance;
            if (journal == null) return;
            Debug.Log("[PhysStageRecovery] Mission: " + active.vesselName + " parts=" + family.Count
                + " boosterChutes=" + attachedBoosterChutes.Count + " journalEntries=" + journal.Entries.Count);
            TerrainProbe.LogSetup(active.mainBody);
            foreach (JournalEntry entry in journal.Entries.Values)
                foreach (uint part in entry.FailedParts) failedParts.Add(part);
            foreach (JournalEntry e in journal.Entries.Values.ToArray())
            {
                if (e.Status != "Tracking") continue;
                Vessel v = FlightGlobals.FindVessel(e.Id);
                if (e.AnchorPart != 0)
                {
                    Part anchor = FlightGlobals.VesselsLoaded.SelectMany(candidate => candidate.parts)
                        .FirstOrDefault(p => p.flightID == e.AnchorPart);
                    if (anchor != null) v = anchor.vessel;
                }
                if (v != null && v.GetCrewCount() == 0 && v.mainBody.isHomeWorld)
                {
                    if (v.id != e.Id) MigrateJournal(e, v);
                    AddBooster(v);
                    if (v.loaded) foreach (Part p in v.parts) family.Add(p.flightID);
                }
            }
        }

        private void ScanVessels()
        {
            if (!initialized) return;
            RecoveryJournal journal = RecoveryJournal.Instance;
            foreach (Vessel v in FlightGlobals.VesselsLoaded.ToArray())
            {
                if (v == null || v.id == originId || v == FlightGlobals.ActiveVessel || boosters.Any(b => b.Id == v.id)) continue;
                string skip = null;
                if (v.packed) skip = "nicht entpackt (packed)";
                else if (v.LandedOrSplashed) skip = "gilt als gelandet oder gewassert";
                else if (v.situation == Vessel.Situations.PRELAUNCH) skip = "noch vor dem Start";
                else if (v.GetCrewCount() != 0) skip = "bemannt";
                else if (!v.mainBody.isHomeWorld) skip = "nicht auf der Heimatwelt";
                else if (v.parts.Any(p => p.flightID == originRoot)) skip = "enthaelt das Wurzelbauteil der Rakete";
                else if (!v.parts.Any(p => family.Contains(p.flightID))) skip = "gehoert nicht zu dieser Rakete";
                else if (v.parts.Any(p => failedParts.Contains(p.flightID))) skip = "bereits als Absturz vermerkt";
                else
                {
                    JournalEntry old;
                    if (journal != null && journal.Entries.TryGetValue(v.id, out old) && old.Status != "Tracking")
                        skip = "Bergungsjournal: " + old.Status;
                    else
                    {
                        bool chutes = v.parts.Any(p => p.FindModulesImplementing<ModuleParachute>().Count > 0);
                        bool engine = settings.PoweredLanding && v.parts.Any(p => p.FindModulesImplementing<ModuleEngines>()
                            .Any(PoweredLanding.Suitable));
                        if (!chutes && !engine)
                            skip = settings.PoweredLanding
                                ? "weder Fallschirme noch ein geeignetes Triebwerk"
                                : "keine Fallschirme, und Triebwerkslandung ist ausgeschaltet";
                    }
                }
                if (skip != null) { ReportSkip(v, skip); continue; }
                if (boosters.Count(b => !b.Finished) >= settings.MaxBoosters)
                {
                    notice = "Booster-Limit erreicht: " + settings.MaxBoosters + ". Weitere Stufen bleiben unveraendert.";
                    break;
                }
                AddBooster(v, true);
            }
        }

        // One log line per rejected vessel and flight, plus a short hint in the window.
        private void ReportSkip(Vessel v, string reason)
        {
            skipNotice = "Nicht erfasst: " + v.vesselName + " - " + reason + ".";
            if (!reportedSkips.Add(v.id)) return;
            Debug.Log("[PhysStageRecovery] Skip vessel=" + v.vesselName + " type=" + v.vesselType
                + " situation=" + v.situation + " packed=" + v.packed + " landed=" + v.LandedOrSplashed
                + " crew=" + v.GetCrewCount() + " homeBody=" + v.mainBody.isHomeWorld
                + " distance=" + (FlightGlobals.ActiveVessel == null ? 0
                    : Vector3d.Distance(v.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D()))
                + " reason=" + reason);
        }

        private void AddBooster(Vessel v, bool newSeparation = false)
        {
            if (boosters.Any(b => b.Id == v.id)) return;
            TrackedBooster b = new TrackedBooster(v, settings);
            boosters.Add(b);
            foreach (Part p in v.parts) family.Add(p.flightID);
            selection = boosters.Count - 1;
            cameraFeed.ClearFrame();
            bool usableChute = v.parts.Any(p => p.FindModulesImplementing<ModuleParachute>()
                .Any(c => c.isEnabled && c.deploymentState != ModuleParachute.deploymentStates.CUT));
            bool fueledEngine = settings.PoweredLanding && v.parts.Any(p => p.FindModulesImplementing<ModuleEngines>()
                .Any(e => PoweredLanding.Suitable(e) && PoweredLanding.HasPropellant(e)));
            if (windowOpenPolicy.Observe(v.id, newSeparation, usableChute || fueledEngine, settings.AutoOpenWindow, enabledMod))
            { settingsOpen = false; SetVisible(true); }
            Debug.Log("[PhysStageRecovery] Tracking " + b.Id + " " + b.Name);
            RecoveryJournal journal = RecoveryJournal.Instance;
            if (journal == null) return;
            if (!journal.Entries.ContainsKey(v.id))
                journal.Entries[v.id] = new JournalEntry { Id = v.id, Name = v.vesselName,
                    Status = "Tracking", Time = Planetarium.GetUniversalTime() };
            JournalEntry entry = journal.Entries[v.id];
            if (entry.StageCursor < 0) entry.StageCursor = Math.Max(0, v.currentStage);
            if (entry.AnchorPart != 0)
                b.Anchor = v.parts.FirstOrDefault(p => p.flightID == entry.AnchorPart) ?? b.Anchor;
            entry.AnchorPart = b.Anchor != null ? b.Anchor.flightID : 0;
        }

        private void MigrateJournal(JournalEntry entry, Vessel next)
        {
            RecoveryJournal journal = RecoveryJournal.Instance;
            if (journal == null) return;
            journal.Entries[next.id] = new JournalEntry { Id = next.id, Name = next.vesselName,
                Status = "Tracking", Time = Planetarium.GetUniversalTime(), StageCursor = entry.StageCursor,
                AnchorPart = entry.AnchorPart };
            entry.Status = "Separated";
        }

        private void FollowStageSeparations()
        {
            foreach (TrackedBooster b in boosters.ToArray())
            {
                if (b.Finished || b.Anchor == null || b.Anchor.vessel == null || b.Anchor.vessel == b.Vessel) continue;
                Vessel next = b.Anchor.vessel;
                b.Restore(); b.Finished = true; b.Status = "Nach Stufentrennung weiterverfolgt";
                if (next == FlightGlobals.ActiveVessel || next.GetCrewCount() != 0 || !next.mainBody.isHomeWorld) continue;
                JournalEntry entry;
                RecoveryJournal journal = RecoveryJournal.Instance;
                if (journal != null && journal.Entries.TryGetValue(b.Id, out entry)) MigrateJournal(entry, next);
                AddBooster(next, true);
            }
        }

        private void OnCrash(EventReport report)
        {
            if (report == null || report.origin == null || RecoveryJournal.Instance == null) return;
            foreach (TrackedBooster b in boosters.ToArray())
            {
                JournalEntry entry;
                bool stillOnGround = RecoveryJournal.Instance.Entries.TryGetValue(b.Id, out entry) && entry.Status == "Touchdown";
                Vessel owner = b.Anchor != null && b.Anchor.vessel != null ? b.Anchor.vessel : b.Vessel;
                if ((!b.Finished || stillOnGround) && (report.origin.vessel == owner || b.KnownParts.Contains(report.origin.flightID)))
                    FinishContact(b, TouchdownOutcome.Crashed);
            }
        }

        private void FinishContact(TrackedBooster b, TouchdownOutcome outcome)
        {
            b.Finished = true;
            b.ImpactFailed = outcome != TouchdownOutcome.Safe;
            string journalStatus = outcome == TouchdownOutcome.Crashed ? "Crashed"
                : outcome == TouchdownOutcome.Safe ? "Touchdown" : "ContactUnconfirmed";
            b.Status = outcome == TouchdownOutcome.Crashed ? "Absturz / harter Aufprall - keine Bergung"
                : outcome == TouchdownOutcome.Safe ? "Sicher aufgesetzt - nicht automatisch geborgen"
                : "Bodenkontakt - sichere Landung nicht bestaetigt";
            if (b.ImpactFailed)
            {
                foreach (uint part in b.KnownParts) failedParts.Add(part);
                if (b.Vessel != null) foreach (Part part in b.Vessel.parts) failedParts.Add(part.flightID);
                JournalEntry entry;
                if (RecoveryJournal.Instance.Entries.TryGetValue(b.Id, out entry))
                {
                    foreach (uint part in b.KnownParts) entry.FailedParts.Add(part);
                    if (b.Vessel != null) foreach (Part part in b.Vessel.parts) entry.FailedParts.Add(part.flightID);
                }
            }
            Debug.Log("[PhysStageRecovery] Contact outcome=" + journalStatus + " vessel=" + b.Id
                + " bodenkontakt=" + (b.SyntheticContact ? "hoehe" : "collider")
                + " preImpactSink=" + b.Sample.Sink + " horizontal=" + b.Sample.Horizontal
                + " angular=" + b.Sample.Angular + " stable=" + (b.Decision == null ? 0 : b.Decision.StableSeconds));
            // A contact the mod detected from the terrain height is invisible to KSP: there is no
            // collider out here, so KSP still believes the booster is flying and deletes it as soon
            // as the released vessel goes on rails. Hand the landing over before releasing it, so a
            // booster the player chose not to recover is still there to be recovered by hand.
            if (outcome != TouchdownOutcome.Crashed && b.SyntheticContact && b.Vessel != null)
                LandingSystems.HandOverLanded(b.Vessel, b.Vessel.mainBody.ocean && b.SurfaceAltitude <= 0);
            b.Restore(); b.StageStatus = ""; b.Landing.Status = "";
            SetJournalStatus(b, journalStatus);
        }

        // Every veto is logged once per canopy, so a flight log always names the blocker.
        private readonly HashSet<uint> reportedBlocks = new HashSet<uint>();

        private bool Blocked(ModuleParachute chute, bool block, Vessel v, string why)
        {
            if (!block) { reportedBlocks.Remove(chute.part.flightID); return false; }
            if (reportedBlocks.Add(chute.part.flightID))
                Debug.Log("[PhysStageRecovery] Chute blocked: part=" + chute.part.flightID + " " + why
                    + " vessel=" + v.id + " vertical=" + v.verticalSpeed.ToString("0.0")
                    + " safe=" + chute.deploymentSafeState + " state=" + chute.deploymentState
                    + " altitude=" + v.altitude.ToString("0"));
            return true;
        }

        public bool BlockParachuteOpening(ModuleParachute chute)
        {
            if (!initialized || !enabledMod || faulted || !settings.AutoArm || chute == null
                || chute.part == null || chute.vessel == null) return false;
            Vessel v = chute.vessel;
            if (!v.mainBody.isHomeWorld || v.LandedOrSplashed) return false;
            // Protect attached booster branches even if their chutes are staged before the
            // separator within the same staging operation. Main-vehicle chutes stay stock.
            if (v.id == originId || v.parts.Any(p => p.flightID == originRoot))
                return Blocked(chute, attachedBoosterChutes.Contains(chute.part.flightID), v, "Stufe der Rakete");
            if (v == FlightGlobals.ActiveVessel || v.GetCrewCount() != 0) return false;
            TrackedBooster b = boosters.FirstOrDefault(item => item.Id == v.id);
            if (b != null && b.Finished) return false;
            if (b == null && !family.Contains(chute.part.flightID)) return false;
            // A newly separated, not-yet-scanned booster has no trusted descent history.
            if (b == null || !v.loaded || v.packed || v.HoldPhysics)
                return Blocked(chute, true, v, "Booster noch nicht erfasst");
            // Beyond this the guard only refuses a canopy that the staging system fired while the
            // booster is still climbing. Everything else is left to KSP: its own opening height, its
            // minimum air pressure and its speed check are the real protection, and MechJeb never
            // blocks a canopy either. Vetoing on the safety state here was what silently kept
            // canopies shut in descents that KSP would have opened.
            return Blocked(chute, v.verticalSpeed >= 0, v, "Steigflug");
        }

        private void Recover(TrackedBooster b, bool kspKnowsContact)
        {
            Vessel v = b.Vessel;
            RecoveryJournal journal = RecoveryJournal.Instance;
            if (v == null || v == FlightGlobals.ActiveVessel || !v.loaded || v.packed || v.GetCrewCount() != 0
                || !v.mainBody.isHomeWorld || journal == null || !autoRecovery || b.ImpactFailed
                || b.Finished) return;
            // Only the established collider/terrain contact can authorize recovery.
            if (!(v.LandedOrSplashed || b.SyntheticContact) || (!b.ContactParts.SetEquals(v.parts.Select(p => p.flightID))
                || !TouchdownPolicy.RecoveredOnContact(true, b.TouchdownOutcome))) return;
            JournalEntry entry;
            if (!journal.Entries.TryGetValue(b.Id, out entry) || entry.Status != "Tracking") return;
            // Set a persistent at-most-once guard BEFORE firing any external recovery handlers.
            // If another mod throws after crediting funds, we must never retry automatically.
            entry.Status = "Recovering";
            entry.Time = Planetarium.GetUniversalTime();
            b.Finished = true;
            b.Restore();
            double fundsBefore = Funding.Instance != null ? Funding.Instance.Funds : 0;
            try
            {
                ProtoVessel snapshot = v.BackupVessel();
                if (kspKnowsContact)
                {
                    // The booster really rests on the ground, so keep its own contact state.
                    snapshot.situation = v.situation;
                }
                else
                {
                    // KSP built no ground
                    // collider here: the established terrain-contact fallback marks the snapshot landed.
                    snapshot.splashed = v.mainBody.ocean && b.SurfaceAltitude <= 0;
                    snapshot.landed = !snapshot.splashed;
                    snapshot.situation = snapshot.splashed ? Vessel.Situations.SPLASHED : Vessel.Situations.LANDED;
                }
                snapshot.landedAt = "";
                snapshot.displaylandedAt = "";
                ShipConstruction.RecoverVesselFromFlight(snapshot, HighLogic.CurrentGame.flightState, true);
                // Remove stale snapshots of the same vessel, if the flight state's snapshot predates BackupVessel.
                HighLogic.CurrentGame.flightState.protoVessels.RemoveAll(p => p.vesselID == b.Id);
                entry.Status = "Recovered";
                entry.Funds = Funding.Instance != null ? Funding.Instance.Funds - fundsBefore : 0;
                b.Status = "Aufgesetzt und geborgen"
                    + (entry.Funds > 0 ? " | +" + entry.Funds.ToString("N0") + " Funds" : "");
                ScreenMessages.PostScreenMessage("PhysStageRecovery: " + b.Name + " geborgen", 5, ScreenMessageStyle.UPPER_CENTER);
                Debug.Log("[PhysStageRecovery] Recovered " + b.Id + " groundContact=true"
                    + " clearance=" + b.Sample.Clearance
                    + " sink=" + b.Sample.Sink + " horizontal=" + b.Sample.Horizontal + " funds=" + entry.Funds);
            }
            catch (Exception e)
            {
                entry.Status = "RecoveryError";
                b.Status = "Bergung unklar; kein automatischer Wiederholungsversuch";
                Debug.LogError("[PhysStageRecovery] Recovery failed or partially completed for " + b.Id + ": " + e);
            }
        }

        private void SetJournalStatus(TrackedBooster b, string status)
        {
            JournalEntry e;
            if (RecoveryJournal.Instance != null && RecoveryJournal.Instance.Entries.TryGetValue(b.Id, out e)) e.Status = status;
        }

        public void LateUpdate()
        {
            if (!enabledMod || faulted || !visible || !uiVisible || !cameraEnabled || cameraFeed.Error != null || FlightDriver.Pause
                || boosters.Count == 0 || settingsOpen) return;
            TrackedBooster b = boosters[Mathf.Clamp(selection, 0, boosters.Count - 1)];
            if (!b.Finished) cameraFeed.Render(b.Vessel, settings.CameraFps);
        }

        public void OnGUI()
        {
            if (!visible || !uiVisible || settings == null || FlightDriver.Pause) return;
            GUISkin previous = GUI.skin;
            try
            {
                GUI.skin = HighLogic.Skin;
                EnsureWindowTheme();
                ResizeWindow();
                Rect before = window;
                window.width = Mathf.Clamp(window.width, Mathf.Min(480, Screen.width), Screen.width);
                window.height = Mathf.Clamp(window.height, Mathf.Min(540, Screen.height), Screen.height);
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width - window.width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height - window.height));
                window = GUI.Window(GetInstanceID(), window, DrawWindow, "", windowTheme.Window);
                if (window != before) geometryDirty = true;
                bool editing = settingsOpen && GUI.GetNameOfFocusedControl().StartsWith("BW", StringComparison.Ordinal);
                if (resizing || editing || window.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                    InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.STAGING | ControlTypes.TWEAKABLES
                        | (editing ? ControlTypes.ALL_SHIP_CONTROLS : ControlTypes.None), HoverLock);
                else InputLockManager.RemoveControlLock(HoverLock);
            }
            finally { GUI.skin = previous; }
        }

        private static string Number(double value, string suffix) { return RecoveryPolicy.Finite(value) ? value.ToString("0.0") + suffix : "--"; }
        private void Select(int delta)
        {
            selection = (selection + delta + boosters.Count) % boosters.Count;
            cameraFeed.ClearFrame();
        }

        private void ToggleMod(bool value)
        {
            enabledMod = value;
            settings.SetBehavior(enabledMod, autoRecovery); settings.Save();
            if (RecoveryJournal.Instance != null) RecoveryJournal.Instance.Enabled = value;
            if (!value)
            {
                foreach (TrackedBooster b in boosters) b.Restore();
                ReleaseWarp();
                cameraFeed.Dispose();
                notice = "Deaktiviert: Standard-Physikreichweite gilt wieder.";
            }
            else
            {
                // Rebuild ranges in two physics ticks, just as on first detection.
                for (int i = 0; i < boosters.Count; ++i)
                {
                    TrackedBooster b = boosters[i];
                    if (!b.Finished && b.Vessel != null) boosters[i] = new TrackedBooster(b.Vessel, settings);
                }
            }
        }

        private void LogDiagnostics()
        {
            Debug.Log("[PhysStageRecovery] DIAGNOSTIC enabled=" + enabledMod + " fault=" + faulted + " origin=" + originId
                + " range=" + settings.PhysicsRange + " camera=" + cameraFeed.Error);
            foreach (TrackedBooster b in boosters)
                Debug.Log("[PhysStageRecovery] " + b.Id + " status=" + b.Status + " physics=" + b.Sample.PhysicsActive
                    + " distance=" + b.Distance + " clearance=" + b.Sample.Clearance + " sink=" + b.Sample.Sink
                    + " chutes=" + b.OpenChutes + "/" + b.TotalChutes);
        }

        private void ReleaseWarp()
        {
            if (warpLocked) InputLockManager.RemoveControlLock(WarpLock);
            warpLocked = false;
        }

        public void OnDestroy()
        {
            SaveGeometry();
            if (windowTheme != null) windowTheme.Dispose();
            RailWarpGuard.Remove();
            ParachuteGuard.Remove();
            GameEvents.onHideUI.Remove(HideUI); GameEvents.onShowUI.Remove(ShowUI);
            GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbar);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveToolbar);
            GameEvents.onCrash.Remove(OnCrash); GameEvents.onCrashSplashdown.Remove(OnCrash);
            RemoveToolbar();
            foreach (TrackedBooster b in boosters) b.Restore();
            cameraFeed.Dispose();
            InputLockManager.RemoveControlLock(HoverLock);
            ReleaseWarp();
        }
    }
}
