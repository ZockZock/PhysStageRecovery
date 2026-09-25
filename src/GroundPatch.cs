using System;
using UnityEngine;

namespace BoosterWatch
{
    // Eigener Boden unter einem getrackten Booster.
    //
    // KSP baut Gelaende und dessen Kollision nur um das aktive Schiff. Ein Booster, der hunderte
    // Kilometer entfernt landet, hat dort keinen Boden: er faellt durch die sichtbare Landschaft,
    // und der Aufsetzkontakt muss aus der prozeduralen Hoehe geschaetzt werden. Dieses Netz gibt
    // ihm echten Boden - dieselbe Hoehe, dieselbe Ebene wie KSPs Gelaende ("Local Scenery"),
    // damit die Teilekollision des Spiels ihn genauso behandelt wie echten Grund.
    //
    // Zwei Konstruktionsdetails sind wesentlich:
    //   * Der Wurzelknoten haengt unter PQS. Damit gehen Planetenrotation und jede
    //     Floating-Origin-Verschiebung automatisch mit; ein fest im Raum stehendes Netz wuerde
    //     nach wenigen Sekunden aus der Landschaft wandern.
    //   * Das Netz wird neu gesetzt, wenn der Booster weiterzieht, und der Kollider danach neu
    //     zugewiesen. PhysX merkt eine Aenderung an derselben Mesh-Instanz sonst nicht.
    public sealed class GroundPatch : IDisposable
    {
        // 700 m Kantenlaenge bei rund 22 m Punktabstand: fein genug fuer einen Booster, grob
        // genug, um in einem Tick gebaut zu werden.
        private const int Resolution = 32;
        private const double HalfSize = 350.0;

        private GameObject root, meshObject;
        private Mesh mesh;
        private MeshCollider collider;
        private PQS pqs;
        private CelestialBody body;
        private Vector3d centre;
        private double builtAt = double.NegativeInfinity;
        private readonly Vector3[] vertices = new Vector3[GroundField.VertexCount(Resolution)];
        private readonly Vector2[] uv = new Vector2[GroundField.VertexCount(Resolution)];
        private readonly int[] indices = GroundField.Indices(Resolution);
        private bool reported;

        public bool Exists { get { return meshObject != null; } }
        public double SurfaceAltitude { get; private set; }
        public double BuiltAt { get { return builtAt; } }

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
            else if (!GroundPatchPolicy.MustRebuild(Vector3d.Distance(position, centre), time - builtAt, clearance))
            {
                // Der Booster ist kaum gezogen und der Boden ist noch frisch: nichts zu tun.
                return true;
            }

            Vector3d up = (position - body.position).normalized;
            double radius = pqs.GetSurfaceHeight(up);
            if (double.IsNaN(radius) || radius <= 0) { Dispose(); return false; }

            Vector3d north = Vector3.ProjectOnPlane(body.transform.up, up).normalized;
            if (north.sqrMagnitude < 0.5f) north = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            Vector3d east = Vector3d.Cross(north, up).normalized;
            centre = body.position + up * radius;
            SurfaceAltitude = radius - body.Radius;
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
                    double height = pqs.GetSurfaceHeight(direction);
                    Vector3d point = body.position + direction * height;
                    vertices[j * side + i] = root.transform.InverseTransformPoint((Vector3)point);
                    uv[j * side + i] = new Vector2(i / (float)(side - 1), j / (float)(side - 1));
                }
            }

            // Die Rechnung laeuft in der lokalen Ebene des Wurzelknotens; damit bleibt sie auch
            // dann genau, wenn der Planet weit vom Weltursprung entfernt liegt.
            root.transform.localPosition = pqs.transform.InverseTransformPoint((Vector3)centre);
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
                renderer.sharedMaterial = pqs.surfaceMaterial != null ? pqs.surfaceMaterial : FallbackMaterial();
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
        }
    }
}
