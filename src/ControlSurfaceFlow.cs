using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // Steuerflaechen im Rueckwaertsflug.
    //
    // KSP rechnet den Ausschlag einer Steuerflaeche nur aus ihrer Lage zum Schwerpunkt und dem
    // Steuerbefehl (ModuleControlSurface.FixedCtrlSurfaceUpdate) - woher die Luft kommt, spielt
    // keine Rolle. Faellt der Booster mit dem Triebwerk voran, kommt die Luft von hinten: derselbe
    // Ausschlag erzeugt dann die entgegengesetzte Kraft, und jede Flaeche arbeitet gegen
    // Reaktionsrad und Schwenkduese. Beim Aufstieg stimmt die Richtung, beim Abstieg ist sie
    // seitenverkehrt.
    //
    // Abhilfe: solange die Luft von hinten kommt, wird der Stellbereich (authorityLimiter, in KSP
    // selbst von -150 bis +150 einstellbar) jeder Flaeche mit umgekehrtem Vorzeichen gesetzt, danach
    // der Originalwert zurueck. Nur an getrackten Boostern, nie am Schiff, das der Spieler fliegt.
    // Luftbremsen (ModuleAeroSurface) bleiben unberuehrt: sie werden ausgefahren, nicht gesteuert.
    public sealed class ControlSurfaceFlow
    {
        private readonly Dictionary<ModuleControlSurface, float> originals = new Dictionary<ModuleControlSurface, float>();
        public bool Reversed { get; private set; }
        public int Count { get { return originals.Count; } }

        // Einmal pro Messung. Wirft nie: laeuft in der Flugschleife.
        public void Update(Vessel vessel, bool enabled, string id)
        {
            try
            {
                if (vessel == null || vessel.parts == null) return;
                if (!enabled) { Restore(); return; }
                Vector3d velocity = vessel.srf_velocity;
                double speed = velocity.magnitude;
                double along = speed > 1e-3 && vessel.ReferenceTransform != null
                    ? Vector3d.Dot((Vector3d)vessel.ReferenceTransform.up, velocity / speed) : double.NaN;
                bool reversed = ControlSurfacePolicy.Reversed(Reversed, speed, along);
                bool changed = reversed != Reversed;
                Reversed = reversed;
                int count = 0;
                foreach (Part part in vessel.parts)
                {
                    if (part == null) continue;
                    foreach (ModuleControlSurface surface in part.FindModulesImplementing<ModuleControlSurface>())
                    {
                        if (surface is ModuleAeroSurface) continue;
                        float original;
                        if (!originals.TryGetValue(surface, out original))
                        {
                            original = ControlSurfacePolicy.Original(surface.authorityLimiter, reversed);
                            originals[surface] = original;
                        }
                        float wanted = ControlSurfacePolicy.Authority(original, reversed);
                        if (surface.authorityLimiter != wanted) surface.authorityLimiter = wanted;
                        count++;
                    }
                }
                if (changed && count > 0)
                    Debug.Log("[PhysStageRecovery] Steuerflaechen " + id + ": " + count + " Flaeche(n) "
                        + (reversed ? "umgekehrt (Luft von hinten, Rueckwaertsflug)" : "wieder normal (Luft von vorn)")
                        + ", " + speed.ToString("0") + " m/s.");
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Steuerflaechen: " + e.Message);
            }
        }

        // Originalwerte zurueck, auch fuer Flaechen, die inzwischen an einem anderen Schiff haengen.
        public void Restore()
        {
            foreach (KeyValuePair<ModuleControlSurface, float> entry in originals)
                if (entry.Key != null) entry.Key.authorityLimiter = entry.Value;
            originals.Clear();
            Reversed = false;
        }
    }
}
