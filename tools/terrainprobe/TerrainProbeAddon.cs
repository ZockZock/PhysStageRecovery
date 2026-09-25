using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PSRTerrainProbe
{
    // Messsonde fuer die Frage, ob KSP sein Terrain um ein zweites Detail-Zentrum baut.
    //
    // Ausgangslage (im KSP.log gemessen): KSP setzt PQS.target auf das aktive Schiff und
    // PQS.secondaryTarget auf die Flugkamera. Beide sind oeffentliche Felder, und fuer beide
    // gibt es einen Setter. Wenn ein Punkt weit weg als zweites Zentrum akzeptiert wird und dort
    // Gelaende (Netz und Boden) entsteht, kann ein entfernter Booster seine eigene kleine Welt
    // bekommen, ohne dass wir sie selbst bauen muessen.
    //
    // Die Sonde tut nichts am Mod. Sie legt einen leeren Zielpunkt, eine eigene Kamera und eine
    // Logdatei an, alles unter GameData/PhysStageRecoveryProbe, und laesst sich mit einem
    // einzigen Befehl wieder entfernen.
    //
    // Tasten:  F7  Test starten / Zielpunkt neu setzen
    //          F6  primaeres Zentrum uebernehmen (Harmony haelt KSP davon ab, es zurueckzusetzen)
    //          Pos1 (Home)  PQS.RebuildSphere() ausloesen
    //          F8  alles zurueckstellen
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class TerrainProbeAddon : MonoBehaviour
    {
        // Abstand des Pruefpunkts vom aktiven Schiff, entlang der Oberflaeche.
        private const double ProbeDistance = 250000.0;
        // Der Zielpunkt liegt so viele Meter ueber der berechneten Oberflaeche.
        private const double ProbeHeight = 15.0;
        private const float LogInterval = 1f;
        private const float DetailInterval = 5f;
        private const int HistoryLines = 6;

        public KeyCode StartKey = KeyCode.F7;
        public KeyCode HijackKey = KeyCode.F6;
        public KeyCode RestoreKey = KeyCode.F8;
        public KeyCode RebuildKey = KeyCode.Home;

        private PQS pqs;
        private CelestialBody body;
        private Transform originalTarget, originalSecondary;
        private GameObject anchor;
        private Vector3d point, up;
        private Vector3 pointLocal;
        private bool pointStored;
        private double exactSurface;
        private double overwrites;
        private float nextLog, nextDetail, started;
        private bool active, hijackPrimary;
        private string status = "bereit - F7 startet den Test";
        private readonly List<string> history = new List<string>();
        private Camera cam;
        private RenderTexture rt;
        private string logPath;
        private static Type colliderType;
        private static bool colliderTypeSearched;
        private string lastDetailText = "";
        private string phase = "";
        private readonly HashSet<Collider> ownColliders = new HashSet<Collider>();
        private float nextOwnColliders;
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private int logged;
        private Harmony harmony;
        private bool suppressTarget;
        private GroundPatchTest patchTest;
        private static TerrainProbeAddon current;
        private float nextNearTry;
        private float nextCameraFrame;
        private bool restingReported;
        // Startet den Test von selbst, damit ein nicht angekommenes F7 keinen Flug kostet.
        public bool AutoStart = true;
        private float startTime;
        private bool autoStarted;

        // Solange wir das primaere Zentrum uebernommen haben, darf KSP es nicht jeden Frame
        // zuruecksetzen. Ohne diesen Eingriff laeuft KSPs eigene Zuweisung immer vor unserem
        // LateUpdate, und PQS sieht das Schiff statt den Pruefpunkt.
        private static bool SetTargetPrefix(PQS __instance)
        {
            TerrainProbeAddon probe = current;
            if (probe == null || !probe.suppressTarget) return true;
            return __instance != probe.pqs;
        }

        private void Start()
        {
            current = this;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                dir = Path.GetFullPath(Path.Combine(dir, "..", "PluginData"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "probe.log");
                Write("=== Terrain-Sonde gestartet, Unity " + Application.unityVersion + " ===");
            }
            catch (Exception e) { logPath = null; Debug.LogError("[PSRProbe] Logdatei: " + e.Message); }
            startTime = Time.unscaledTime;
            InstallHarmony();
            try
            {
                Vessel ship = FlightGlobals.ActiveVessel;
                Write("Szene=" + HighLogic.LoadedScene + " Fahrzeug=" + (ship == null ? "keins" : ship.vesselName)
                    + " Lage=" + (ship == null ? "?" : ship.situation.ToString())
                    + " Hoehe=" + (ship == null ? "?" : ship.altitude.ToString("0"))
                    + " Tempo=" + (ship == null ? "?" : ship.srfSpeed.ToString("0")));
            }
            catch { }
        }

        // KSP setzt PQS.target jeden Frame auf das aktive Schiff. Der Prefix laesst diese
        // Zuweisung ausfallen, solange wir das Zentrum uebernommen haben - nur fuer diese eine
        // PQS-Instanz, damit andere Welten unberuehrt bleiben.
        private void InstallHarmony()
        {
            try
            {
                MethodInfo target = typeof(PQS).GetMethod("SetTarget", new[] { typeof(Transform) });
                if (target == null) { status = "PQS.SetTarget nicht gefunden"; return; }
                harmony = new Harmony("psr.terrainprobe");
                MethodInfo prefix = typeof(TerrainProbeAddon).GetMethod("SetTargetPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(target, new HarmonyMethod(prefix), null, null);
                Write("Harmony-Prefix auf PQS.SetTarget gesetzt");
            }
            catch (Exception e)
            {
                status = "Harmony fehlgeschlagen: " + e.Message;
                Debug.LogError("[PSRProbe] Harmony: " + e);
            }
        }

        // In der Luft und nicht aufgesetzt - nur dort laeuft die Physik normal.
        private static bool ReadyToStart()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.LandedOrSplashed || v.altitude < 500.0) return false;
            return v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL
                || v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.ESCAPING;
        }

        private void Update()
        {            try
            {
                if (Input.GetKeyDown(StartKey)) Begin();
                // Autostart nur in der Luft: auf der Rampe haelt KSP das Schiff fest und faehrt
                // einen Sonderbetrieb, in dem unsere Testkugel nur mit einem Bruchteil der
                // Fallbeschleunigung faellt. Solche Messungen sind wertlos.
                if (AutoStart && !autoStarted && !active && Time.unscaledTime - startTime > 8f)
                {
                    if (ReadyToStart())
                    {
                        autoStarted = true;
                        Begin();
                        if (active) status = "automatisch gestartet - F7 baut neu, F8 raeumt auf";
                    }
                    else if (FlightGlobals.ActiveVessel != null)
                    {
                        status = "warte auf Flug (" + FlightGlobals.ActiveVessel.situation
                            + ", " + FlightGlobals.ActiveVessel.altitude.ToString("0") + " m)";
                    }
                }
                if (Input.GetKeyDown(RestoreKey)) Restore("Taste F8");
                if (Input.GetKeyDown(HijackKey)) ToggleHijack();
                if (Input.GetKeyDown(RebuildKey)) Rebuild();
                if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown))
                {
                    if (patchTest != null)
                    {
                        patchTest.CameraDistance = Math.Max(30.0, Math.Min(8000.0,
                            patchTest.CameraDistance * (Input.GetKeyDown(KeyCode.PageUp) ? 1.35 : 1.0 / 1.35)));
                        UpdateCamera(true);
                        status = "Kameraabstand " + patchTest.CameraDistance.ToString("0") + " m";
                    }
                }
                if (!active) return;
                UpdateCamera(false);

                // Land kam erst im Flug in Reichweite? Dann den nahen Patch nachbauen.
                if (patchTest != null && !patchTest.HasNear && Time.unscaledTime >= nextNearTry)
                {
                    nextNearTry = Time.unscaledTime + 10f;
                    Vessel ship = FlightGlobals.ActiveVessel;
                    if (ship != null && patchTest.TryBuildNear(ship.CoM))
                    {
                        Write("naher Patch nachgebaut: " + patchTest.NearSearch);
                        status = "naher Patch gefunden";
                        CreateCamera();
                    }
                }

                // Hat KSP das zweite Zentrum in diesem Frame selbst gesetzt?
                if (pqs != null && pqs.secondaryTarget != anchor.transform) overwrites += 1;

                if (Time.unscaledTime - nextOwnColliders > 0f) CollectOwnColliders();
                if (Time.unscaledTime >= nextLog) { nextLog = Time.unscaledTime + LogInterval; Measure(false); }
                if (Time.unscaledTime >= nextDetail)
                {
                    nextDetail = Time.unscaledTime + DetailInterval;
                    Measure(true);
                }
            }
            catch (Exception e)
            {
                status = "Fehler: " + e.Message;
                Debug.LogError("[PSRProbe] " + e);
                Restore("Fehler");
            }
        }

        // Kugel-Schwerkraft ausserhalb des Physikschritts waere zu spaet: KSP rechnet die
        // Gravitation nur fuer eigene Fahrzeuge, unsere Testkugel braucht sie von uns.
        private void FixedUpdate()
        {
            if (patchTest != null) patchTest.PhysicsTick(Time.fixedDeltaTime);
        }

        // Nach allen Update() der Szene: hier setzen wir die beiden Zentren.
        private void LateUpdate()
        {
            if (!active || pqs == null || anchor == null) return;
            try
            {
                if (pqs.secondaryTarget != anchor.transform) pqs.SetSecondaryTarget(anchor.transform);
                if (!hijackPrimary) return;
                // KSP hat die Zuweisung durch den Prefix nicht ausgefuehrt, also steht unser
                // Punkt noch. Fehlt er trotzdem, setzen wir ihn nach.
                if (pqs.target != anchor.transform) pqs.target = anchor.transform;
                WriteDerived();
            }
            catch (Exception e) { status = "Setzen fehlgeschlagen: " + e.Message; }
        }

        // Pos1: Testkugel neu fallen lassen (zweiter Beweis, falls der erste Lauf unklar war).
        private void Rebuild()
        {
            if (patchTest == null) { status = "erst F7"; return; }
            patchTest.ResetBall();
            status = "Kugel neu abgesetzt";
            Write("Kugel neu abgesetzt");
        }

        // Alles, was PQS sonst aus dem Ziel ableitet, auf den Pruefpunkt ziehen. Ohne diese
        // Werte bleibt die Detailstufe die des echten Schiffes.
        private void WriteDerived()
        {
            try
            {
                double altitude = exactSurface + ProbeHeight;
                pqs.targetAltitude = altitude;
                pqs.targetHeight = ProbeHeight;
                pqs.transformPosition = point;
                pqs.transformRotation = body.transform.rotation;
                Vector3d velocity = body.getRFrmVel(point);
                pqs.targetVelocity = velocity;
                pqs.targetSpeed = velocity.magnitude;
            }
            catch (Exception e) { status = "abgeleitete Werte: " + e.Message; }
        }

        private void ToggleHijack()
        {
            if (patchTest == null) { status = "erst F7"; return; }
            patchTest.CycleView();
            status = "Ansicht: " + patchTest.ViewName;
            UpdateCamera(true);
        }

        private void Begin()
        {
            if (active) { Restore("neu gesetzt"); }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.mainBody == null) { status = "kein aktives Schiff"; return; }
            body = v.mainBody;
            pqs = body.pqsController;
            if (pqs == null) { status = "diese Welt hat kein PQS"; Write("kein PQS fuer " + body.name); return; }

            originalTarget = pqs.target;
            originalSecondary = pqs.secondaryTarget;

            Vector3d radial = (v.CoM - body.position).normalized;
            Vector3d axis = body.transform.up.normalized;
            Vector3d side = Vector3d.Cross(axis, radial).normalized;
            // Land suchen: von weit nach nah, damit der Punkt sicher ausserhalb jeder schon
            // gebauten Kachel liegt und nicht im Ozean, wo es kein Gelaende zu finden gibt.
            double[] candidates = new double[] { 300, 250, 200, 150, 100, 50 };
            double radius = double.NaN;
            double chosen = ProbeDistance / 1000.0;
            for (int pass = 0; pass < 2 && double.IsNaN(radius); pass++)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    double sign = pass == 0 ? 1.0 : -1.0;
                    double angle = sign * candidates[i] * 1000.0 / body.Radius;
                    Vector3d direction = (radial * Math.Cos(angle) + side * Math.Sin(angle)).normalized;
                    double candidate = pqs.GetSurfaceHeight(direction);
                    if (double.IsNaN(candidate) || candidate <= 0) continue;
                    if (candidate - body.Radius > 50.0)
                    {
                        radius = candidate;
                        point = body.position + direction * (candidate + ProbeHeight);
                        chosen = candidates[i];
                        break;
                    }
                }
            }
            if (double.IsNaN(radius))
            {
                // Nur Ozean in Reichweite: dann bleibt es beim festen Abstand.
                double angle = ProbeDistance / body.Radius;
                Vector3d direction = (radial * Math.Cos(angle) + side * Math.Sin(angle)).normalized;
                radius = pqs.GetSurfaceHeight(direction);
                if (double.IsNaN(radius) || radius <= 0) { status = "Oberflaeche nicht lesbar"; return; }
                point = body.position + direction * (radius + ProbeHeight);
            }
            exactSurface = radius - body.Radius;
            up = (point - body.position).normalized;
            // Der Pruefpunkt wird PQS-lokal gemerkt: KSP verschiebt den Weltursprung laufend,
            // ein gespeicherter Weltvektor zeigt nach wenigen Sekunden ins Leere.
            pointLocal = pqs.transform.InverseTransformPoint((Vector3)point);
            pointStored = true;

            // Zustand VOR dem Umhaengen: ohne diesen Wert liesse sich ein Treffer nicht von
            // einem schon vorher gebauten Gelaende unterscheiden.
            started = Time.unscaledTime;
            CollectOwnColliders();
            phase = "vorher";
            Measure(true);

            // Stufe-2-Prototyp: eigener Boden am fernen Punkt (Kollisionsbeweis) und 3 km neben
            // dem Schiff (Formvergleich mit KSPs eigenem Gelaende).
            if (patchTest != null) patchTest.Dispose();
            patchTest = new GroundPatchTest();
            patchTest.Build(pqs, body, v.CoM, point, exactSurface);

            anchor = new GameObject("PSR_ProbeTarget");
            anchor.transform.position = (Vector3)point;
            pqs.SetSecondaryTarget(anchor.transform);
            CreateCamera();
            phase = "nachher";
            nextLog = Time.unscaledTime;
            nextDetail = Time.unscaledTime;
            overwrites = 0;
            logged = 0;
            active = true;
            status = "laeuft: Patch " + chosen.ToString("0") + " km entfernt, Kugel faellt";
            Write("--- Test: ferner Patch " + chosen.ToString("0") + " km entfernt, Hoehe "
                + exactSurface.ToString("0.0") + " m, Ziel war " + Fmt(originalTarget) + ", zweites war "
                + Fmt(originalSecondary) + " ---");
        }

        private void Restore(string reason)
        {
            suppressTarget = false;   // zuerst: sonst blockiert der Prefix das Zuruecksetzen
            try
            {
                if (pqs != null)
                {
                    if (pqs.secondaryTarget != originalSecondary)
                    {
                        if (originalSecondary != null) pqs.SetSecondaryTarget(originalSecondary);
                        else pqs.secondaryTarget = null;
                    }
                    if (pqs.target != originalTarget)
                    {
                        if (originalTarget != null) pqs.SetTarget(originalTarget);
                        else pqs.target = null;
                    }
                }
            }
            catch (Exception e) { Debug.LogError("[PSRProbe] Zurueckstellen: " + e.Message); }
            if (active) Write("--- zurueckgestellt (" + reason + "), ueberschrieben=" + overwrites.ToString("0") + " ---");
            active = false;
            hijackPrimary = false;
            if (anchor != null) { UnityEngine.Object.Destroy(anchor); anchor = null; }
            if (patchTest != null) { patchTest.Dispose(); patchTest = null; }
            DisposeCamera();
            if (!string.IsNullOrEmpty(reason)) status = "zurueckgestellt (" + reason + ")";
        }

        private void Measure(bool detail)
        {
            if (pqs == null) return;
            if (pointStored)
            {
                // Frisch umrechnen, damit alle Sonden am aktuellen Ort messen.
                point = pqs.transform.TransformPoint(pointLocal);
                up = (point - body.position).normalized;
            }

            // Netz: fragt PQS selbst, ob an dieser Stelle ein Quad gebaut ist.
            Vector3d from = point + up * 5000.0;
            bool meshHit = false;
            double meshDistance = 0;
            try { meshHit = pqs.RayIntersection((Vector3)from, (Vector3)(-up), out meshDistance); }
            catch (Exception e) { lastDetailText = "RayIntersection: " + e.Message; }

            // Boden: was eine Physiksonde unter dem Punkt findet.
            int count = Physics.RaycastNonAlloc((Vector3)from, (Vector3)(-up), hits, 20000f, ~0,
                QueryTriggerInteraction.Ignore);
            if (count > hits.Length) count = hits.Length;
            string nearest = "keins";
            double nearestDistance = double.PositiveInfinity;
            int foreign = 0;
            List<string> found = new List<string>();
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || ownColliders.Contains(c)) continue;
                foreign++;
                double distance = hits[i].distance;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = Describe(hits[i]); }
                if (found.Count < 3)
                    found.Add(Describe(hits[i]) + " abstand="
                        + (hits[i].point - (Vector3)point).magnitude.ToString("0.0"));
            }

            string colliders = ColliderObjects();
            string near = NearGround();
            string vessel = ActiveVesselGround();

            double meshAltitude = meshHit ? (from - body.position).magnitude - body.Radius - meshDistance : double.NaN;
            StringBuilder line = new StringBuilder();
            line.Append("t=").Append((Time.unscaledTime - started).ToString("0")).Append("s");
            line.Append(" ").Append(phase);
            line.Append(" primaer=").Append(hijackPrimary ? "uebernommen" : "schiff");
            line.Append(" ziel=").Append(Fmt(pqs.target));
            line.Append(" zweites=").Append(Fmt(pqs.secondaryTarget));
            line.Append(" ueberschrieben=").Append(overwrites.ToString("0"));
            line.Append(" netz=").Append(meshHit ? "ja" : "nein");
            if (meshHit)
            {
                line.Append(" netzHoehe=").Append(meshAltitude.ToString("0.0"));
                line.Append(" netzFehler=").Append((meshAltitude - exactSurface).ToString("0.0"));
            }
            line.Append(" exakt=").Append(exactSurface.ToString("0.0"));
            line.Append(" boden=").Append(foreign == 0 ? "keins" : nearest);
            if (foreign > 0) line.Append(" bodenAbstand=").Append(nearestDistance.ToString("0.0"));
            line.Append(" bodenNah=").Append(near);
            line.Append(" pqsCollider=").Append(colliders);
            line.Append(" schiff=").Append(vessel);
            line.Append(" detailRad=").Append(pqs.detailRad.ToString("0"));
            line.Append(" visRad=").Append(pqs.visRad.ToString("0"));
            line.Append(" quadsProFrame=").Append(pqs.maxQuadLenghtsPerFrame.ToString("0"));
            // KSPs eigene Auswertung der beiden Ziele: hier muss der neue Punkt auftauchen.
            line.Append(" zielHoehe=").Append(pqs.targetAltitude.ToString("0"));
            line.Append(" zweitesHoehe=").Append(pqs.targetSecondaryAltitude.ToString("0"));
            Vessel ship = FlightGlobals.ActiveVessel;
            if (ship != null)
            {
                line.Append(" schiffHoehe=").Append(ship.altitude.ToString("0"));
                line.Append(" tempo=").Append(ship.srfSpeed.ToString("0"));
                line.Append(" lage=").Append(ship.situation);
            }
            if (!string.IsNullOrEmpty(lastDetailText))
            {
                line.Append(" hinweis=").Append(lastDetailText);
                lastDetailText = "";
            }
            if (patchTest != null)
            {
                if (patchTest.Resting && !restingReported)
                {
                    restingReported = true;
                    Write(">>> KUGEL LIEGT AUF DEM PATCH: " + patchTest.BallReport());
                    Debug.Log("[PSRProbe] >>> KUGEL LIEGT AUF DEM PATCH");
                }
                line.Append(" patch=").Append(patchTest.Info);
                line.Append(" ansicht=").Append(patchTest.ViewName);
                line.Append("/").Append(patchTest.CameraDistance.ToString("0")).Append("m");
                line.Append(" patchStrahl=").Append(patchTest.PatchRay());
                line.Append(" netzNah=").Append(patchTest.NearMeshCheck());
                if (!patchTest.HasNear) line.Append(" landSuche=").Append(patchTest.NearSearch);
                line.Append(" kugel=").Append(patchTest.BallReport());
            }

            string text = line.ToString();
            Add(text);
            Write(text);
            Debug.Log("[PSRProbe] " + text);
            logged++;

            if (detail && found.Count > 0)
            {
                string detailLine = "Treffer unter dem Punkt: " + string.Join(" | ", found.ToArray());
                Add(detailLine);
                Write(detailLine);
            }
        }

        // Kurze Sonde direkt ueber der berechneten Oberflaeche: nur dort hilft ein Collider
        // wirklich, denn nur dort kann ein Fahrzeug aufsetzen.
        private string NearGround()
        {
            try
            {
                Vector3d from = point + up * 60.0;
                int count = Physics.RaycastNonAlloc((Vector3)from, (Vector3)(-up), hits, 400f, ~0,
                    QueryTriggerInteraction.Ignore);
                if (count > hits.Length) count = hits.Length;
                string nearest = "keins";
                double nearestDistance = double.PositiveInfinity;
                double nearestAltitude = double.NaN;
                for (int i = 0; i < count; i++)
                {
                    Collider c = hits[i].collider;
                    if (c == null || ownColliders.Contains(c)) continue;
                    if (hits[i].distance < nearestDistance)
                    {
                        nearestDistance = hits[i].distance;
                        nearest = Describe(hits[i]);
                        nearestAltitude = (from - body.position).magnitude - body.Radius - hits[i].distance;
                    }
                }
                if (double.IsPositiveInfinity(nearestDistance)) return "keins";
                return nearest + " hoehe=" + nearestAltitude.ToString("0.0")
                    + " fehler=" + (nearestAltitude - exactSurface).ToString("0.0");
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }

        private string ActiveVesselGround()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return "kein Schiff";
            Vector3d position = v.CoM;
            Vector3d down = (v.CoM - v.mainBody.position).normalized;
            try
            {
                int count = Physics.RaycastNonAlloc((Vector3)position, (Vector3)(-down), hits, 300f, ~0,
                    QueryTriggerInteraction.Ignore);
                if (count > hits.Length) count = hits.Length;
                string nearest = "keins";
                double nearestDistance = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    Collider c = hits[i].collider;
                    if (c == null || ownColliders.Contains(c)) continue;
                    if (hits[i].distance < nearestDistance)
                    {
                        nearestDistance = hits[i].distance;
                        nearest = Describe(hits[i]);
                    }
                }
                return nearest + (double.IsPositiveInfinity(nearestDistance) ? "" : "@" + nearestDistance.ToString("0.0"));
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }

        private void CollectOwnColliders()
        {
            nextOwnColliders = Time.unscaledTime + 1f;
            ownColliders.Clear();
            try
            {
                foreach (Vessel v in FlightGlobals.VesselsLoaded)
                {
                    if (v == null || v.parts == null) continue;
                    foreach (Part p in v.parts)
                        foreach (Collider c in p.GetPartColliders())
                            if (c != null) ownColliders.Add(c);
                }
            }
            catch { }
        }

        private string Describe(RaycastHit hit)
        {
            Collider c = hit.collider;
            if (c == null) return "?";
            return c.gameObject.name + "/" + LayerMask.LayerToName(c.gameObject.layer);
        }

        // Alles, was KSP an Bodenkollision aufgebaut hat - Name, Lage und die Felder, die
        // Reichweite und Tiefe des Kollisionsnetzes bestimmen.
        private string ColliderObjects()
        {
            FindColliderType();
            if (colliderType == null) return "typ fehlt";
            try
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(colliderType);
                if (all == null || all.Length == 0) return "0";
                StringBuilder text = new StringBuilder();
                text.Append(all.Length);
                int shown = 0;
                for (int i = 0; i < all.Length && shown < 3; i++)
                {
                    Component c = all[i] as Component;
                    if (c == null) continue;
                    shown++;
                    double distance = Vector3.Distance(c.transform.position, (Vector3)point);
                    text.Append(" [").Append(c.gameObject.name)
                        .Append(" dist=").Append(distance.ToString("0"))
                        .Append(" max=").Append(Value(c, "colliderMaxDistance"))
                        .Append(" tiefe=").Append(Value(c, "colliderDepth"))
                        .Append(" aktiv=").Append(Value(c, "isColliderActive")).Append("]");
                }
                return text.ToString();
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }

        private static string Value(object component, string name)
        {
            try
            {
                FieldInfo field = component.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = field == null ? null : field.GetValue(component);
                return value == null ? "?" : value.ToString();
            }
            catch { return "?"; }
        }

        private static void FindColliderType()
        {
            if (colliderTypeSearched) return;
            colliderTypeSearched = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "Assembly-CSharp") continue;
                    colliderType = a.GetType("PQS_Collider");
                    break;
                }
            }
            catch { }
        }

        private void CreateCamera()
        {
            DisposeCamera();
            try
            {
                if (FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null) return;
                rt = new RenderTexture(480, 270, 24, RenderTextureFormat.ARGB32);
                rt.name = "PSRProbe.Feed";
                rt.Create();
                GameObject go = new GameObject("PSRProbe.Camera");
                cam = go.AddComponent<Camera>();
                cam.CopyFrom(FlightCamera.fetch.mainCamera);
                cam.enabled = false;
                cam.targetTexture = rt;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 30000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f);
                cam.useOcclusionCulling = false;
                int ui = LayerMask.NameToLayer("UI");
                if (ui >= 0) cam.cullingMask &= ~(1 << ui);
                cam.cullingMask |= 1 << 15;   // eigener Patch liegt auf der Gelaendebene
                UpdateCamera(true);
            }
            catch (Exception e)
            {
                Debug.LogError("[PSRProbe] Kamera: " + e);
                DisposeCamera();
            }
        }

        // Laufendes Bild statt Einzelbild: die Kamera folgt dem gewaehlten Ziel, damit man den
        // Fall der Kugel wirklich sieht. 10 Bilder je Sekunde genuegen und kosten nichts.
        private void UpdateCamera(bool force)
        {
            if (cam == null) return;
            if (!force && Time.unscaledTime < nextCameraFrame) return;
            nextCameraFrame = Time.unscaledTime + 0.1f;
            try
            {
                Vector3d focus;
                double distance = 600.0;
                if (patchTest != null)
                {
                    focus = patchTest.ViewPoint;
                    distance = patchTest.CameraDistance;
                }
                else
                {
                    focus = point;
                }
                Vector3d focusUp = (focus - body.position).normalized;
                Vector3d axis = body.transform.up.normalized;
                Vector3d side = Vector3d.Cross(axis, focusUp).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3d.Cross(Vector3.right, focusUp).normalized;
                Vector3d eye = focus + (focusUp * 0.55 + side * 0.83).normalized * distance;
                cam.transform.SetPositionAndRotation((Vector3)eye,
                    Quaternion.LookRotation((Vector3)(focus - eye).normalized, (Vector3)focusUp));
                cam.Render();
            }
            catch (Exception e)
            {
                Debug.LogError("[PSRProbe] Kamera: " + e);
                DisposeCamera();
            }
        }

        private void DisposeCamera()
        {
            if (cam != null) { UnityEngine.Object.Destroy(cam.gameObject); cam = null; }
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
        }

        private void OnGUI()
        {
            float height = rt != null ? 350f : 170f;
            Rect box = new Rect(8f, 8f, 500f, height);
            GUI.Box(box, "");
            GUILayout.BeginArea(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f));
            GUILayout.Label("Stufe-2-Sonde  F7 Patch bauen  F6 Ansicht fern/nah  Bild↑↓ Zoom  Pos1 Kugel  F8 weg");
            GUILayout.Label(status + (logPath == null ? "" : "   (Log: " + logPath + ")"));
            if (rt != null) GUILayout.Label(rt, GUILayout.Width(470f), GUILayout.Height(264f));
            foreach (string line in history) GUILayout.Label(line);
            GUILayout.EndArea();
        }

        private void Add(string line)
        {
            history.Add(line);
            if (history.Count > HistoryLines) history.RemoveAt(0);
        }

        private void Write(string line)
        {
            try { if (logPath != null) File.AppendAllText(logPath, line + Environment.NewLine); }
            catch { }
        }

        private static string Fmt(Transform t) { return t == null ? "keiner" : t.name; }

        private void OnDestroy()
        {
            Restore("Szene beendet");
            try { if (harmony != null) harmony.UnpatchAll(harmony.Id); }
            catch (Exception e) { Debug.LogError("[PSRProbe] Unpatch: " + e.Message); }
            current = null;
        }
    }
}
