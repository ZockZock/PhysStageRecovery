using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // Solange das Kamerafenster kein Bild hat (Zeitraffer auf Schienen, Booster noch ohne Physik),
    // zeigt es die Missionskontrolle: einen Kerbal aus dem Spiel selbst, der versucht, die Verbindung
    // zum Booster wiederherzustellen. Er schaut sich um, wundert sich, schuettelt den Kopf, seufzt -
    // dann klettern die Signalbalken, Daumen hoch - und das Signal ist wieder weg.
    //
    // Die Figur ist KSPs eigener Tutorial-Sprecher (KerbalInstructor, AssetBase-Prefab "Instructor_Gene",
    // wie in den Tutorials), mit dessen eigener Kamera in eine RenderTexture gefilmt. Nichts davon ist im
    // Mod gezeichnet oder mitgeliefert: fehlt das Prefab, bleibt es bei ControlRoomAnimation.
    public sealed class ReconnectScene
    {
        // Wer vor der Kamera sitzt. Gene Kerman leitet die Missionskontrolle.
        public static readonly string[] Candidates = { "Instructor_Gene", "Instructor_Wernher" };
        public const float LoopSeconds = 14;

        private GameObject instance;
        private KerbalInstructor instructor;
        private RenderTexture texture;
        private bool failed, listed;
        private bool? muted;
        private int createdFrame;
        private float lastShown = -1, loopStart;
        private int step = -1;
        public string Name { get; private set; } = "";

        public Texture Texture { get { return texture; } }
        public bool Ready { get { return instructor != null && texture != null && texture.IsCreated(); } }
        // 0-4 Balken, und was gerade passiert: 0 sucht, 1 Signal, 2 wieder weg.
        public int Signal { get; private set; }
        public int Mood { get; private set; }

        // Jedes Bild, solange die Szene sichtbar sein soll. Liefert false, wenn es sie nicht gibt.
        public bool Show(float now, bool voice, int width, int height, bool resizing)
        {
            lastShown = now;
            if (failed) return false;
            if (instance == null && !Create(now, width, height)) return false;
            // Neue Groesse des Kamerafelds: neue RenderTexture - nicht waehrend des Ziehens am Fensterrand.
            if (!resizing && texture != null && (Math.Abs(texture.width - Clamp(width)) > 8 || Math.Abs(texture.height - Clamp(height)) > 8))
                Resize(width, height);
            try
            {
                if (!instructor.instructorCamera.enabled) instructor.instructorCamera.enabled = true;
                // Die Figur bewegt sich in echter Zeit, auch im Physikwarp.
                float scale = Time.timeScale > 0 ? 1f / Time.timeScale : 1f;
                foreach (Animation animation in instance.GetComponentsInChildren<Animation>())
                    foreach (AnimationState state in animation) state.speed = scale;
                foreach (Animator animator in instance.GetComponentsInChildren<Animator>()) animator.speed = scale;
                if (muted != !voice) { muted = !voice; Mute(!voice); }
                // KerbalInstructor richtet seine Animationen erst in Start() ein, ein Bild nach dem Erzeugen.
                if (Time.frameCount > createdFrame + 1) Direct(now);
                return Ready;
            }
            catch (Exception e)
            {
                Fail("Missionskontrolle: " + e.Message);
                return false;
            }
        }

        // Jedes Bild, in dem die Szene nicht gebraucht wird: nach ein paar Sekunden wird sie abgebaut,
        // damit keine zweite Kamera mitlaeuft.
        public void Idle(float now)
        {
            if (instance != null && now - lastShown > 3) Dispose();
        }

        // Regie: eine Geschichte von LoopSeconds, immer wieder.
        private void Direct(float now)
        {
            float t = (now - loopStart) % LoopSeconds;
            int next = t < 3 ? 0 : t < 6 ? 1 : t < 8.5f ? 2 : t < 10 ? 3 : t < 11.5f ? 4 : t < 12.8f ? 5 : 6;
            Signal = next < 4 ? 0 : next == 4 ? Mathf.Clamp(1 + (int)((t - 10) * 2.2f), 1, 4) : next == 5 ? 4 : 0;
            Mood = next == 5 ? 1 : next == 6 ? 2 : 0;
            if (next == step) return;
            step = next;
            switch (next)
            {
                case 0: Emote(instructor.anim_idle_lookAround); break;
                case 1: Emote(instructor.anim_idle_wonder); break;
                case 2: Emote(instructor.anim_false_disagreeA); break;
                case 3: Emote(instructor.anim_idle_sigh); break;
                case 4: Emote(instructor.anim_true_nodA); break;
                case 5: Emote(instructor.anim_true_thumbsUp ?? instructor.anim_true_thumbUp); break;
                default: Emote(instructor.anim_false_disappointed); break;
            }
        }

        private void Emote(CharacterAnimationState state)
        {
            if (state == null || state.clip == null) return;
            instructor.PlayEmote(state);
        }

        private static int Clamp(int size) { return Mathf.Clamp(size, 64, 2048); }

        private void Resize(int width, int height)
        {
            instructor.ClearCamera();
            if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            texture = new RenderTexture(Clamp(width), Clamp(height), 24) { name = "PhysStageRecovery MissionControl" };
            texture.Create();
            instructor.SetupCamera(texture);
        }

        private bool Create(float now, int width, int height)
        {
            try
            {
                ListInstructors();
                GameObject prefab = null;
                foreach (string candidate in Candidates)
                {
                    prefab = AssetBase.GetPrefab(candidate);
                    if (prefab != null && prefab.GetComponent<KerbalInstructor>() != null) { Name = candidate; break; }
                    prefab = null;
                }
                if (prefab == null) { Fail("kein Tutorial-Kerbal gefunden"); return false; }
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.name = "PhysStageRecovery MissionControl";
                instructor = instance.GetComponent<KerbalInstructor>();
                texture = new RenderTexture(Clamp(width), Clamp(height), 24) { name = "PhysStageRecovery MissionControl" };
                texture.Create();
                instructor.SetupCamera(texture);
                loopStart = now; step = -1; createdFrame = Time.frameCount;
                Debug.Log("[PhysStageRecovery] Missionskontrolle: " + Name + " vor der Kamera.");
                return true;
            }
            catch (Exception e)
            {
                Fail("Missionskontrolle: " + e.Message);
                return false;
            }
        }

        // Einmal ins Log, welche Sprecher es gibt - falls eine andere Figur gewuenscht ist.
        private void ListInstructors()
        {
            if (listed) return;
            listed = true;
            try
            {
                var names = new List<string>();
                foreach (KerbalInstructorBase known in Resources.FindObjectsOfTypeAll<KerbalInstructorBase>())
                    if (known != null && !names.Contains(known.gameObject.name)) names.Add(known.gameObject.name);
                Debug.Log("[PhysStageRecovery] Missionskontrolle: verfuegbare Sprecher: "
                    + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "keine"));
            }
            catch (Exception) { }
        }

        // Die Tutorial-Kerbals murmeln bei jeder Geste. AudioSource liegt in einem Unity-Modul, auf das der
        // Mod sonst nicht verweist; deshalb ueber den Namen.
        private void Mute(bool mute)
        {
            foreach (Component audio in instance.GetComponentsInChildren(typeof(Behaviour), true))
            {
                if (audio == null || audio.GetType().Name != "AudioSource") continue;
                var property = audio.GetType().GetProperty("mute");
                if (property != null) property.SetValue(audio, mute, null);
            }
        }

        private void Fail(string reason)
        {
            failed = true;
            Debug.LogWarning("[PhysStageRecovery] " + reason + " - es bleibt beim gezeichneten Kontrollraum.");
            Dispose();
        }

        public void Dispose()
        {
            try
            {
                if (instructor != null) instructor.ClearCamera();
                if (instance != null) UnityEngine.Object.Destroy(instance);
                if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            }
            catch (Exception) { }
            instance = null; instructor = null; texture = null; step = -1; muted = null;
        }
    }
}
