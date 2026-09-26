using System;
using UnityEngine;

namespace BoosterWatch
{
    // Eigener Boden unter einem getrackten Booster.
    //
    // Sicherheitsnetz. Seit 0.9.40 laesst TerrainDetailBubble KSP auch um ferne Booster echtes
    // Gelaende mit Kollidern bauen; die feinsten Quads (nur sie tragen Kollider) entstehen aber
    // unter Zeitbudget und koennen einem schnellen Booster hinterherhinken, und ohne die Blase gaebe
    // es dort gar keinen Boden. Dieses Netz gibt ihm bis dahin Boden - dieselbe Hoehe, dieselbe
    // Ebene wie KSPs Gelaende ("Local Scenery"). Sobald echter Boden gefunden wird, tritt es zurueck
    // (SetRealGround).
    //
    // Zwei Konstruktionsdetails sind wesentlich:
    //   * Der Wurzelknoten haengt unter PQS. Damit gehen Planetenrotation und jede
    //     Floating-Origin-Verschiebung automatisch mit; ein fest im Raum stehendes Netz wuerde
    //     nach wenigen Sekunden aus der Landschaft wandern.
    //   * Das Netz wird neu gesetzt, wenn der Booster weiterzieht, und der Kollider danach neu
    //     zugewiesen. PhysX merkt eine Aenderung an derselben Mesh-Instanz sonst nicht.
    public sealed class GroundPatch : IDisposable
    {
        // 1000 m Kantenlaenge bei rund 31 m Punktabstand: fein genug fuer einen Booster, grob
        // genug, um in einem Tick gebaut zu werden. Die Breite ist auch eine Reserve: eine Stufe,
        // die noch seitlich treibt, soll nicht gleich ueber den Rand laufen.
        private const int Resolution = 32;
        private const double HalfSize = 500.0;

        private GameObject root, meshObject;
        private Mesh mesh;
        private MeshCollider collider;
        private PQS pqs;
        private CelestialBody body;
        private Vector3d centre;
        // Mitte des Netzes im PQS-Raum: dreht der Planet (Inertialsystem), wandert sie mit ihm.
        private Vector3 centreLocal;
        // Wo der Booster beim letzten Bau stand, im PQS-Raum. Verglichen wird die WAAGERECHTE
        // Strecke seitdem: bis 0.9.39 wurde der Abstand zur Netzmitte am Boden genommen - der ist
        // die Hoehe ueber Grund und damit ueber 150 m immer "weit gezogen"; das Netz wurde auf den
        // letzten 2,3 km jedes fernen Sinkflugs alle 0,1 s neu gebaut (1089 Hoehenabfragen).
        private Vector3 builtFromLocal;
        // Eine Materialinstanz fuer alle Flaechen; frueher blieb bei jedem Abbau eine liegen.
        private static Material sharedMaterial;
        // Wie viele Messungen in Folge echter Boden unter dem Booster lag (SetRealGround).
        private int realGroundTicks;
        private const int RealGroundConfirmTicks = 3;
        private double builtAt = double.NegativeInfinity;
        private readonly Vector3[] vertices = new Vector3[GroundField.VertexCount(Resolution)];
        private readonly Vector2[] uv = new Vector2[GroundField.VertexCount(Resolution)];
        private readonly int[] indices = GroundField.Indices(Resolution);
        private bool reported;

        public bool Exists { get { return meshObject != null; } }
        // Der eigene Kollider. Der Booster darf ihn nicht fuer fremden Boden halten, sonst faellt
        // die Hoehenerkennung aus und die Landung wird nie erkannt (siehe TrackedBooster).
        public Collider Surface { get { return collider; } }
        public double SurfaceAltitude { get; private set; }

