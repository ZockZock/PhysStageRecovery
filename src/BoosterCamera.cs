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
        private float nextFrame;
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

        public void Render(Vessel target, int fps)
        {
            if (target == null || !target.loaded || target.packed || Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 1f / fps;
            try
            {
                if (local == null) Create();
                if (local == null) return;
                Vector3 up = (target.CoM - target.mainBody.position).normalized;
                Vector3 north = Vector3.ProjectOnPlane(target.mainBody.transform.up, up).normalized;
                if (north.sqrMagnitude < 0.1f) north = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                Vector3 side = Quaternion.AngleAxis(Heading, up) * north;
                Vector3 aim = target.CoM + up * 5;
                float pitch = Pitch * Mathf.Deg2Rad;
                Vector3 position = aim + (side * Mathf.Cos(pitch) + up * Mathf.Sin(pitch)) * Distance;
                Quaternion rotation = Quaternion.LookRotation(aim - position, up);
                local.transform.SetPositionAndRotation(position, rotation);
                if (scaled != null)
                {
                    scaled.transform.SetPositionAndRotation((Vector3)ScaledSpace.LocalToScaledSpace(position), rotation);
                    scaled.Render();
                }
                local.Render();
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
            texture = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
            texture.name = "PhysStageRecovery.Feed";
            texture.antiAliasing = 1;
            texture.Create();
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
            Debug.Log("[PhysStageRecovery] Independent camera created (640x360).");
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
            camera.aspect = 16f / 9f;
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
