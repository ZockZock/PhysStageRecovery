using System;
using BoosterWatch.Guidance;
using UnityEngine;

namespace BoosterWatch
{
    // Die Luft, wie KSPs FlightIntegrator sie fuer die Erwaermung rechnet (CalculateConstantsAtmosphere,
    // CalculateConvectiveCoefficient, CalculateShockTemperature), fuer eine beliebige Hoehe voraus.
    // Die Dichte kommt aus dem Landeautomaten (an der gemessenen Dichte ausgerichtet).
    public sealed class KspEntryAtmosphere : IEntryAtmosphere
    {
        private readonly CelestialBody body;
        private readonly Func<double, double> density;
        // Druck und Temperatur alle 100 m, einmal gerechnet: die Vorausrechnung fragt die Luft
        // zehntausende Male pro Plan ab.
        private const double Step = 100;
        private readonly double[] pressures, temperatures;
        public KspEntryAtmosphere(CelestialBody body, Func<double, double> densityAtAltitude)
        {
            this.body = body; density = densityAtAltitude;
            int count = body != null && body.atmosphere ? (int)(body.atmosphereDepth / Step) + 2 : 0;
            pressures = new double[count]; temperatures = new double[count];
            for (int i = 0; i < count; i++)
            {
                double altitude = Math.Min(i * Step, body.atmosphereDepth);
                pressures[i] = body.GetPressure(altitude); temperatures[i] = body.GetTemperature(altitude);
            }
        }
        private static double Sample(double[] table, double altitude)
        {
            if (table.Length == 0) return 0;
            double x = Math.Max(0, altitude) / Step;
            int i = (int)x;
            if (i + 1 >= table.Length) return table[table.Length - 1];
            return table[i] + (table[i + 1] - table[i]) * (x - i);
        }

        public void At(double altitude, double speed, out double rho, out double convection, out double shock)
        {
            rho = 0; convection = 0; shock = 0;
            if (body == null || !body.atmosphere || altitude >= body.atmosphereDepth) return;
            rho = Math.Max(0, density(altitude));
            double pressure = Sample(pressures, altitude);
            double temperature = Sample(temperatures, altitude);
            double sound = rho > 0 ? body.GetSpeedOfSound(pressure, rho) : 0;
            double mach = sound > 0 ? speed / sound : 0;
            double lerp = Math.Pow(Clamp01((mach - PhysicsGlobals.NewtonianMachTempLerpStartMach)
                / (PhysicsGlobals.NewtonianMachTempLerpEndMach - PhysicsGlobals.NewtonianMachTempLerpStartMach)),
                PhysicsGlobals.NewtonianMachTempLerpExponent);
            double newtonian = (rho > 1 ? rho : Math.Pow(rho, PhysicsGlobals.NewtonianDensityExponent))
                * (PhysicsGlobals.NewtonianConvectionFactorBase + Math.Pow(speed, PhysicsGlobals.NewtonianVelocityExponent))
                * PhysicsGlobals.NewtonianConvectionFactorTotal;
            double machConvection = 1e-7 * PhysicsGlobals.MachConvectionFactor
                * (rho > 1 ? rho : Math.Pow(rho, PhysicsGlobals.MachConvectionDensityExponent))
                * Math.Pow(speed, PhysicsGlobals.MachConvectionVelocityExponent);
            convection = (lerp == 0 ? newtonian : lerp == 1 ? machConvection : newtonian + (machConvection - newtonian) * lerp)
                * body.convectionMultiplier;
            double shockTemperature = speed * PhysicsGlobals.NewtonianTemperatureFactor;
            if (lerp > 0)
                shockTemperature += (PhysicsGlobals.MachTemperatureScalar
                    * Math.Pow(speed, PhysicsGlobals.MachTemperatureVelocityExponent) - shockTemperature) * lerp;
            double scale = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Parameters.Difficulty.ReentryHeatScale : 1;
            shock = Math.Max(temperature, shockTemperature * scale * body.shockTemperatureMultiplier);
            if (!Finite(convection) || convection < 0) convection = 0;
            if (!Finite(shock)) shock = temperature;
        }
        private static double Clamp01(double x) { return double.IsNaN(x) ? 0 : x < 0 ? 0 : x > 1 ? 1 : x; }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }

    // Die KSP-Seite des Wiedereintrittsburns: sammelt die Messwerte fuer EntryBurnPolicy, sagt dem
    // Landeautomaten, ob er gegen die Bahn halten und schieben soll, und schreibt ins Log.
    public sealed class EntryBurn
    {
        private EntryBurnPolicy policy;
        private CelestialBody body;
        private bool wasActive;
        private string id = "";
        private double nextLog;

        public bool Active { get { return policy != null && policy.Active; } }
        public double Throttle { get; private set; }
        public EntryBurnPolicy Policy { get { return policy; } }

        // Widerstand pro rho*v^2 beim Gleiten und rueckwaerts, zuletzt gemessen.
        private double glideDrag = double.NaN, retroDrag = double.NaN, glideSince = double.NaN;
        // So lange nach Beginn des Gleitens wird der Widerstand noch nicht als Gleitwert genommen: die
        // Stufe dreht erst in den Anstellwinkel (Flug vom 26.09.2026: Cd*A 0,9 -> 5 in 6 s).
        private const double GlideSettleSeconds = 6;

