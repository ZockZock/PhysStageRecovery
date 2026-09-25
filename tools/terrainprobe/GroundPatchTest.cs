using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSRTerrainProbe
{
    // Stufe 2 als Prototyp: eigenen Boden bauen, wo KSP keinen hat.
    //
    // Ein Höhenfeld aus der exakten prozeduralen Oberflaeche (PQS.GetSurfaceHeight) als Netz,
    // darunter ein MeshCollider auf derselben Ebene wie KSPs eigenes Gelaende ("Local Scenery").
    // Der Wurzelknoten haengt unter PQS, damit Drehung der Welt und Floating-Origin-Verschiebung
    // automatisch mitgehen - sonst wandert der Patch aus der Landschaft.
    //
    // Zwei Patches in einem Flug:
    //   nah  (3 km)  - liegt auf Gelaende, das KSP selbst gebaut hat: prueft, ob unser Hoehenfeld
    //                  die richtige Form hat (Vergleich im Kamerabild).
    //   fern (250 km) - dort gibt es nichts: eine fallende Testkugel kann nur auf unserem Patch
    //                  zur Ruhe kommen. Das ist der Kollisionsbeweis.
    public sealed class GroundPatchTest : IDisposable
    {
        private const int Resolution = 48;
        private const double HalfSize = 400.0;
        private const double BallHeight = 250.0;
        // Gross genug, um auf einem 800 m breiten Patch aus 1500 m Entfernung sichtbar zu sein.
        public const double BallRadius = 20.0;
        // Die Kugel faellt genau ueber der Patchmitte, damit "liegt auf der Flaeche" eindeutig ist.
        private const double BallOffset = 0.0;
        // So weit seitlich wird die Sondenmessung abgesetzt, damit sie nicht die Kugel trifft.
        private const double RayOffset = 150.0;

        public GameObject NearRoot, FarRoot, Ball;
        public Rigidbody BallBody;
        public string Info = "";
        // 0 = Kugel (Standard, damit man den Fall sieht), 1 = ferner Patch, 2 = naher Patch.
        public int View;
        // Abstand der Sondenkamera. 600 m zeigt die 40 m grosse Kugel gross genug und den Boden
        // darunter, ohne dass die Kugel zu einem Punkt wird.
        public double CameraDistance = 600.0;
        private CelestialBody body;
        private PQS pqs;
        private Vector3d up;
        private double nearSurface;
        private string gravityInfo = "";

        // Weltpositionen immer frisch aus der Hierarchie lesen: der Patch haengt unter PQS und
        // wandert deshalb mit jeder Floating-Origin-Verschiebung mit. Ein gespeicherter
        // Weltvektor waere nach wenigen Sekunden falsch.
        private Vector3d WorldCenter(GameObject root)
        {
            if (root == null) return Vector3d.zero;
            return root.transform.position;
        }

        private Vector3d FarCenter { get { return WorldCenter(FarRoot); } }
        private Vector3d NearCenter { get { return WorldCenter(NearRoot); } }
        private double FarSurface { get { return SurfaceUnder(FarCenter); } }

        // Hoehe der prozeduralen Oberflaeche unter einem beliebigen Weltpunkt.
        private double SurfaceUnder(Vector3d position)
        {
            try
            {
                Vector3d direction = (position - body.position).normalized;
                return pqs.GetSurfaceHeight(direction) - body.Radius;
            }
            catch { return double.NaN; }
        }

        public bool HasBall { get { return Ball != null; } }
        public Vector3d BallPosition { get { return Ball == null ? FarCenter : (Vector3d)Ball.transform.position; } }
        public bool HasNear { get { return NearRoot != null; } }

        public string ViewName
        {
            get
            {
                if (View == 0) return HasBall ? "kugel" : "keine kugel";
                if (View == 1) return "fern";
                return HasNear ? "nah" : "kein land";
            }
        }

        // Zielpunkt der Kamera, je nach Ansicht immer frisch gelesen.
        public Vector3d ViewPoint
        {
            get
            {
                if (View == 0 && HasBall) return BallPosition;
                if (View == 1 || !HasNear) return FarCenter;
                return NearCenter;
            }
        }

        public void CycleView()
        {
            View = (View + 1) % 3;
            if (View == 2 && !HasNear) View = 0;
        }

        public void Dispose()
        {
            if (NearRoot != null) { UnityEngine.Object.Destroy(NearRoot); NearRoot = null; }
            if (FarRoot != null) { UnityEngine.Object.Destroy(FarRoot); FarRoot = null; }
            if (Ball != null) { UnityEngine.Object.Destroy(Ball); Ball = null; BallBody = null; }
            Info = "";
        }

        // Baut beide Patches. shipPosition ist die Position des aktiven Schiffes, von dort wird
        // der nahe Patch gerechnet; der ferne kommt als fertiger Punkt herein.
        public void Build(PQS pqsController, CelestialBody planet, Vector3d shipPosition,
            Vector3d farPoint, double farSurfaceValue)
        {
            Dispose();
            pqs = pqsController;
            body = planet;
            up = (farPoint - planet.position).normalized;
            gravityInfo = "gravitation=" + Physics.gravity.ToString("0.00");

            TryBuildNear(shipPosition);
            FarRoot = BuildPatch(farPoint, false);

            StringBuilder text = new StringBuilder();
            text.Append("patchNah=").Append(NearRoot == null ? "kein Land in 3-15 km" : "ok");
            if (NearRoot != null) text.Append(" hoehe=").Append(nearSurface.ToString("0.0"));
            text.Append(" patchFern=").Append(FarRoot == null ? "fehlgeschlagen" : "ok");
            text.Append(" hoehe=").Append(farSurfaceValue.ToString("0.0"));
            text.Append(" ").Append(gravityInfo);
            Info = text.ToString();
            SpawnBall();
        }

        // Was die Landsuche an den acht Punkten gemessen hat, damit "kein Land" nachpruefbar ist.
        public string NearSearch = "";
        // Wahr, sobald die Kugel nachweislich auf dem Patch ruht.
        public bool Resting;

        // Sucht fuer den Formvergleich einen Punkt ueber dem Meeresspiegel, statt blind 3 km
        // daneben zu bauen - dort liegt sonst der Meeresboden und die Kamera steht unter Wasser.
        // Wird waehrend des Fluges wiederholt, bis Land in Reichweite kommt.
        public bool TryBuildNear(Vector3d shipPosition)
        {
            if (NearRoot != null) return true;
            if (pqs == null || body == null) return false;
            double[] distances = new double[] { 3000, 6000, 10000, 15000 };
            StringBuilder samples = new StringBuilder();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < distances.Length; i++)
                    {
                        Vector3d radial = (shipPosition - body.position).normalized;
                        Vector3d axis = body.transform.up.normalized;
                        Vector3d side = Vector3d.Cross(axis, radial).normalized;
                        double sign = pass == 0 ? 1.0 : -1.0;
                        double angle = sign * distances[i] / body.Radius;
                        Vector3d direction = (radial * Math.Cos(angle) + side * Math.Sin(angle)).normalized;
                        double radius = pqs.GetSurfaceHeight(direction);
                        if (double.IsNaN(radius) || radius <= 0) continue;
                        double height = radius - body.Radius;
                        samples.Append((sign > 0 ? "+" : "-")).Append((distances[i] / 1000).ToString("0"))
                            .Append("km=").Append(height.ToString("0")).Append("m ");
                        if (height < 20.0) continue;
                        nearSurface = height;
                        NearRoot = BuildPatch(body.position + direction * radius, true);
                        if (NearRoot != null)
                        {
                            NearSearch = samples.ToString();
                            return true;
                        }
                    }
                }
            }
            catch (Exception e) { NearSearch = "Suche fehlgeschlagen: " + e.Message; }
            NearSearch = samples.ToString();
            return false;
        }

        // surfacePoint liegt bereits auf der Oberflaeche; hier wird nur noch das Netz gebaut.
        private GameObject BuildPatch(Vector3d surfacePoint, bool near)
        {
            try
            {
                Vector3d center = surfacePoint;
                Vector3d pointUp = (center - body.position).normalized;
                Vector3d north = Vector3.ProjectOnPlane(body.transform.up, pointUp).normalized;
                if (north.sqrMagnitude < 0.5f) north = Vector3.ProjectOnPlane(Vector3.right, pointUp).normalized;
                Vector3d east = Vector3d.Cross(north, pointUp).normalized;

                GameObject root = new GameObject(near ? "PSR_PatchNah" : "PSR_PatchFern");
                root.transform.SetParent(pqs.transform, false);
                root.transform.localPosition = pqs.transform.InverseTransformPoint((Vector3)center);
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                int n = Resolution + 1;
                Vector3[] vertices = new Vector3[n * n];
                Vector2[] uv = new Vector2[n * n];
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        double u = (i / (double)Resolution - 0.5) * 2.0 * HalfSize;
                        double v = (j / (double)Resolution - 0.5) * 2.0 * HalfSize;
                        Vector3d world = center + east * u + north * v;
                        Vector3d direction = (world - body.position).normalized;
                        double height = pqs.GetSurfaceHeight(direction);
                        Vector3d point = body.position + direction * height;
                        vertices[j * n + i] = root.transform.InverseTransformPoint((Vector3)point);
                        uv[j * n + i] = new Vector2(i / (float)Resolution, j / (float)Resolution);
                    }
                }
                int[] triangles = new int[Resolution * Resolution * 6];
                int t = 0;
                for (int j = 0; j < Resolution; j++)
                {
                    for (int i = 0; i < Resolution; i++)
                    {
                        int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                        triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                        triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                    }
                }

                Mesh mesh = new Mesh();
                mesh.name = root.name + ".Mesh";
                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                GameObject go = new GameObject(root.name + ".Mesh");
                go.transform.SetParent(root.transform, false);
                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = TestMaterial();
                int layer = LayerMask.NameToLayer("Local Scenery");
                if (layer < 0) layer = 15;
                go.layer = layer;
                root.layer = layer;
                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                return root;
            }
            catch (Exception e)
            {
                Info = "Patch fehlgeschlagen: " + e.Message;
                return null;
            }
        }

        // Grelles, unbeleuchtetes Material: die Kugel soll im Bild sofort auffallen.
        private static Material MarkerMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("KSP/Diffuse");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            Material material = shader == null ? null : new Material(shader);
            if (material != null)
            {
                material.color = new Color(0.1f, 1f, 1f, 1f);
                material.name = "PSR_BallMarker";
            }
            return material;
        }

        // Auffaelliges Magenta statt Gelaendetextur: im Kamerabild ist sofort zu sehen, wo
        // unser Netz liegt und ob es sich mit dem echten Gelaende deckt.
        private static Material TestMaterial()
        {
            Shader shader = Shader.Find("KSP/Diffuse");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Diffuse");
            Material material = shader == null ? null : new Material(shader);
            if (material != null)
            {
                material.color = new Color(1f, 0f, 0.8f, 1f);
                material.name = "PSR_PatchTest";
            }
            return material;
        }

        // Testkugel auf der Ebene der Schiffsteile, die auch ein landender Booster benutzt. Sie
        // haengt als Kind des Patches unter PQS und macht damit jede Floating-Origin-Verschiebung
        // mit. Die Fallbeschleunigung bekommt sie von uns: KSP setzt die globale Gravitation auf
        // null und rechnet sie nur fuer eigene Fahrzeuge.
        private void SpawnBall()
        {
            try
            {
                if (FarRoot == null) return;
                int layer = PartsLayer();
                int patchLayer = LayerMask.NameToLayer("Local Scenery");
                if (patchLayer < 0) patchLayer = 15;
                Info += " kugelEbene=" + layer + "(" + LayerMask.LayerToName(layer) + ")"
                    + " patchEbene=" + patchLayer
                    + " ignorieren=" + Physics.GetIgnoreLayerCollision(layer, patchLayer)
                    + " " + LayerInventory();
                Ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Ball.name = "PSR_TestBall";
                UnityEngine.Object.Destroy(Ball.GetComponent<Collider>());
                SphereCollider collider = Ball.AddComponent<SphereCollider>();
                collider.radius = (float)BallRadius;
                BallBody = Ball.AddComponent<Rigidbody>();
                BallBody.mass = 5000f;
                BallBody.drag = 0f;
                BallBody.angularDrag = 0.05f;
                BallBody.useGravity = false;
                Ball.layer = layer;
                Ball.transform.localScale = Vector3.one * (float)(BallRadius * 2.0);
                MeshRenderer renderer = Ball.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = MarkerMaterial();
                Vector3d center = FarCenter;
                Vector3d freshUp = (center - body.position).normalized;
                Vector3d side = Vector3d.Cross(body.transform.up.normalized, freshUp).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3d.Cross(Vector3.right, freshUp).normalized;
                Ball.transform.position = (Vector3)(center + side * BallOffset + freshUp * BallHeight);
                Ball.transform.SetParent(FarRoot.transform, true);
                Info += " kugelRadius=" + BallRadius.ToString("0") + "m";
            }
            catch (Exception e) { Info += " Kugel fehlgeschlagen: " + e.Message; }
        }

        public void PhysicsTick(float dt)
        {
            if (BallBody == null || body == null) return;
            try
            {
                Vector3d position = Ball.transform.position;
                Vector3d down = (body.position - position).normalized;
                double gravity = body.GeeASL * 9.80665;
                BallBody.AddForce((Vector3)down * (float)(gravity * BallBody.mass), ForceMode.Force);
            }
            catch { }
        }

        // Ebene der Teile-Kollider des aktiven Schiffes. Wichtig: die Trigger-Kollider liegen auf
        // einer eigenen Ebene und erzeugen keine Physikkontakte - die Testkugel muss auf die
        // Ebene eines echten Kolliders.
        private static int PartsLayer()
        {
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null && v.parts != null)
                {
                    // Erste Wahl: der Hauptkollider eines Teils.
                    foreach (Part part in v.parts)
                    {
                        if (part.collider == null || part.collider.isTrigger) continue;
                        return part.collider.gameObject.layer;
                    }
                    foreach (Part part in v.parts)
                        foreach (Collider collider in part.GetPartColliders())
                            if (collider != null && !collider.isTrigger) return collider.gameObject.layer;
                }
            }
            catch { }
            return 0;
        }

        // Alle Ebenen der Schiffskollider mit Trigger-Kennzeichen - damit im Log steht, worauf
        // eine Landung tatsaechlich trifft.
        private static string LayerInventory()
        {
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || v.parts == null) return "";
                Dictionary<string, int> counts = new Dictionary<string, int>();
                foreach (Part part in v.parts)
                {
                    if (part.collider != null)
                        Count(counts, part.collider);
                    foreach (Collider collider in part.GetPartColliders())
                        if (collider != null) Count(counts, collider);
                }
                StringBuilder text = new StringBuilder("ebenen=");
                foreach (KeyValuePair<string, int> entry in counts)
                    text.Append("[").Append(entry.Key).Append(" x").Append(entry.Value).Append("]");
                return text.ToString();
            }
            catch { return ""; }
        }

        private static void Count(Dictionary<string, int> counts, Collider collider)
        {
            string key = collider.gameObject.layer + "(" + LayerMask.LayerToName(collider.gameObject.layer) + ")"
                + (collider.isTrigger ? " Trigger" : "");
            int value;
            counts[key] = counts.TryGetValue(key, out value) ? value + 1 : 1;
        }

        // Vergleicht unser Hoehenfeld mit dem Netz, das KSP selbst gebaut hat - dort, wo KSP
        // welches hat (naher Patch ueber Land). Damit braucht die Formpruefung kein Auge.
        public string NearMeshCheck()
        {
            if (NearRoot == null) return "kein Land in 3-15 km";
            try
            {
                Vector3d center = NearCenter;
                Vector3d freshUp = (center - body.position).normalized;
                Vector3d from = center + freshUp * 5000.0;
                double distance = 0;
                if (!pqs.RayIntersection((Vector3)from, (Vector3)(-freshUp), out distance)) return "kein KSP-Netz";
                double meshAltitude = (from - body.position).magnitude - body.Radius - distance;
                double surface = SurfaceUnder(center);
                return "kspHoehe=" + meshAltitude.ToString("0.0") + " unsere=" + surface.ToString("0.0")
                    + " fehler=" + (meshAltitude - surface).ToString("0.0");
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }

        public void ResetBall()
        {
            if (BallBody == null) return;
            BallBody.velocity = Vector3.zero;
            BallBody.angularVelocity = Vector3.zero;
            Vector3d center = FarCenter;
            Vector3d freshUp = (center - body.position).normalized;
            Vector3d side = Vector3d.Cross(body.transform.up.normalized, freshUp).normalized;
            if (side.sqrMagnitude < 0.5f) side = Vector3d.Cross(Vector3.right, freshUp).normalized;
            Ball.transform.position = (Vector3)(center + side * BallOffset + freshUp * BallHeight);
        }

        // Jede Sekunde: wo ist die Kugel, und steht sie auf unserem Patch? Gemeldet wird der
        // Spalt unter der Kugel, also Hoehe minus Oberflaeche minus Radius.
        public string BallReport()
        {
            if (Ball == null) return "keine";
            try
            {
                Vector3d position = Ball.transform.position;
                double altitude = (position - body.position).magnitude - body.Radius;
                double surface = SurfaceUnder(position);
                double speed = BallBody == null ? 0 : BallBody.velocity.magnitude;
                double gap = altitude - surface - BallRadius;
                string state = gap < 3.0 && speed < 3.0 ? "liegt auf dem Patch"
                    : gap < 3.0 ? "auf dem Patch, rutscht" : "faellt noch";
                Resting = gap < 3.0 && speed < 3.0;
                return "spalt=" + gap.ToString("0.0") + "m hoehe=" + altitude.ToString("0.0")
                    + "m v=" + speed.ToString("0.0") + " " + state;
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }

        // Kurze Physiksonde direkt ueber dem fernen Patch: findet sie unseren eigenen Collider?
        public string PatchRay()
        {
            if (FarRoot == null) return "kein Patch";
            try
            {
                Vector3d center = FarCenter;
                Vector3d freshUp = (center - body.position).normalized;
                // Seitlich versetzt messen, damit die Kugel ueber der Mitte nicht im Weg ist.
                Vector3d side = Vector3d.Cross(body.transform.up.normalized, freshUp).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3d.Cross(Vector3.right, freshUp).normalized;
                Vector3d measured = center + side * RayOffset;
                Vector3d from = measured + freshUp * 60.0;
                RaycastHit[] hits = new RaycastHit[16];
                int count = Physics.RaycastNonAlloc((Vector3)from, (Vector3)(-freshUp), hits, 400f, ~0,
                    QueryTriggerInteraction.Ignore);
                string nearest = "keins";
                double nearestDistance = double.PositiveInfinity;
                double nearestAltitude = double.NaN;
                for (int i = 0; i < count && i < hits.Length; i++)
                {
                    Collider collider = hits[i].collider;
                    if (collider == null) continue;
                    if (hits[i].distance < nearestDistance)
                    {
                        nearestDistance = hits[i].distance;
                        nearest = collider.gameObject.name + "/" + LayerMask.LayerToName(collider.gameObject.layer);
                        nearestAltitude = (from - body.position).magnitude - body.Radius - hits[i].distance;
                    }
                }
                if (double.IsPositiveInfinity(nearestDistance)) return "keins";
                return nearest + " hoehe=" + nearestAltitude.ToString("0.0")
                    + " fehler=" + (nearestAltitude - SurfaceUnder(measured)).ToString("0.0");
            }
            catch (Exception e) { return "Fehler " + e.Message; }
        }
    }
}
