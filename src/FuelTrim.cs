using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterWatch
{
    // Trimmtank: waehrend die Stufe gleitet, wird ihr Treibstoff in den Tank gepumpt, der am
    // weitesten vom Triebwerk weg liegt.
    //
    // Rueckwaerts fliegend liegt das Triebwerk vorn. Je weiter der Schwerpunkt nach vorn (zum
    // Triebwerk) rueckt, desto staerker richtet die Luft die Stufe wieder genau rueckwaerts aus -
    // und desto weniger Anstellwinkel schaffen die Steuerflaechen. Flug vom 25.09.2026, 22:39:
    // 35 Grad befohlen, 10 bis 14 erreicht, alle Flaechen am Anschlag. KSP leert einen Tankstapel
    // von oben, die Reste liegen also im unteren Tank direkt am Triebwerk. Nach oben gepumpt
    // wandert der Schwerpunkt um einige Meter zu den Flaechen hin, die Luft haelt weniger dagegen.
    //
    // Gepumpt wird nur zwischen Tanks, deren Treibstoff das Triebwerk selbst nutzt, nur Ressourcen,
    // die fliessen duerfen, und nur mit PumpRate - so schnell wie KSPs eigene Umpumpfunktion
    // ungefaehr. Die Summe bleibt gleich; der Landevorhalt zaehlt ueber alle eigenen Tanks.
    public sealed class FuelTrim
    {
        // Tonnen pro Sekunde.
        public const double PumpRate = 1.5;
        public double Moved { get; private set; }
        public double Shift { get; private set; }
        private bool reported;

        // Einmal pro Physikschritt, solange gepumpt werden soll. Wirft nie.
        public void Pump(Vessel vessel, IEnumerable<ModuleEngines> engines, double dt, string id)
        {
            try
            {
                if (vessel == null || dt <= 0) return;
                Transform reference = vessel.ReferenceTransform;
                if (reference == null) return;
                Vector3d nose = reference.up;
                Vector3d com = vessel.CoMD;
                var ids = new HashSet<int>();
                foreach (ModuleEngines engine in engines)
                    foreach (Propellant p in engine.propellants)
                        if (p.id != 0 && !p.ignoreForIsp) ids.Add(p.id);
                if (ids.Count == 0) return;
                double budget = PumpRate * dt;
                double movedNow = 0, shiftNow = 0;
                foreach (int resourceId in ids)
                {
                    PartResourceDefinition definition = PartResourceLibrary.Instance.GetDefinition(resourceId);
                    if (definition == null || definition.density <= 0) continue;
                    // Tanks dieser Ressource, nach Lage entlang der Achse.
                    PartResource low = null, high = null;
                    double lowPos = double.PositiveInfinity, highPos = double.NegativeInfinity;
                    foreach (Part part in vessel.parts)
                    {
                        PartResource r = part.Resources.Get(resourceId);
                        if (r == null || !r.flowState || r.maxAmount <= 0) continue;
                        double pos = Vector3d.Dot((Vector3d)part.transform.position - com, nose);
                        if (r.amount > 1e-6 && pos < lowPos) { lowPos = pos; low = r; }
                        if (r.maxAmount - r.amount > 1e-6 && pos > highPos) { highPos = pos; high = r; }
                    }
                    if (low == null || high == null || low == high || highPos - lowPos < 0.5) continue;
                    // Anteil am Budget nach Masse, damit Brennstoff und Oxidator zusammen wandern.
                    double units = Math.Min(budget / ids.Count / definition.density,
                        Math.Min(low.amount, high.maxAmount - high.amount));
                    if (units <= 0) continue;
                    low.amount -= units; high.amount += units;
                    double tonnes = units * definition.density;
                    movedNow += tonnes; shiftNow += tonnes * (highPos - lowPos);
                }
                Moved += movedNow;
                double mass = Math.Max(0.001, vessel.GetTotalMass());
                Shift += shiftNow / mass;
                if (movedNow <= 0 && Moved > 0 && !reported)
                {
                    reported = true;
                    Debug.Log("[PhysStageRecovery] Trimmtank " + id + ": " + Moved.ToString("0.0")
                        + " t Treibstoff vom Triebwerk weg gepumpt, Schwerpunkt ~" + Shift.ToString("0.0")
                        + " m Richtung Steuerflaechen.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Trimmtank: " + e.Message);
            }
        }
    }
}