        public void Step(Vessel vessel, Settings settings, HeatGuard heat, DescentAdapter adapter, DescentGuidance descent,
            DescentConfig config, GuidanceStep step, double thrustAcceleration)
        {
            Throttle = 0;
            if (vessel == null || heat == null || adapter == null) return;
            if (policy == null || body != vessel.mainBody)
            {
                body = vessel.mainBody;
                id = vessel.id.ToString();
                DescentAdapter source = adapter;
                policy = new EntryBurnPolicy(new KspEntryAtmosphere(body, h => source.AirDensity(h)));
            }
            heat.Measure(vessel);
            double now = Planetarium.GetUniversalTime();
            Vector3d velocity = vessel.srf_velocity;
            double speed = velocity.magnitude;
            Vector3d up = (vessel.CoMD - body.position).normalized;
            double vu = Vector3d.Dot(velocity, up);
            double vh = Math.Sqrt(Math.Max(0, speed * speed - vu * vu));
            double rho = adapter.AirDensityNow(vessel);
            double dragPer = rho > 0 && speed > 50 && adapter.DragAcceleration > 0
                ? adapter.DragAcceleration / (rho * speed * speed) : double.NaN;
            double aimNow = speed > 1 ? Vector3d.Angle((Vector3d)vessel.ReferenceTransform.up, -velocity) : double.NaN;
            bool gliding = step.Valid && step.Gliding;
            if (!gliding) glideSince = double.NaN;
            else if (double.IsNaN(glideSince)) glideSince = now;
            if (Finite(dragPer))
            {
                if (gliding && now - glideSince >= GlideSettleSeconds)
                    glideDrag = Finite(glideDrag) ? glideDrag + (dragPer - glideDrag) * 0.1 : dragPer;
                else if (!gliding && aimNow < 5)
                    retroDrag = Finite(retroDrag) ? retroDrag + (dragPer - retroDrag) * 0.1 : dragPer;
            }
            // Gleitet die Stufe gleich (oder gerade erst), entscheidet das Modell noch nicht: mit dem
            // Rueckwaerts-Widerstand saehe es sie viel zu schnell. Flug vom 26.09.2026, 08:52: Burn bei
            // 53,8 km fuer 600 m/s, weil die Vorhersage mit Cd*A 0,7 rechnete - gegleitet wurde mit 6.
            bool glideComing = config != null && config.Glide.AngleDegrees > 0 && descent != null
                && descent.GlideEndReason.Length == 0 && !Finite(glideDrag)
                && (gliding || rho < 1.5 * config.Glide.MinimumDensity);
            double coastDrag = Finite(glideDrag) && descent != null && descent.GlideEndReason.Length == 0 ? glideDrag : dragPer;
            if (policy.Active && !Finite(glideDrag)) coastDrag = double.NaN;
            DescentPrediction plan = descent != null ? descent.LastPrediction : null;
            double landing = plan != null && plan.Valid && !plan.Unstoppable && !plan.UsedBestEffort ? plan.RequiredDeltaV : double.NaN;
            double aim = speed > 1 ? Vector3d.Angle((Vector3d)vessel.ReferenceTransform.up, -velocity) : double.NaN;
            bool allowed = settings.EntryBurn && HeatPolicy.Destructive(settings.HeatMode)
                && step.Valid && step.Phase == DescentPhase.Entry && descent != null && !descent.IgnitionOrdered
                && vu < 0 && thrustAcceleration > 0;
            Throttle = policy.Update(new EntryBurnPolicy.Flight
            {
                Time = now, Ratio = heat.ReentryRatio, Limit = heat.ReentryLimit,
                Altitude = vessel.altitude, VelocityUp = vu, VelocityHorizontal = vh,
                Gravity = body.gravParameter / (vessel.CoMD - body.position).sqrMagnitude,
                DragPerRhoV2 = policy.Active && Finite(glideDrag) ? glideDrag : coastDrag,
                BurnDragPerRhoV2 = Finite(retroDrag) ? retroDrag : double.NaN, Wait = glideComing,
                ThrustAcceleration = thrustAcceleration,
                AvailableDeltaV = adapter.AvailableDeltaV, LandingDeltaV = landing, AimError = aim, Allowed = allowed
            });
            if (policy.Active != wasActive)
            {
                wasActive = policy.Active;
                Debug.Log("[PhysStageRecovery] Eintrittsburn " + id + (policy.Active ? " an: " : " aus: ") + policy.Reason
                    + " bei " + (vessel.altitude / 1000).ToString("0.0") + " km, " + speed.ToString("0") + " m/s, Hitze "
                    + Percent(heat.ReentryRatio) + " (" + HeatPolicy.Key(settings.HeatMode) + "), Spitze voraus "
                    + Percent(policy.Predicted) + ", bisher " + policy.Spent.ToString("0") + " m/s, Vorrat "
                    + adapter.AvailableDeltaV.ToString("0") + " m/s, Vorhalt " + policy.Floor.ToString("0") + " m/s.");
            }
            // Solange es in der Luft heiss wird, alle 5 s eine Zeile mit dem gelernten Modell.
            if (allowed && heat.ReentryRatio > 0.3 && now >= nextLog)
            {
                nextLog = now + 5;
                Debug.Log("[PhysStageRecovery] Hitze " + id + ": " + Percent(heat.ReentryRatio) + " (" + heat.HottestPart
                    + "), Anstieg " + (100 * policy.Trend).ToString("0.0") + " %/s, Spitze voraus " + Percent(policy.Predicted)
                    + (policy.ModelReady ? ", Modell a=" + policy.HeatGain.ToString("E2") + " b=" + policy.Cooling.ToString("0.000")
                        : ", Modell lernt noch")
                    + ", " + (vessel.altitude / 1000).ToString("0.0") + " km, " + speed.ToString("0") + " m/s"
                    + (policy.Active ? ", BURN" : ""));
            }
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        private static string Percent(double x)
        { return double.IsNaN(x) || double.IsInfinity(x) ? "--" : (100 * x).ToString("0") + " %"; }
    }
}
