using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // Feines Gelaende um ferne Booster: ein zweites (drittes, ...) Detail-Zentrum fuer KSPs PQS.
    //
    // KSP baut die Planetenoberflaeche aus einem Quadbaum (PQ). Wie fein ein Quad unterteilt wird,
    // entscheidet eine einzige Zahl pro Quad, PQ.gcDist: der Grosskreisabstand zum PQS-Ziel (das
    // aktive Schiff) mal 1,3 plus dessen Hoehe ueber Grund (PQ.UpdateTargetRelativity). Ist gcDist
    // kleiner als die Schwelle der Stufe, wird unterteilt, ist es groesser als die Kollaps-Schwelle,
    // wird zusammengelegt; und nur Quads mit gcd1 < visibleRadius werden ueberhaupt gezeigt. Ein
    // Booster 200 km vom aktiven Schiff sitzt deshalb auf den groebsten Quads, die es gibt - und
    // die Stufen wechseln, waehrend das aktive Schiff weiterfliegt.
    //
    // Der Eingriff ist klein: nach KSPs eigener Rechnung wird gcDist/gcd1 durch den kleineren Wert
    // ersetzt, den der Quad zu einem Booster haette. Damit unterteilt KSP selbst um den Booster so
    // fein wie um das aktive Schiff - mit echtem Gelaendenetz, echtem Material (auch Parallax) und
    // den echten Bodenkollidern (PQSMod_QuadMeshColliders haengt sie an die feinsten Quads).
    //
    // Eine Bremse wird fuer die Dauer geloest, weil sie nur am aktiven Schiff haengt:
    // maxLevelAtCurrentTgtSpeed - KSP begrenzt die Feinheit, wenn das Ziel schnell fliegt (die
    // Oberstufe mit 2 km/s); der Booster soll trotzdem die volle Stufe bekommen. (DisableSubdivision
    // ist dagegen keine Bremse: isSubdivisionEnabled liest ausser Enable/Disable niemand.)
    //
    // Und die groesste Bremse von allen (Flug vom 25.09.2026, 19:26): PQSMod_CelestialBodyTransform
    // SCHALTET DAS GANZE GELAENDE AB, sobald das aktive Schiff hoeher als deactivateAltitude ist
    // (DeactivateSphere) - keine Quads, keine Bodenkollider, im Kamerabild nur noch der verschwommene
    // Scaled-Space-Planet weit unter dem Booster. Das war das "stufenweise Wegladen". Solange Booster
    // Zentren haben, bleibt die Kugel mit forceActivate an. Dazu blendet KSP das Gelaende mit der
    // Hoehe der Hauptkamera aus (AltitudeFade, _PlanetOpacity o. ae.); fuer das Bild der
    // Booster-Kamera wird die Blende auf "am Boden" gestellt und danach sofort zurueck
    // (BeginCameraRender / EndCameraRender).
    //
    // forceActivate hat eine Nebenwirkung: PQSMod_CelestialBodyTransform.OnPreUpdate kehrt dann
    // sofort zurueck - und genau dort setzt KSP sonst das PQS-Ziel auf das aktive Schiff. Nach einem
    // Schiffswechsel oder wenn die Oberstufe zerstoert wird, bliebe das Ziel auf dem alten (oder
    // einem geloeschten) Transform stehen. BeforeVisual holt das nach.
    public static class TerrainDetailBubble
    {
        private const string Id = "PhysStageRecovery.TerrainDetail";
        // Mehr Zentren kosten mehr Quads; vier reichen fuer jeden sinnvollen Start.
        public const int MaxAnchors = 4;

        private struct Anchor { public Vector3d Direction; public double Height; }

        private static Harmony harmony;
        private static Func<List<Vessel>> source;
        private static readonly List<Vessel> scratch = new List<Vessel>();
        // Zentren pro PQS, in dessen eigenem Raum (wie PQS.relativeTargetPositionNormalized).
        private static readonly Dictionary<PQS, Anchor[]> anchors = new Dictionary<PQS, Anchor[]>();
        private static readonly Anchor[] none = new Anchor[0];
        private static bool reported;
        // Kugeln, bei denen WIR forceActivate gesetzt haben - nur die werden wieder freigegeben.
        private static readonly HashSet<PQSMod_CelestialBodyTransform> forced = new HashSet<PQSMod_CelestialBodyTransform>();
        private static readonly Dictionary<PQS, PQSMod_CelestialBodyTransform> bodyTransforms
            = new Dictionary<PQS, PQSMod_CelestialBodyTransform>();
        private static PQSMod_CelestialBodyTransform fadedFor;
        private static bool reportedActivation;

        public static bool Active { get; private set; }

        public static void Install(Func<List<Vessel>> vessels)
        {
            Remove();
            source = vessels;
            MethodInfo visual = AccessTools.Method(typeof(PQS), "UpdateVisual");
            MethodInfo relativity = AccessTools.Method(typeof(PQ), "UpdateTargetRelativity");
            if (visual == null || relativity == null)
                throw new MissingMethodException("PQS.UpdateVisual/PQ.UpdateTargetRelativity");
            harmony = new Harmony(Id);
            harmony.Patch(visual, prefix: new HarmonyMethod(typeof(TerrainDetailBubble), nameof(BeforeVisual)),
                postfix: new HarmonyMethod(typeof(TerrainDetailBubble), nameof(AfterVisual)));
            harmony.Patch(relativity, postfix: new HarmonyMethod(typeof(TerrainDetailBubble), nameof(AfterRelativity)));
            Debug.Log("[PhysStageRecovery] Gelaende-Detail um Booster installiert.");
        }

        public static void Remove()
        {
            ReleaseAll();
            reported = false; reportedActivation = false;
            bodyTransforms.Clear();
            textureAnchors.Clear();
            fadedFor = null;
            source = null;
            anchors.Clear();
            Active = false;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }

        private static void ReleaseAll()
        {
            foreach (PQSMod_CelestialBodyTransform t in forced) if (t != null) t.forceActivate = false;
            forced.Clear();
        }

        // Vor KSPs eigener Rechnung: haben wir die Kugel festgehalten, das Ziel selbst nachfuehren.
        public static void BeforeVisual(PQS __instance)
        {
            try
            {
                if (forced.Count == 0) return;
                PQSMod_CelestialBodyTransform keeper;
                if (!bodyTransforms.TryGetValue(__instance, out keeper) || keeper == null || !forced.Contains(keeper)) return;
                Vessel active = FlightGlobals.ActiveVessel;
                if (active != null && __instance.target != active.transform) __instance.SetTarget(active.transform);
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Gelaende-Ziel: " + e); }
        }

        // Einmal pro PQS-Aktualisierung: die Zentren dieses Planeten neu bestimmen.
        public static void AfterVisual(PQS __instance)
        {
            try
            {
                Anchor[] list = none;
                scratch.Clear();
                List<Vessel> vessels = source == null ? null : source();
                if (vessels != null)
                    foreach (Vessel v in vessels)
                        if (v != null && v.mainBody != null && v.mainBody.pqsController == __instance && scratch.Count < MaxAnchors)
                            scratch.Add(v);
                if (scratch.Count > 0)
                {
                    list = new Anchor[scratch.Count];
                    for (int i = 0; i < scratch.Count; i++)
                    {
                        // Genau wie KSP das Ziel umrechnet (PQS.UpdateVisual): in den PQS-Raum.
                        Vector3d relative = __instance.transform.InverseTransformPoint(scratch[i].CoM);
                        Vector3d direction = relative.normalized;
                        list[i] = new Anchor { Direction = direction,
                            Height = relative.magnitude - __instance.GetSurfaceHeight(direction) };
                    }
                    // Die Bremsen des aktiven Schiffs gelten nicht fuer die Booster.
                    __instance.maxLevelAtCurrentTgtSpeed = __instance.maxLevel;
                    if (!reported)
                    {
                        reported = true;
                        Debug.Log("[PhysStageRecovery] Gelaende-Detail: " + list.Length + " Zentrum/Zentren auf "
                            + __instance.name + ", Stufen " + __instance.minLevel + "-" + __instance.maxLevel
                            + ", erstes " + list[0].Height.ToString("0") + " m ueber Grund.");
                    }
                }
                // Nur nachschlagen, wenn es etwas zu tun gibt: Kugeln ohne eigenen
                // CelestialBodyTransform (z. B. der Ozean) wuerden sonst jeden Frame gesucht.
                PQSMod_CelestialBodyTransform keeper = list.Length > 0 || forced.Count > 0 ? BodyTransform(__instance) : null;
                if (keeper != null)
                {
                    if (list.Length > 0 && !keeper.forceActivate)
                    {
                        keeper.forceActivate = true;
                        forced.Add(keeper);
                        if (!reportedActivation)
                        {
                            reportedActivation = true;
                            Debug.Log("[PhysStageRecovery] Gelaende bleibt aktiv (" + __instance.name
                                + "): KSP haette es ab " + keeper.deactivateAltitude.ToString("0")
                                + " m Hoehe des aktiven Schiffs abgeschaltet, gerade " + __instance.visibleAltitude.ToString("0") + " m.");
                        }
                    }
                    else if (list.Length == 0 && forced.Remove(keeper))
                        keeper.forceActivate = false;
                }
                if (list.Length == 0) anchors.Remove(__instance); else anchors[__instance] = list;
                Active = anchors.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Gelaende-Detail abgeschaltet: " + e);
                ReleaseAll();
                source = null;
                anchors.Clear();
                Active = false;
            }
        }

        private static PQSMod_CelestialBodyTransform BodyTransform(PQS pqs)
        {
            PQSMod_CelestialBodyTransform t;
            // Auch "keiner" wird gemerkt; neu gesucht wird nur, wenn ein gefundener zerstoert wurde.
            if (!bodyTransforms.TryGetValue(pqs, out t) || ((object)t != null && t == null))
            {
                t = pqs.GetComponentInChildren<PQSMod_CelestialBodyTransform>(true);
                bodyTransforms[pqs] = t;
            }
            return t;
        }

        // Texturverankerung fuer das Bild der Booster-Kamera.
        //
        // Die Gelaendeshader (Stock und Parallax) legen ihre Detailtexturen ueber die WELTposition
        // plus einen globalen Versatz (_floatingOriginOffset bzw. _TerrainShaderOffset). KSP fuehrt
        // diesen Versatz nur bei Floating-Origin-Spruengen nach, nicht fuer die Krakensbane-
        // Bewegung - und setzt ihn auf 0, sobald die Hauptkamera hoch ueber dem Gelaende ist
        // (AltitudeFade -> FloatingOrigin.ResetTerrainShaderOffset). Beides ist egal, solange dort
        // niemand hinschaut. Unsere Kamera schaut hin: mit der Oberstufe auf 1,2 km/s wanderten die
        // Texturen im Video vom 25.09.2026 sichtbar ueber den Boden.
        //
        // Fuer unser Bild wird der Versatz deshalb so gesetzt, dass Weltposition + Versatz =
        // Position relativ zum Planeten minus ein fester Bezugspunkt nahe dem Booster ist. Im
        // rotierenden Bezugssystem (RotatingFrameHold) steht der Planet still, die Texturen damit
        // auch. Der Bezugspunkt haelt die Zahlen klein (Genauigkeit im Shader).
        private static readonly int floatingOriginId = Shader.PropertyToID("_floatingOriginOffset");
        private static readonly int terrainOffsetId = Shader.PropertyToID("_TerrainShaderOffset");
        private static readonly int opacityId = Shader.PropertyToID("_PlanetOpacity");
        private static readonly Dictionary<Guid, Vector3d> textureAnchors = new Dictionary<Guid, Vector3d>();
        private static Vector4 savedFloatingOrigin, savedTerrainOffset;
        private static float savedOpacity;
        private static bool globalsSaved;

        // Vor dem Bild der Booster-Kamera: Gelaende voll sichtbar, wie fuer eine Kamera am Boden,
        // und die Texturen am Planeten verankert.
        public static void BeginCameraRender(Vessel target)
        {
            fadedFor = null;
            globalsSaved = false;
            try
            {
                PQS pqs = target == null || target.mainBody == null ? null : target.mainBody.pqsController;
                if (pqs == null || !pqs.isActive) return;
                PQSMod_CelestialBodyTransform t = BodyTransform(pqs);
                if (t == null || t.planetFade == null) return;
                Fade(t, 0);
                fadedFor = t;

                savedFloatingOrigin = Shader.GetGlobalVector(floatingOriginId);
                savedTerrainOffset = Shader.GetGlobalVector(terrainOffsetId);
                savedOpacity = Shader.GetGlobalFloat(opacityId);
                globalsSaved = true;
                Vector3d body = target.mainBody.position;
                Vector3d anchor;
                if (!textureAnchors.TryGetValue(target.id, out anchor))
                {
                    Vector3d relative = target.CoMD - body;
                    // Auf volle Kilometer, damit der Bezugspunkt eine glatte Zahl ist.
                    anchor = new Vector3d(Math.Round(relative.x / 1000) * 1000,
                        Math.Round(relative.y / 1000) * 1000, Math.Round(relative.z / 1000) * 1000);
                    textureAnchors[target.id] = anchor;
                }
                Vector3d offset = -body - anchor;
                Vector4 value = new Vector4((float)offset.x, (float)offset.y, (float)offset.z, 0);
                Shader.SetGlobalVector(floatingOriginId, value);
                Shader.SetGlobalVector(terrainOffsetId, value);
                // Parallax liest die Deckkraft als globalen Wert aus dem Material der Hauptkamera.
                if (pqs.surfaceMaterial != null && pqs.surfaceMaterial.HasProperty(opacityId))
                    Shader.SetGlobalFloat(opacityId, pqs.surfaceMaterial.GetFloat(opacityId));
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Gelaende-Blende: " + e); }
        }

        // Danach: Blende und Versatz wieder so, wie KSP sie fuer die Hauptkamera gesetzt hat.
        public static void EndCameraRender()
        {
            PQSMod_CelestialBodyTransform t = fadedFor;
            fadedFor = null;
            try
            {
                if (t != null && t.sphere != null) Fade(t, t.overrideFade ? 0 : t.sphere.visibleAltitude);
            }
            catch (Exception e) { Debug.LogError("[PhysStageRecovery] Gelaende-Blende: " + e); }
            // Nach der Blende: sie kann selbst den Versatz zuruecksetzen.
            if (globalsSaved)
            {
                globalsSaved = false;
                Shader.SetGlobalVector(floatingOriginId, savedFloatingOrigin);
                Shader.SetGlobalVector(terrainOffsetId, savedTerrainOffset);
                Shader.SetGlobalFloat(opacityId, savedOpacity);
            }
        }

        private static void Fade(PQSMod_CelestialBodyTransform t, double altitude)
        {
            t.planetFade.DoFade(altitude);
            if (t.secondaryFades != null)
                foreach (PQSMod_CelestialBodyTransform.AltitudeFade f in t.secondaryFades)
                    if (f != null) f.DoFade(altitude);
        }

        // Pro Quad und Frame: den kleineren Abstand nehmen - KSPs eigener zum aktiven Schiff oder
        // derselbe Ausdruck zu einem Booster.
        public static void AfterRelativity(PQ __instance, double ___angularinterval)
        {
            Anchor[] list;
            if (!Active || __instance.sphereRoot == null || !anchors.TryGetValue(__instance.sphereRoot, out list)) return;
            double radius = __instance.sphereRoot.radius;
            for (int i = 0; i < list.Length; i++)
            {
                double dot = Vector3d.Dot(__instance.positionPlanetRelative, list[i].Direction);
                if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
                double gcd1 = Math.Acos(dot) * radius * 1.3;
                double gcDist = gcd1 + Math.Abs(list[i].Height) - ___angularinterval;
                if (gcd1 < __instance.gcd1) __instance.gcd1 = gcd1;
                if (gcDist < __instance.gcDist) __instance.gcDist = gcDist;
            }
        }
    }
}
