using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoosterWatch
{
    // Haelt das rotierende Bezugssystem, solange ein getrackter Booster in der Atmosphaere fliegt.
    //
    // KSP rechnet den Luftwiderstand jedes Teils aus rb.velocity + Krakensbane-Rahmen
    // (FlightIntegrator.UpdateAerodynamics) - ohne die Drehung des Planeten abzuziehen. Das stimmt
    // nur, solange die Physikwelt mit dem Planeten mitdreht (CelestialBody.inverseRotation). Steigt
    // das AKTIVE Schiff ueber inverseRotThresholdAltitude, schaltet OrbitPhysicsManager auf
    // "Inertial" um. Ab dann steht die Luft fuer jeden geladenen Booster still im Raum, waehrend der
    // Boden sich unter ihm wegdreht: am Schirm bremst er gegen den Raum ab und treibt mit der
    // Rotationsgeschwindigkeit des Planeten (Kerbin: ~175 m/s) seitwaerts ueber den Boden.
    // Im Log vom 25.09.2026: "Reference Frame: Inertial" um 17:27:27, danach horizontal=175.6
    // bei sink=5.7 unter zwei offenen Schirmen, bis der Flug abgebrochen wurde.
    //
    // Stock kennt das Problem nicht, weil dort nur Schiffe im Umkreis von 2,5 km Physik haben - die
    // sind nie gleichzeitig ueber der Schwelle und in der Luft. Die Abhilfe ist deshalb schlicht: so
    // lange ein Booster in der Luft ist, schaltet KSP nicht auf Inertial. Das rotierende System ist
    // physikalisch vollstaendig (VesselPrecalculate.CalculateGravity fuegt Flieh- und Corioliskraft
    // fuer jedes Schiff hinzu); das aktive Schiff fliegt darin genauso richtig wie unter 100 km.
    // Ist kein Booster mehr in der Luft, laeuft KSPs eigene Pruefung wieder und schaltet selbst um.
    public static class RotatingFrameHold
    {
        private const string Id = "PhysStageRecovery.RotatingFrame";
        private static Harmony harmony;
        private static Func<CelestialBody> holdBody;
        private static FieldInfo dominant;
        private static MethodInfo setRotating;
        private static bool holding;

        public static void Install(Func<CelestialBody> bodyToHold)
        {
            Remove();
            holdBody = bodyToHold;
            dominant = AccessTools.Field(typeof(OrbitPhysicsManager), "dominantBody");
            setRotating = AccessTools.Method(typeof(OrbitPhysicsManager), "setRotatingFrame", new[] { typeof(bool) });
            MethodInfo check = AccessTools.Method(typeof(OrbitPhysicsManager), "checkReferenceFrame");
            if (dominant == null || setRotating == null || check == null)
                throw new MissingMethodException("OrbitPhysicsManager.checkReferenceFrame/setRotatingFrame");
            harmony = new Harmony(Id);
            harmony.Patch(check, prefix: new HarmonyMethod(typeof(RotatingFrameHold), nameof(BeforeCheck)));
            Debug.Log("[PhysStageRecovery] Bezugssystem-Halter installiert.");
        }

        public static void Remove()
        {
            holdBody = null;
            holding = false;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }

        // true = KSPs eigene Pruefung laufen lassen, false = sie fuer diesen Frame auslassen.
        public static bool BeforeCheck(OrbitPhysicsManager __instance)
        {
            try
            {
                CelestialBody hold = holdBody == null ? null : holdBody();
                Vessel active = FlightGlobals.ActiveVessel;
                CelestialBody body = dominant.GetValue(__instance) as CelestialBody;
                bool applies = hold != null && active != null && body != null && body == hold && body.rotates
                    && active.orbit != null && active.orbit.referenceBody == body;
                if (!applies)
                {
                    if (holding)
                    {
                        holding = false;
                        Debug.Log("[PhysStageRecovery] Bezugssystem freigegeben: kein Booster mehr in der Luft.");
                    }
                    return true;
                }
                if (!body.inverseRotation)
                {
                    // Schon inertial (Booster erst ueber der Schwelle abgetrennt): zurueck auf rotierend.
                    // setRotatingFrame rechnet die Geschwindigkeiten aller entpackten Schiffe selbst um.
                    setRotating.Invoke(__instance, new object[] { true });
                    Debug.Log("[PhysStageRecovery] Bezugssystem auf rotierend gestellt: Booster in der Luft, aktives Schiff "
                        + active.altitude.ToString("0") + " m.");
                }
                if (!holding && active.altitude > body.inverseRotThresholdAltitude)
                {
                    Debug.Log("[PhysStageRecovery] Bezugssystem bleibt rotierend: aktives Schiff "
                        + active.altitude.ToString("0") + " m ueber der Umschalthoehe "
                        + body.inverseRotThresholdAltitude.ToString("0") + " m, Booster noch in der Luft.");
                    holding = true;
                }
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[PhysStageRecovery] Bezugssystem-Halter abgeschaltet: " + e);
                holdBody = null;
                return true;
            }
        }
    }
}
