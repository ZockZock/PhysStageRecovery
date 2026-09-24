using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BoosterWatch
{
    // Read-only terrain diagnostics.
    //
    // KSP builds its ground collision from a separate PQS_Collider mesh that is limited by
    // colliderMaxDistance / colliderDepth and is centred on PQS.target, which is the active
    // vessel. A tracked booster far away therefore collides against nothing or against a much
    // coarser surface than the procedural height we compute.
    //
    // Nothing here changes any behaviour: it only compares the height sources KSP offers and
    // writes them to KSP.log, so the size of that error can be measured in one flight.
    public static class TerrainProbe
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // The exact query that ignores whether the quad is built is internal to KSP.
        private static readonly MethodInfo ForcedSurface = typeof(PQS).GetMethod("GetSurfaceHeight", Flags, null,
            new[] { typeof(Vector3d), typeof(bool) }, null);
        private static Type colliderType, advancedType;
        private static bool searched;

        private static void Search()
        {
            if (searched) return;
            searched = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Assembly-CSharp") continue;
                colliderType = assembly.GetType("PQS_Collider");
                advancedType = assembly.GetType("PQS_ColliderAdvanced");
                break;
            }
        }

        private static string Name(Transform t) { return t == null ? "keiner" : t.name; }

        private static string Value(object component, string name)
        {
            if (component == null) return "?";
            try
            {
                FieldInfo field = component.GetType().GetField(name, Flags);
                object value = field == null ? null : field.GetValue(component);
                return value == null ? "?" : value.ToString();
            }
            catch { return "?"; }
        }

        private static double Forced(PQS pqs, Vector3d radial, double fallback)
        {
            if (ForcedSurface == null) return fallback;
            try { return (double)ForcedSurface.Invoke(pqs, new object[] { radial, true }); }
            catch { return fallback; }
        }

        // Once per mission: how KSP configured the terrain for this body, how far its detail and
        // its ground colliders reach, and which transforms the detail is currently centred on.
        public static void LogSetup(CelestialBody body)
        {
            try
            {
                Search();
                PQS pqs = body == null ? null : body.pqsController;
                if (pqs == null) { Debug.Log("[PhysStageRecovery] Terrain setup: kein PQS fuer diese Welt"); return; }
                StringBuilder text = new StringBuilder();
                text.Append("[PhysStageRecovery] Terrain setup body=").Append(body.name);
                text.Append(" target=").Append(Name(pqs.target));
                text.Append(" secondary=").Append(Name(pqs.secondaryTarget));
                text.Append(" targetAltitude=").Append(pqs.targetAltitude.ToString("0"));
                text.Append(" secondaryAltitude=").Append(pqs.targetSecondaryAltitude.ToString("0"));
                text.Append(" level=").Append(pqs.minLevel).Append("-").Append(pqs.maxLevel);
                text.Append(" detailRad=").Append(pqs.detailRad.ToString("0"));
                text.Append(" visRad=").Append(pqs.visRad.ToString("0"));
                text.Append(" quadsPerFrame=").Append(pqs.maxQuadLenghtsPerFrame.ToString("0"));
                text.Append(" exaktAbfrage=").Append(ForcedSurface == null ? "nicht verfuegbar" : "verfuegbar");
                if (colliderType == null) text.Append(" PQS_Collider=nicht gefunden");
                else
                {
                    UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(colliderType);
                    text.Append(" colliderAnzahl=").Append(found.Length);
                    for (int i = 0; i < found.Length && i < 4; i++)
                    {
                        Component c = found[i] as Component;
                        if (c == null) continue;
                        text.Append(" [").Append(c.gameObject.name)
                            .Append(" maxDistance=").Append(Value(c, "colliderMaxDistance"))
                            .Append(" depth=").Append(Value(c, "colliderDepth"))
                            .Append(" aktiv=").Append(Value(c, "isColliderActive")).Append("]");
                    }
                }
                if (advancedType != null)
                    text.Append(" advancedAnzahl=").Append(Resources.FindObjectsOfTypeAll(advancedType).Length);
                Debug.Log(text.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Terrain setup fehlgeschlagen: " + e.Message);
            }
        }

        // One comparison line for a tracked vessel: every height source KSP offers, plus where a
        // downward ray actually finds a collider. Never throws into the flight loop.
        public static string Report(Vessel v) { return Report(v, double.NaN, ""); }

        // bottom and rejected come from the tracked booster's own measurement; they are the two
        // values that decide whether the landing height can be trusted at all.
        public static string Report(Vessel v, double bottom, string rejected)
        {
            try
            {
                if (v == null || v.mainBody == null) return "Terrain probe: kein Fahrzeug";
                CelestialBody body = v.mainBody;
                PQS pqs = body.pqsController;
                if (pqs == null) return "Terrain probe: kein PQS";
                Vector3d position = v.GetWorldPos3D();
                Vector3d radial = body.GetRelSurfaceNVector(body.GetLatitude(position), body.GetLongitude(position));
                double exact = pqs.GetSurfaceHeight(radial) - body.Radius;
                double forced = Forced(pqs, radial, exact) - body.Radius;
                double quad = pqs.GetAltitude(position);
                Vector3 up = (v.CoM - body.position).normalized;
                RaycastHit own = v.HeightFromSurfaceHit;
                double distance = FlightGlobals.ActiveVessel == null ? 0
                    : Vector3d.Distance(position, FlightGlobals.ActiveVessel.GetWorldPos3D());
                StringBuilder text = new StringBuilder();
                text.Append("Terrain probe ").Append(v.id.ToString().Substring(0, 8));
                text.Append(" distance=").Append((distance / 1000).ToString("0.0")).Append("km");
                text.Append(" bodenPublic=").Append(exact.ToString("0.00"));
                text.Append(" bodenInternal=").Append(forced.ToString("0.00"));
                text.Append(" differenz=").Append((exact - forced).ToString("0.00"));
                text.Append(" pqsGetAltitude=").Append(quad.ToString("0.00"));
                text.Append(" vesselAlt=").Append(v.altitude.ToString("0.00"));
                text.Append(" pqsAlt=").Append(v.pqsAltitude.ToString("0.00"));
                text.Append(" terrainAlt=").Append(v.terrainAltitude.ToString("0.00"));
                text.Append(" radarAlt=").Append(v.radarAltitude.ToString("0.00"));
                text.Append(" eigeneHoehe=").Append(v.GetHeightFromSurface().ToString("0.00"));
                text.Append(" eigenesHit=").Append(own.collider == null ? "keins"
                    : own.collider.gameObject.name + "/" + own.distance.ToString("0.00"));
                text.Append(" bodenTiefe=").Append(double.IsNaN(bottom) ? "?" : bottom.ToString("0.00"));
                if (!string.IsNullOrEmpty(rejected)) text.Append(" verworfen=").Append(rejected);
                // Everything a downward ray can find, with the vessel's own colliders removed, so
                // the ground layer of this body can be read straight out of the log.
                text.Append(" alleLayer=").Append(AllLayers(v, position, up));
                return text.ToString();
            }
            catch (Exception e) { return "Terrain probe fehlgeschlagen: " + e.Message; }
        }

        private static string Layer(int layer) { return "L" + layer + "(" + LayerMask.LayerToName(layer) + ")"; }

        // Everything a downward ray can find, with the vessel's own colliders removed, so the
        // ground layer of this body can be read straight out of the log.
        private static string AllLayers(Vessel v, Vector3d position, Vector3 up)
        {
            try
            {
                HashSet<Collider> own = new HashSet<Collider>();
                foreach (Part part in v.parts)
                    foreach (Collider collider in part.GetPartColliders())
                        if (collider != null) own.Add(collider);
                RaycastHit[] hits = Physics.RaycastAll((Vector3)position, -up, 30000f, ~0, QueryTriggerInteraction.Ignore);
                int ownHits = 0, foreignHits = 0;
                double nearest = double.PositiveInfinity;
                string nearestName = "keins";
                Dictionary<int, int> layers = new Dictionary<int, int>();
                foreach (RaycastHit candidate in hits)
                {
                    if (candidate.collider == null) continue;
                    if (own.Contains(candidate.collider)) { ownHits++; continue; }
                    foreignHits++;
                    int layer = candidate.collider.gameObject.layer;
                    int count;
                    layers[layer] = layers.TryGetValue(layer, out count) ? count + 1 : 1;
                    if (candidate.distance < nearest)
                    {
                        nearest = candidate.distance;
                        nearestName = candidate.collider.gameObject.name + " " + Layer(layer);
                    }
                }
                StringBuilder text = new StringBuilder();
                text.Append("eigene=").Append(ownHits).Append(" fremde=").Append(foreignHits);
                text.Append(" nah=").Append(double.IsPositiveInfinity(nearest)
                    ? nearestName : nearestName + " dist" + nearest.ToString("0.00"));
                foreach (KeyValuePair<int, int> entry in layers)
                    text.Append(" [").Append(Layer(entry.Key)).Append(" x").Append(entry.Value).Append("]");
                return text.ToString();
            }
            catch (Exception e) { return "fehlgeschlagen: " + e.Message; }
        }
    }
}
