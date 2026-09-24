using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BoosterWatch
{
    // The landing reserve of one engine: how much of the fuel in the tanks attached to it has to stay
    // for the landing. It is set on the engine's own right-click menu, in the editor and in flight,
    // because that is where the engine that lands is chosen.
    //
    // Flight behaviour, and why it is built this way:
    //
    //  * The reserve covers the engine's OWN tanks: its branch of the part tree, up to the first
    //    decoupler, docking port or part that does not pass fuel on. Drop tanks behind a decoupler or
    //    tanks that are only reached through a fuel line are not part of it.
    //  * When those tanks reach the reserve, the engine is shut down, marked as flamed out and marked
    //    as shut down. A flameout alone would not hold: KSP clears that flag every tick while
    //    propellant is available (ModuleEngines.RequestPropellant -> UnFlameout). A shut down engine
    //    consumes nothing, and KSP leaves its flags alone while EngineIgnited is false.
    //  * MechJeb (2.15, MechJebModuleStagingController) asks exactly `!flameout && !engineShutdown`
    //    to decide whether a stage still has fuel, and fires the next stage once no engine of the
    //    running stage has any. That is the "separate here" the setting asks for. The mod itself
    //    never stages the active rocket.
    //  * Engines on the same tanks are shut down with it: a neighbour would otherwise drink the
    //    reserve, and MechJeb would still see fuel in the running stage.
    //  * The reserve is released - once, for the rest of the flight - when the part is separated into
    //    another vessel, when the vessel lands, or by hand. From then on the landing autopilot may
    //    burn it.
    [KSPModule("Lande-Vorhalt")]
    public sealed class ModuleFuelReserve : PartModule
    {
        public const string ModuleName = "ModuleFuelReserve";
        // The menu text and the tank group are rebuilt at this rate. The trigger itself is checked
        // every physics tick, which only adds up numbers that are already in the tank objects.
        private const float RefreshSeconds = 0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Lande-Vorhalt (%)"),
         UI_FloatRange(minValue = 0f, maxValue = (float)FuelReserve.MaxPercent, stepIncrement = 1f)]
        public float reservePercent;

        // A saved flag: after a separation the booster keeps its reserve for the landing, and a game
        // saved during that descent must not lock the landing engine again when it is loaded. In the
        // editor it is always false, and a vessel that has not left the ground since the save was
        // loaded starts a new flight with the reserve armed.
        [KSPField(isPersistant = true)]
        public bool reserveReleased;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Vorhalt")]
        public string reserveInfo = "";

        [KSPField(guiActive = true, guiName = "Vorhalt-Status")]
        public string reserveStatus = "";

        [KSPEvent(guiActive = true, guiName = "Vorhalt freigeben")]
        public void ReleaseReserve()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            Release("#PSR_Reserve_ReasonManual");
        }

        private readonly List<Part> ownTanks = new List<Part>();
        private readonly List<Part> selfTank = new List<Part>();
        private readonly List<List<Part>> stockTanks = new List<List<Part>>();
        private readonly List<int> stockIds = new List<int>();
        private readonly List<string> stockNames = new List<string>();
        private readonly List<double> stockDensities = new List<double>();
        private readonly List<FuelStock> stocks = new List<FuelStock>();
        private readonly List<ModuleEngines> held = new List<ModuleEngines>();
        private readonly HashSet<int> propellantIds = new HashSet<int>();
        private readonly HashSet<Part> seen = new HashSet<Part>();
        private readonly Queue<Part> open = new Queue<Part>();
        private Vessel startVessel;
        private string engineName = "", releaseReason = "", stockMarkers = "";
        private double dryMass, isp, lockTime = double.NegativeInfinity, nextRelightNote = double.NegativeInfinity;
        private int tankCount, unstoppable, relights;
        // Taken once from the language files: this string is written into the engine's own menu on
        // every physics tick while the reserve holds, and the lookup has no business there.
        private string reachedStatus = "";
        private bool setup, shuttable, locked, started, flew, faulted, textDirty = true, padWarned;
        private float nextRefresh = float.NegativeInfinity;

        // Read by the reserve display: what this engine's reserve is and whether it still holds.
        public string EngineTitle { get { return engineName; } }
        public double OwnTankShare { get { return FuelReserve.RemainingShare(stocks); } }
        public string Markers { get { return stockMarkers; } }

        private void AddMarker(string marker)
        {
            if (string.IsNullOrEmpty(marker)) return;
            marker = marker.Trim();
            if (marker.Length < 2) return;
            if (stockMarkers.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return;
            stockMarkers += (stockMarkers.Length > 0 ? "|" : "") + marker;
        }

        // A module put on a part at runtime gets no OnStart, so everything that has to happen once is
        // done from the update hooks instead - and from OnAwake, which KSP calls while the module is
        // being added.
        public override void OnAwake()
        {
            EnsureSetup();
        }

        private void EnsureSetup()
        {
            // Attaching a module runs Awake before the part is necessarily known, so a setup attempt
            // without a part is not counted as done.
            if (setup || part == null) return;
            setup = true;
            BaseField percent = Fields["reservePercent"];
            if (percent != null && percent.uiControlEditor != null && percent.uiControlEditor.onFieldChanged == null)
                percent.uiControlEditor.onFieldChanged = OnPercentChanged;
            if (percent != null && percent.uiControlFlight != null && percent.uiControlFlight.onFieldChanged == null)
                percent.uiControlFlight.onFieldChanged = OnPercentChanged;
            // The menu follows the player's language. The wording in the attributes is the German one
            // and only ever shows if this never runs - KSP takes a module's field names from the
            // attributes, which are compile-time constants and cannot carry a language lookup. The
            // module's own heading is a private field that KSP fills from KSPModule and never
            // localizes, so it is the one text that needs the field written directly.
            RenameModule(Loc.Get("#PSR_Reserve_Module"));
            reachedStatus = Loc.Get("#PSR_Reserve_Reached");
            Rename("reservePercent", Loc.Get("#PSR_Reserve_Percent"));
            Rename("reserveInfo", Loc.Get("#PSR_Reserve_Info"));
            Rename("reserveStatus", Loc.Get("#PSR_Reserve_Status"));
            if (Events["ReleaseReserve"] != null) Events["ReleaseReserve"].guiName = Loc.Get("#PSR_Reserve_Release");
            if (HighLogic.LoadedSceneIsEditor) reserveReleased = false;
            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onPartDeCoupleNewVesselComplete.Add(OnPartDeCouple);
                GameEvents.onVesselWasModified.Add(OnVesselModified);
            }
            RefreshGroup();
            ReportSetup();
        }

        // One line per engine and flight, so a log shows which tanks the reserve was measured over.
        private void ReportSetup()
        {
            if (!HighLogic.LoadedSceneIsFlight || reservePercent <= 0 || part.vessel == null) return;
            Debug.Log("[PhysStageRecovery] Vorhalt " + part.vessel.id + " part=" + part.flightID + " " + engineName
                + " " + FuelReserve.PercentText(reservePercent) + " eigene Tanks=" + tankCount
                + " (" + FuelReserve.TankText(stocks) + ") Vorhalt=" + FuelReserve.ReserveText(stocks, reservePercent)
                + " = " + FuelReserve.MassText(FuelReserve.ReserveMass(stocks, reservePercent))
                + " (ca. " + FuelReserve.SpeedText(FuelReserve.IdealDeltaV(dryMass,
                    FuelReserve.ReserveMass(stocks, reservePercent), isp)) + ")"
                + (shuttable ? "" : " - Triebwerk nicht abschaltbar"));
        }

        private void OnPercentChanged(BaseField field, object value)
        {
            RefreshGroup();
            textDirty = true;
        }

        private void Rename(string field, string text)
        {
            BaseField target = Fields[field];
            if (target != null) target.guiName = text;
        }

        private static FieldInfo moduleGuiName;
        private void RenameModule(string text)
        {
            if (moduleGuiName == null)
                moduleGuiName = typeof(PartModule).GetField("guiName",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (moduleGuiName != null) moduleGuiName.SetValue(this, text);
        }

        public override void OnStart(StartState state)
        {
            EnsureSetup();
        }

        // Editor and flight: keep the menu text and the tank group current. The walk over the branch
        // happens twice a second, never per frame and never per physics tick.
        private void Update()
        {
            if (faulted) return;
            try
            {
                if (!setup) EnsureSetup();
                bool periodic = Time.unscaledTime >= nextRefresh;
                if (!periodic && !textDirty) return;
                if (periodic)
                {
                    nextRefresh = Time.unscaledTime + RefreshSeconds;
                    RefreshGroup();
                }
                textDirty = false;
                UpdateTexts();
            }
            catch (Exception e) { Fault(e); }
        }

        // Flight: the trigger and the hold. One pass per physics tick over a handful of resources.
        public override void OnFixedUpdate()
        {
            if (faulted || !HighLogic.LoadedSceneIsFlight || part == null || part.vessel == null) return;
            try { Step(); }
            catch (Exception e) { Fault(e); }
        }

        private void Fault(Exception e)
        {
            faulted = true;
            reserveStatus = Loc.Get("#PSR_Reserve_Disturbed");
            Debug.LogError("[PhysStageRecovery] Lande-Vorhalt gestoert an " + (part == null ? "?" : part.name) + ": " + e);
        }

        private void Step()
        {
            Vessel vessel = part.vessel;
            // The vessel this part started the flight on: on the pad that is the whole rocket, and
            // when the booster is later separated into a ship of its own, the reserve has done its
            // job. Taking it from the first flight tick and not from the module's setup keeps a
            // loaded save honest - the first tick is whatever vessel the part is on then.
            if (startVessel == null) startVessel = vessel;
            bool onGround = vessel.LandedOrSplashed;
            if (!started)
            {
                started = true;
                // A craft that begins the flight on the ground (pad, runway) arms its reserve again,
                // so a recovered rocket can be flown a second time. A module that turns up on a vessel
                // already coming down - an old save, or a slider touched during a descent - is
                // released instead: that reserve was not set for this flight, and one that snapped
                // shut on a landing booster would kill the landing.
                if (onGround) reserveReleased = false;
                else if (vessel.verticalSpeed < -1) Release("#PSR_Reserve_ReasonDescending");
            }
            if (!onGround) flew = true;
            if (startVessel != vessel) Release("#PSR_Reserve_ReasonSeparation");
            else if (flew && onGround) Release("#PSR_Reserve_ReasonGround");

            ReadAmounts();
            bool canHold = shuttable && reservePercent > 0 && !reserveReleased && FuelReserve.HasTanks(stocks);
            if (canHold && FuelReserve.Reached(stocks, reservePercent)) Hold();
            else if (locked) { LetGo("Vorhalt nicht mehr erreicht"); textDirty = true; }

            // A booster that already sits below its reserve on the pad cannot lift: say so instead of
            // leaving the player with a rocket that does not move.
            if (locked && !padWarned && vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                padWarned = true;
                Debug.Log("[PhysStageRecovery] Vorhalt " + vessel.id + " part=" + part.flightID + " "
                    + engineName + ": Tankinhalt liegt schon vor dem Start auf oder unter dem Vorhalt ("
                    + FuelReserve.PercentText(reservePercent) + ") - die Stufe liefert keinen Schub.");
            }
        }

        // --- The tank group belonging to this engine ---------------------------------------------

        private void CollectOwnTanks()
        {
            ownTanks.Clear(); seen.Clear(); open.Clear();
            if (part == null) return;
            ownTanks.Add(part);
            seen.Add(part);
            open.Enqueue(part);
            while (open.Count > 0)
            {
                Part node = open.Dequeue();
                AddTank(node.parent);
                if (node.children == null) continue;
                for (int i = 0; i < node.children.Count; i++) AddTank(node.children[i]);
            }
            selfTank.Clear();
            selfTank.Add(part);
        }

        private void AddTank(Part candidate)
        {
            if (candidate == null || seen.Contains(candidate) || BlocksFuel(candidate)) return;
            seen.Add(candidate);
            ownTanks.Add(candidate);
            open.Enqueue(candidate);
        }

        // A part that does not pass fuel on ends the branch: the crossfeed switch, a decoupler and a
        // docking port all separate "the tanks this engine lives in" from everything beyond. A fuel
        // line is not a part tree connection at all, so a tank fed through one is never reached here.
        private static bool BlocksFuel(Part candidate)
        {
            if (!candidate.fuelCrossFeed) return true;
            if (candidate.FindModuleImplementing<ModuleDecouplerBase>() != null) return true;
            if (candidate.FindModuleImplementing<ModuleDockingNode>() != null) return true;
            return false;
        }

        // --- What the own tanks hold -------------------------------------------------------------

        private void RefreshGroup()
        {
            stockTanks.Clear(); stockIds.Clear(); stockNames.Clear(); stockDensities.Clear();
            stocks.Clear(); propellantIds.Clear(); stockMarkers = "";
            dryMass = 0; isp = 0; tankCount = 0; shuttable = false;
            if (part == null) return;
            List<ModuleEngines> engines = part.FindModulesImplementing<ModuleEngines>();
            if (engines.Count == 0) return;
            engineName = part.partInfo != null ? part.partInfo.title : part.name;
            CollectOwnTanks();
            for (int i = 0; i < engines.Count; i++)
            {
                ModuleEngines engine = engines[i];
                if (engine.allowShutdown) shuttable = true;
                for (int p = 0; p < engine.propellants.Count; p++)
                {
                    Propellant propellant = engine.propellants[p];
                    if (propellant.ratio <= 0) continue;
                    bool selfOnly = propellant.GetFlowMode() == ResourceFlowMode.NO_FLOW;
                    int index = stockIds.IndexOf(propellant.id);
                    if (index < 0)
                    {
                        PartResourceDefinition definition = PartResourceLibrary.Instance != null
                            ? PartResourceLibrary.Instance.GetDefinition(propellant.id) : null;
                        if (definition == null) continue;
                        // How the stock resource box labels this propellant: its abbreviation ("FT" for
                        // LiquidFuel), its display name and its technical name. The reserve hatch looks
                        // for the box with one of those on it.
                        AddMarker(definition.abbreviation);
                        AddMarker(definition.displayName);
                        AddMarker(definition.name);
                        propellantIds.Add(propellant.id);
                        stockIds.Add(propellant.id);
                        stockNames.Add(propellant.displayName);
                        stockDensities.Add(definition.density);
                        stockTanks.Add(selfOnly ? selfTank : ownTanks);
                    }
                    else if (!selfOnly && !ReferenceEquals(stockTanks[index], ownTanks))
                    {
                        // Another engine on this part pulls the same propellant from the whole
                        // branch, so it is not limited to this part's own tank.
                        stockTanks[index] = ownTanks;
                    }
                }
            }
            // Capacity, current amount and the tank count of every propellant.
            for (int i = 0; i < stockIds.Count; i++)
            {
                double amount = 0, capacity = 0;
                List<Part> tanks = stockTanks[i];
                for (int t = 0; t < tanks.Count; t++)
                {
                    PartResource resource = tanks[t].Resources.Get(stockIds[i]);
                    if (resource == null) continue;
                    amount += Math.Max(0, resource.amount);
                    capacity += Math.Max(0, resource.maxAmount);
                }
                stocks.Add(new FuelStock(stockNames[i], amount, capacity, stockDensities[i]));
            }
            tankCount = 0;
            for (int t = 0; t < ownTanks.Count; t++)
                if (HoldsPropellant(ownTanks[t])) tankCount++;
            MeasureDryMass(engines);
        }

        // Everything in the branch that is not one of the engine's propellants counts as dry mass,
        // landing gear and engine included: that is the mass the reserve has to brake.
        private void MeasureDryMass(List<ModuleEngines> engines)
        {
            double mass = 0, propellantMass = 0;
            for (int t = 0; t < ownTanks.Count; t++)
            {
                Part tank = ownTanks[t];
                mass += tank.mass;
                for (int r = 0; r < tank.Resources.Count; r++)
                {
                    PartResource resource = tank.Resources[r];
                    double resourceMass = Math.Max(0, resource.amount) * resource.info.density;
                    mass += resourceMass;
                    if (propellantIds.Contains(resource.info.id)) propellantMass += resourceMass;
                }
            }
            dryMass = Math.Max(0, mass - propellantMass);
            // Isp at the pressure the vessel is in right now, weighted by the engines' thrust.
            double weight = 0, ispSum = 0;
            float pressure = HighLogic.LoadedSceneIsFlight && part.vessel != null
                ? (float)(part.vessel.staticPressurekPa / 101.325) : 0f;
            for (int i = 0; i < engines.Count; i++)
            {
                double value = engines[i].atmosphereCurve.Evaluate(pressure);
                if (!(value > 0)) continue;
                double w = engines[i].maxThrust > 0 ? engines[i].maxThrust : 1;
                ispSum += value * w; weight += w;
            }
            isp = weight > 0 ? ispSum / weight : 0;
        }

        private bool HoldsPropellant(Part candidate)
        {
            for (int r = 0; r < candidate.Resources.Count; r++)
            {
                PartResource resource = candidate.Resources[r];
                if (resource.info != null && resource.maxAmount > 0 && propellantIds.Contains(resource.info.id))
                    return true;
            }
            return false;
        }

        // The amounts move with the burn, so they are read fresh every tick; capacity and the tank
        // count only change with the craft and are handled in RefreshGroup.
        private void ReadAmounts()
        {
            for (int i = 0; i < stocks.Count; i++)
            {
                double amount = 0;
                List<Part> tanks = stockTanks[i];
                for (int t = 0; t < tanks.Count; t++)
                {
                    PartResource resource = tanks[t].Resources.Get(stockIds[i]);
                    if (resource != null) amount += Math.Max(0, resource.amount);
                }
                stocks[i].Amount = amount;
            }
        }

        // --- Holding and releasing ---------------------------------------------------------------

        private void Hold()
        {
            if (!locked)
            {
                locked = true;
                textDirty = true;
                lockTime = Time.unscaledTime;
                relights = 0;
                CollectHeld();
                Debug.Log("[PhysStageRecovery] Vorhalt erreicht " + part.vessel.id + " part=" + part.flightID
                    + " " + engineName + " Grenze=" + FuelReserve.PercentText(reservePercent)
                    + " Rest=" + FuelReserve.ShareText(FuelReserve.RemainingShare(stocks))
                    + " eigene Tanks=" + tankCount + " (" + FuelReserve.TankText(stocks) + ")"
                    + " abgeschaltet=" + held.Count
                    + (unstoppable > 0 ? " davon " + unstoppable + " nicht abschaltbar" : "")
                    + " - Ausbrand gemeldet, Stufung uebernimmt KSP/MechJeb");
            }
            for (int i = 0; i < held.Count; i++)
            {
                ModuleEngines engine = held[i];
                if (engine == null || engine.part == null || engine.part.vessel == null) continue;
                if (engine.EngineIgnited)
                {
                    engine.Shutdown();
                    if (Time.unscaledTime - lockTime > 1)
                    {
                        relights++;
                        textDirty = true;
                        if (Time.unscaledTime >= nextRelightNote)
                        {
                            nextRelightNote = Time.unscaledTime + 1;
                            Debug.Log("[PhysStageRecovery] Vorhalt haelt part=" + engine.part.flightID
                                + " - Triebwerk wurde trotz Vorhalt gezuendet, erneut abgeschaltet");
                        }
                    }
                }
                // The menu line of the engine itself says why it is dark.
                if (!engine.flameout) engine.Flameout(reachedStatus, false, true);
                engine.engineShutdown = true;
            }
        }

        private void CollectHeld()
        {
            held.Clear();
            unstoppable = 0;
            Vessel vessel = part.vessel;
            if (vessel == null || vessel.parts == null) return;
            for (int p = 0; p < vessel.parts.Count; p++)
            {
                Part candidate = vessel.parts[p];
                if (candidate == null) continue;
                List<ModuleEngines> engines = candidate.FindModulesImplementing<ModuleEngines>();
                for (int i = 0; i < engines.Count; i++)
                {
                    ModuleEngines engine = engines[i];
                    if (!SharesPropellant(engine) || !SharesTanks(engine)) continue;
                    if (!engine.allowShutdown) { unstoppable++; continue; }
                    held.Add(engine);
                }
            }
        }

        // An engine that draws from the tanks this reserve covers is held back with it, whichever
        // part it sits on.
        private bool SharesTanks(ModuleEngines engine)
        {
            if (engine == null || engine.part == null) return false;
            if (engine.part == part) return true;
            List<Part> branch = BranchOf(engine.part);
            for (int i = 0; i < branch.Count; i++) if (ownTanks.Contains(branch[i])) return true;
            return false;
        }

        private bool SharesPropellant(ModuleEngines engine)
        {
            for (int p = 0; p < engine.propellants.Count; p++)
            {
                Propellant propellant = engine.propellants[p];
                if (propellant.ratio > 0 && propellantIds.Contains(propellant.id)) return true;
            }
            return false;
        }

        private static List<Part> BranchOf(Part enginePart)
        {
            List<Part> branch = new List<Part>();
            if (enginePart == null) return branch;
            HashSet<Part> visit = new HashSet<Part>();
            Queue<Part> pending = new Queue<Part>();
            branch.Add(enginePart); visit.Add(enginePart); pending.Enqueue(enginePart);
            while (pending.Count > 0)
            {
                Part node = pending.Dequeue();
                Push(node.parent, branch, visit, pending);
                if (node.children == null) continue;
                for (int i = 0; i < node.children.Count; i++) Push(node.children[i], branch, visit, pending);
            }
            return branch;
        }

        private static void Push(Part candidate, List<Part> branch, HashSet<Part> visit, Queue<Part> pending)
        {
            if (candidate == null || visit.Contains(candidate) || BlocksFuel(candidate)) return;
            visit.Add(candidate); branch.Add(candidate); pending.Enqueue(candidate);
        }

        private void LetGo(string reason)
        {
            if (!locked) return;
            locked = false;
            textDirty = true;
            for (int i = 0; i < held.Count; i++)
            {
                ModuleEngines engine = held[i];
                if (engine == null || engine.part == null) continue;
                // An engine that was never restartable must not become restartable by releasing the
                // reserve, so its shut down flag stays.
                if (engine.allowRestart) engine.engineShutdown = false;
                engine.UnFlameout(false);
            }
            held.Clear();
            Debug.Log("[PhysStageRecovery] Vorhalt aufgehoben "
                + (part.vessel == null ? "?" : part.vessel.id.ToString()) + " part=" + part.flightID
                + " " + engineName + " Grund=" + reason
                + " Rest=" + FuelReserve.ShareText(FuelReserve.RemainingShare(stocks)));
        }

        // reason is a language tag: the release shows up in the menu and in the log, and both read the
        // player's language. On a German installation both carry exactly the wording they always had.
        private void Release(string reason)
        {
            bool wasLocked = locked;
            releaseReason = Loc.Get(reason);
            LetGo(releaseReason);
            if (reserveReleased && !wasLocked) return;
            reserveReleased = true;
            textDirty = true;
            Debug.Log("[PhysStageRecovery] Vorhalt freigegeben part=" + part.flightID + " " + engineName
                + " Grund=" + releaseReason + " Rest=" + FuelReserve.ShareText(FuelReserve.RemainingShare(stocks))
                + " - der Treibstoff steht der Landung zur Verfuegung");
        }

        private void OnPartDeCouple(Vessel first, Vessel second)
        {
            if (faulted || !HighLogic.LoadedSceneIsFlight || part == null || part.vessel == null) return;
            if (startVessel != null && part.vessel != startVessel) Release("#PSR_Reserve_ReasonSeparation");
        }

        private void OnVesselModified(Vessel vessel)
        {
            if (faulted || !HighLogic.LoadedSceneIsFlight || part == null || part.vessel == null) return;
            if (startVessel != null && part.vessel != startVessel) Release("#PSR_Reserve_ReasonSeparation");
        }

        // --- Menu ---------------------------------------------------------------------------------

        private void UpdateTexts()
        {
            double percent = FuelReserve.ClampPercent(reservePercent);
            bool flight = HighLogic.LoadedSceneIsFlight;
            // The module sits on every engine part because KSP takes a part's modules from its config.
            // Where the engine cannot be shut down there is no reserve to hold, so the slider is kept
            // out of that part's menu.
            Fields["reservePercent"].guiActive = shuttable;
            Fields["reservePercent"].guiActiveEditor = shuttable;
            if (percent <= 0)
            {
                reserveInfo = "";
                Fields["reserveInfo"].guiActive = false;
                Fields["reserveInfo"].guiActiveEditor = false;
                reserveStatus = "";
                Fields["reserveStatus"].guiActive = false;
                Events["ReleaseReserve"].guiActive = false;
                return;
            }
            Fields["reserveInfo"].guiActive = true;
            Fields["reserveInfo"].guiActiveEditor = true;
            StringBuilder text = new StringBuilder();
            text.Append(FuelReserve.PercentText(percent)).Append(" = ");
            if (!FuelReserve.HasTanks(stocks)) text.Append(Loc.Get("#PSR_Reserve_NoTank"));
            else
            {
                double mass = FuelReserve.ReserveMass(stocks, percent);
                text.Append(Loc.Get("#PSR_Reserve_FromTanks", FuelReserve.MassText(mass), tankCount,
                    Loc.Get(tankCount == 1 ? "#PSR_Reserve_Tank" : "#PSR_Reserve_Tanks")));
                double deltaV = FuelReserve.IdealDeltaV(dryMass, mass, isp);
                if (deltaV > 0) text.Append(Loc.Get("#PSR_Reserve_Approx", FuelReserve.SpeedText(deltaV)));
            }
            if (!shuttable) text.Append(" - ").Append(Loc.Get("#PSR_Reserve_NoShutdown"));
            reserveInfo = text.ToString();

            if (!flight)
            {
                reserveStatus = "";
                Fields["reserveStatus"].guiActive = false;
                Events["ReleaseReserve"].guiActive = false;
                return;
            }
            Events["ReleaseReserve"].guiActive = !reserveReleased;
            string share = FuelReserve.ShareText(FuelReserve.RemainingShare(stocks));
            if (reserveReleased)
                reserveStatus = Loc.Get("#PSR_Reserve_Released", releaseReason, share);
            else if (!shuttable) reserveStatus = Loc.Get("#PSR_Reserve_NoShutdownNoReserve");
            else if (!FuelReserve.HasTanks(stocks)) reserveStatus = Loc.Get("#PSR_Reserve_NoTankNoReserve");
            else if (locked)
                reserveStatus = Loc.Get("#PSR_Reserve_Held", held.Count, share, FuelReserve.PercentText(percent))
                    + (relights > 0 ? Loc.Get("#PSR_Reserve_HeldRelight", relights) : "");
            else reserveStatus = Loc.Get("#PSR_Reserve_Active", share, FuelReserve.PercentText(percent));
            Fields["reserveStatus"].guiActive = true;
        }

        public void OnDestroy()
        {
            GameEvents.onPartDeCoupleNewVesselComplete.Remove(OnPartDeCouple);
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
        }
    }
}
