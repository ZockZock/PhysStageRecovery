using System;
using System.Reflection;
using UnityEngine;

namespace BoosterWatch
{
    // Own cameras only. Never moves or reparents the player's flight camera.
    public sealed class BoosterCamera : IDisposable
    {
        private Camera local, scaled;
        private RenderTexture texture;
        // Bildtakt an den Physiktakt gebunden: KSP bewegt Teile nur im FixedUpdate (keine
        // Interpolation). Ein Bild nach Uhrzeit trifft mal zwei, mal drei Physikschritte - bei 20 fps
        // gegen 50 Hz Physik ergibt das genau das Ruckeln des vorbeiziehenden Hintergrunds. Gezaehlt
        // werden deshalb Physikschritte, und jedes Bild zeigt gleich viele davon.
        private int ticksSinceFrame = int.MaxValue / 2;
        // Gewuenschte Bildgroesse = Groesse des Anzeigefelds im Fenster (sonst wird 640x360 auf
        // 1200 Pixel hochgezogen und alles wirkt grob).
        private int wantedWidth = 640, wantedHeight = 360;
        public const int MaxWidth = 1920, MaxHeight = 1080;
        public float Distance = 65;
        public float Heading = 35;
        // Degrees above the horizon. The camera used to have a fixed 12 degree elevation; the mouse
        // control needs a free one.
        public float Pitch = 12;
        public const float MinDistance = 8, MaxDistance = 180, MaxPitch = 80;
        public string Error;
        public bool HasFrame { get; private set; }
        public RenderTexture Texture { get { return texture; } }

        // Mouse control for the feed, the same feel as the stock camera: dragging with the right
        // button moves the view, the wheel changes the distance. Only called while the pointer is
        // over the mod window, so the stock camera keeps its own controls everywhere else.
        // Both axes run opposite to the raw drag so the scene follows the mouse.
        public void Turn(float dx, float dy)
        {
            Heading = Mathf.Repeat(Heading + dx + 180f, 360f) - 180f;
            Pitch = Mathf.Clamp(Pitch - dy, -MaxPitch, MaxPitch);
        }

        public void Zoom(float wheel)
        {
            Distance = Mathf.Clamp(Distance * (1f - wheel * 0.8f), MinDistance, MaxDistance);
        }

        // Aus FixedUpdate: ein Physikschritt ist vergangen.
        public void PhysicsTick() { if (ticksSinceFrame < int.MaxValue / 2) ticksSinceFrame++; }

        // Groesse des Anzeigefelds in Pixeln. Die Textur folgt ihr beim naechsten Bild.
        public void SetViewport(float width, float height)
        {
            if (width < 16 || height < 16) return;
            float scale = Mathf.Min(1f, Mathf.Min(MaxWidth / width, MaxHeight / height));
            wantedWidth = Mathf.Max(320, Mathf.RoundToInt(width * scale));
            wantedHeight = Mathf.Max(180, Mathf.RoundToInt(height * scale));
        }

        public void Render(Vessel target, int fps)
        {
            if (target == null || !target.loaded || target.packed) return;
            // Wie viele Physikschritte ein Bild zeigt: 50 Hz Physik und 25 fps -> jedes zweite.
            float step = Mathf.Max(0.005f, Time.fixedDeltaTime);
            int every = Mathf.Max(1, Mathf.RoundToInt(1f / (Mathf.Max(1, fps) * step)));
            if (ticksSinceFrame < every) return;
            ticksSinceFrame = 0;
            try
            {
                if (local == null) Create();
                if (local == null) return;
                if (texture == null || texture.width != wantedWidth || texture.height != wantedHeight) Resize();
                Vector3 up = (target.CoM - target.mainBody.position).normalized;
                Vector3 north = Vector3.ProjectOnPlane(target.mainBody.transform.up, up).normalized;
                if (north.sqrMagnitude < 0.1f) north = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                Vector3 side = Quaternion.AngleAxis(Heading, up) * north;
                Vector3 aim = target.CoM + up * 5;
                float pitch = Pitch * Mathf.Deg2Rad;
                Vector3 position = aim + (side * Mathf.Cos(pitch) + up * Mathf.Sin(pitch)) * Distance;
                Quaternion rotation = Quaternion.LookRotation(aim - position, up);
                local.transform.SetPositionAndRotation(position, rotation);
                // Gelaende fuer dieses Bild voll einblenden (KSP blendet es nach der Hoehe der
                // Hauptkamera aus) und danach sofort zurueck.
                TerrainDetailBubble.BeginCameraRender(target);
                try
                {
                if (scaled != null)
                {
                    scaled.transform.SetPositionAndRotation((Vector3)ScaledSpace.LocalToScaledSpace(position), rotation);
                    scaled.Render();
                }
                local.Render();
                }
                finally { TerrainDetailBubble.EndCameraRender(); }
                HasFrame = true;
            }
            catch (Exception e)
            {
                Error = Loc.Get("#PSR_Camera_Unavailable");
                Debug.LogError("[PhysStageRecovery] Camera disabled: " + e);
                Dispose();
            }
        }