        // Liegt echter Boden (KSPs eigene Gelaendekollider) unter dem Booster? Dann verschwindet die
        // braune Flaeche sofort (sie laege genau auf dem Gelaende und flimmerte), und nach einigen
        // Messungen Bestaetigung auch ihr Kollider: das Hoehenfeld ist bilinear auf 31 m und kann
        // bis zu einem Meter UEBER dem echten Boden liegen - der Booster stuende sonst auf einer
        // unsichtbaren Flaeche. Geht der echte Boden verloren, ist beides sofort wieder da.
        public void SetRealGround(bool real)
        {
            realGroundTicks = real ? realGroundTicks + 1 : 0;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (meshObject == null) return;
            MeshRenderer view = meshObject.GetComponent<MeshRenderer>();
            if (view != null) view.enabled = realGroundTicks == 0;
            if (collider != null) collider.enabled = realGroundTicks < RealGroundConfirmTicks;
        }

        // Baut oder erneuert das Hoehenfeld unter dem Booster. Rueckgabe: liegt jetzt Boden dort?
        public bool Refresh(Vessel v, double time, double clearance)
        {
            if (v == null || v.mainBody == null || v.mainBody.pqsController == null) { Dispose(); return false; }
            body = v.mainBody;
            pqs = body.pqsController;
            Vector3d position = v.GetWorldPos3D();
            if (root == null)
            {
                if (!Create()) return false;
            }
            else if (!GroundPatchPolicy.MustRebuild(HorizontalTravel(position), time - builtAt, clearance))
            {
                // Der Booster ist kaum gezogen und der Boden ist noch frisch: nichts zu tun.
                return true;
            }

            Vector3d up = (position - body.position).normalized;
            double radius = SurfaceRadius(up);
            if (double.IsNaN(radius) || radius <= 0) { Dispose(); return false; }

            Vector3d north = Vector3.ProjectOnPlane(body.transform.up, up).normalized;
            if (north.sqrMagnitude < 0.5f) north = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            Vector3d east = Vector3d.Cross(north, up).normalized;
            centre = body.position + up * radius;
            SurfaceAltitude = radius - body.Radius;
            centreLocal = pqs.transform.InverseTransformPoint((Vector3)centre);
            // Zuerst den Wurzelknoten an die neue Mitte setzen, DANN die Punkte in seinen Raum
            // umrechnen. Andersherum lag das Netz um die Strecke zwischen alter und neuer Mitte
            // daneben - beim ersten Bau um den ganzen Planetenradius (Wurzel noch im Planetenmittelpunkt).
            root.transform.localPosition = centreLocal;
            builtFromLocal = pqs.transform.InverseTransformPoint((Vector3)position);
            // Guertel und Hosentraeger: ueber Wasser wird nichts gebaut, egal wie der Aufrufer
            // entschieden hat. Ein Netz auf dem Meeresboden waere hunderte Meter zu tief.
            if (body.ocean && SurfaceAltitude < 0) { Dispose(); return false; }

            int side = GroundField.SideCount(Resolution);
            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                {
                    double du, dv;
                    GroundField.Offset(Resolution, HalfSize, i, j, out du, out dv);
                    Vector3d direction = (centre + east * du + north * dv - body.position).normalized;
                    double height = SurfaceRadius(direction);
                    Vector3d point = body.position + direction * height;
                    vertices[j * side + i] = root.transform.InverseTransformPoint((Vector3)point);
                    uv[j * side + i] = new Vector2(i / (float)(side - 1), j / (float)(side - 1));
                }
            }

            // Die Rechnung laeuft in der lokalen Ebene des Wurzelknotens; damit bleibt sie auch
            // dann genau, wenn der Planet weit vom Weltursprung entfernt liegt.
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ApplyVisibility();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            builtAt = time;
            if (!reported)
            {
                reported = true;
                // Die Entscheidungsgrundlagen mitschreiben: nur so laesst sich nachlesen, warum
                // der Boden hier gebaut wurde (Entfernung, Hoehe, Wasser).
                double distance = FlightGlobals.ActiveVessel == null ? double.NaN
                    : Vector3d.Distance(position, FlightGlobals.ActiveVessel.GetWorldPos3D());
                bool overWater = body.ocean && SurfaceAltitude < 0;
                Debug.Log("[PhysStageRecovery] Eigener Boden unter Booster: " + Resolution + "x" + Resolution
                    + " Punkte, " + (2 * HalfSize).ToString("0") + " m breit, Hoehe "
                    + SurfaceAltitude.ToString("0.0") + " m, Ebene " + meshObject.layer
                    + " (" + LayerMask.LayerToName(meshObject.layer) + ")"
                    + ", Entfernung " + (RecoveryPolicy.Finite(distance) ? distance.ToString("0") + " m" : "unbekannt")
                    + ", ueberGrund " + (RecoveryPolicy.Finite(clearance) ? clearance.ToString("0") + " m" : "unbekannt")
                    + ", Wasser " + (overWater ? "ja" : "nein"));
            }
            return true;
        }

        // Radius der prozeduralen Oberflaeche in einer WELT-Richtung.
        //
        // PQS.GetSurfaceHeight erwartet eine Richtung im Koerpersystem des Planeten
        // (CelestialBody.GetRelSurfaceNVector), keine Weltrichtung. Mit der Weltrichtung fragte der
        // Patch bis 0.9.39 einen anderen Punkt des Planeten ab - um die aktuelle Drehung versetzt:
        // im Flug von 0.9.38 "Hoehe -1055 m, Wasser ja" unter einem Booster, der ueber 788 m hohem
        // Land flog, und in 0.9.39 gar kein Bau mehr, weil die Wasser-Sperre griff. Derselbe Weg
        // ueber Breite und Laenge wie in TrackedBooster.Measure.
        private double SurfaceRadius(Vector3d worldDirection)
        {
            Vector3d point = body.position + worldDirection * body.Radius;
            return pqs.GetSurfaceHeight(body.GetRelSurfaceNVector(body.GetLatitude(point), body.GetLongitude(point)));
        }

        private bool Create()
        {
            try
            {
                root = new GameObject("PhysStageRecovery.Boden");
                root.transform.SetParent(pqs.transform, false);
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                meshObject = new GameObject("PhysStageRecovery.Boden.Mesh");
                meshObject.transform.SetParent(root.transform, false);
                mesh = new Mesh();
                mesh.name = "PhysStageRecovery.Boden";
                mesh.MarkDynamic();
                MeshFilter filter = meshObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
                // Bewusst NICHT das Gelaendematerial: KSPs Terrain-Shader (mit Parallax erst recht)
                // braucht Daten, die ein zur Laufzeit gebautes Netz nicht hat, und rendert es dann
                // gar nicht - der Booster stand auf einer unsichtbaren Flaeche.
                if (sharedMaterial == null) sharedMaterial = FallbackMaterial();
                renderer.sharedMaterial = sharedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                int layer = LayerMask.NameToLayer("Local Scenery");
                if (layer < 0) layer = 15;
                meshObject.layer = layer;
                root.layer = layer;
                collider = meshObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Eigener Boden konnte nicht gebaut werden: " + e);
                Dispose();
                return false;
            }
        }

        // Waagerechte Strecke seit dem letzten Bau (PQS-Raum, Ursprung im Planetenmittelpunkt).
        private float HorizontalTravel(Vector3d position)
        {
            Vector3 now = pqs.transform.InverseTransformPoint((Vector3)position);
            Vector3 moved = now - builtFromLocal;
            Vector3 radial = builtFromLocal.normalized;
            return (moved - Vector3.Dot(moved, radial) * radial).magnitude;
        }

        private static Material FallbackMaterial()
        {
            Shader shader = Shader.Find("KSP/Diffuse");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Diffuse");
            if (shader == null) return null;
            Material material = new Material(shader);
            material.color = new Color(0.35f, 0.32f, 0.26f, 1f);
            material.name = "PhysStageRecovery.Boden";
            return material;
        }

        public void Dispose()
        {
            if (root != null) { UnityEngine.Object.Destroy(root); root = null; }
            meshObject = null;
            collider = null;
            if (mesh != null) { UnityEngine.Object.Destroy(mesh); mesh = null; }
            builtAt = double.NegativeInfinity;
            SurfaceAltitude = 0;
            realGroundTicks = 0;
        }
    }
}