        private void Create()
        {
            if (FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null) return;
            texture = NewTexture();
            Camera background = null;
            foreach (Camera c in Camera.allCameras)
                if (c.name == "Camera ScaledSpace") background = c;
            if (background != null)
            {
                scaled = NewCamera("Scaled", background);
                scaled.clearFlags = CameraClearFlags.SolidColor;
                scaled.backgroundColor = Color.black;
                scaled.nearClipPlane = 0.01f;
            }
            local = NewCamera("Local", FlightCamera.fetch.mainCamera);
            // Include stock far-camera layers where the platform uses a split camera stack.
            foreach (Camera c in FlightCamera.fetch.cameras)
                if (c != null) local.cullingMask |= c.cullingMask;
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) local.cullingMask &= ~(1 << ui);
            local.clearFlags = scaled != null ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;
            local.backgroundColor = new Color(0.08f, 0.13f, 0.2f);
            local.nearClipPlane = 0.3f;
            local.farClipPlane = 200000;
            Debug.Log("[PhysStageRecovery] Independent camera created (" + texture.width + "x" + texture.height + ").");
        }

        private RenderTexture NewTexture()
        {
            RenderTexture t = new RenderTexture(wantedWidth, wantedHeight, 24, RenderTextureFormat.ARGB32);
            t.name = "PhysStageRecovery.Feed";
            t.antiAliasing = 1;
            t.Create();
            return t;
        }

        // Neue Textur in Fenstergroesse; die Kameras rendern ab jetzt dort hinein.
        private void Resize()
        {
            RenderTexture old = texture;
            texture = NewTexture();
            float aspect = texture.width / (float)texture.height;
            if (local != null) { local.targetTexture = texture; local.aspect = aspect; }
            if (scaled != null) { scaled.targetTexture = texture; scaled.aspect = aspect; }
            if (old != null) { old.Release(); UnityEngine.Object.Destroy(old); }
            HasFrame = false;
        }

        private Camera NewCamera(string name, Camera source)
        {
            GameObject go = new GameObject("PhysStageRecovery." + name);
            Camera camera = go.AddComponent<Camera>();
            try
            {
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = texture.width / (float)texture.height;
            camera.fieldOfView = 48;
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.renderingPath = RenderingPath.Forward;
            // Deferred exposes this component for third-party cameras. No hard dependency.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Deferred") continue;
                Type bridge = assembly.GetType("Deferred.ForwardRenderingCompatibility");
                MethodInfo init = bridge == null ? null : bridge.GetMethod("Init", new[] { typeof(int) });
                if (init == null) break;
                camera.renderingPath = RenderingPath.DeferredShading;
                init.Invoke(go.AddComponent(bridge), new object[] { name == "Local" ? 15 : 10 });
                break;
            }
            return camera;
            }
            catch { UnityEngine.Object.Destroy(go); throw; }
        }

        public void ClearFrame() { HasFrame = false; }
        public void Dispose()
        {
            if (local != null) { UnityEngine.Object.Destroy(local.gameObject); local = null; }
            if (scaled != null) { UnityEngine.Object.Destroy(scaled.gameObject); scaled = null; }
            if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); texture = null; }
            HasFrame = false;
        }
    }
}
