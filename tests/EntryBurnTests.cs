// Wiedereintrittsburn gegen ein Waermemodell in der Form, die KSP rechnet: Waermeuebergang
// h = k * rho^0,5 * v^3, Stosstemperatur Ts = 21 * v^0,75, Haut erwaermt mit h * (Ts - T), strahlt mit
// T^4 ab und leitet ins Innere. Die Regel kennt nur die Form von h und Ts, nicht die Konstanten der
// Teile; die lernt sie im Flug. Ein zweites Modell weicht auch in der Form ab.
using System;
using BoosterWatch;

class EntryBurnTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    sealed class Air : IEntryAtmosphere
    {
        public double DensityExponent = 0.5;
        public void At(double altitude, double speed, out double density, out double convection, out double shock)
        {
            density = Density(altitude);
            convection = 2e-6 * Math.Pow(Math.Max(0, density), DensityExponent) * speed * speed * speed;
            shock = 21 * Math.Pow(speed, 0.75);
        }
    }
    static double Density(double h) { return h > 70000 ? 0 : 1.2 * Math.Exp(-h / 5600); }
    static readonly Air Known = new Air();
    static EntryBurnPolicy New() { return new EntryBurnPolicy(Known); }
    static double Gain = 5e-6;

    sealed class Result
    {
        public double Peak, Spent, SpeedAtEnd; public int Burns; public bool Exhausted;
    }

    // speed: Eintrittsgeschwindigkeit bei 70 km, gamma: Bahnneigung unter der Horizontalen [Grad].
    // policy null = kein Burn; earlyDv > 0 = fester Burn gleich bei 70 km (Vergleich).
    // truth: das Modell, mit dem das Teil wirklich erwaermt wird.
    static Result Fly(double speed, double gamma, EntryBurnPolicy policy, double availableDv = 1500,
        double landingDv = double.NaN, double earlyDv = 0, bool allowed = true, Air truth = null, double glideBelow = double.NaN)
    {
        truth = truth ?? Known;
        const double g = 9.81, mass = 20000, thrust = 25, limit = 1500;
        const double retroCda = 1;
        bool glides = !double.IsNaN(glideBelow);
        const double radiate = 2e-13, conduct = 0.02, soak = 0.01;
        double vx = speed * Math.Cos(gamma * Math.PI / 180), vy = -speed * Math.Sin(gamma * Math.PI / 180);
        double h = 70000, skin = 300, inside = 300, dt = 0.02, t = 0, dv = availableDv;
        if (earlyDv > 0)
        {
            double s = Math.Sqrt(vx * vx + vy * vy), k = Math.Max(0, s - earlyDv) / s;
            vx *= k; vy *= k; dv -= earlyDv;
        }
        Result r = new Result();
        double activeSince = double.NaN;
        while (h > 3000 && t < 2000)
        {
            double v = Math.Sqrt(vx * vx + vy * vy);
            double rho, conv, shock;
            truth.At(h, v, out rho, out conv, out shock);
            // Mit glideBelow: darueber faellt die Stufe rueckwaerts (Cd*A 1), darunter gleitet sie (8);
            // waehrend des Burns immer rueckwaerts. Ohne: immer 8, wie bisher.
            bool burning = policy != null && policy.Active;
            double cda = !glides ? 8 : burning || h > glideBelow ? retroCda : 8;
            double drag = 0.5 * rho * v * v * cda / mass;
            double ratio = Math.Max(skin / limit, inside / limit);
            if (ratio > r.Peak) r.Peak = ratio;
            double throttle = 0;
            if (policy != null)
            {
                bool active = policy.Active;
                if (active && double.IsNaN(activeSince)) activeSince = t;
                if (!active) activeSince = double.NaN;
                // Ueber 45 km faellt die Stufe rueckwaerts (ausgerichtet), darunter gleitet sie 30 Grad
                // schraeg; aus dem Gleiten braucht sie 3 s, bis sie gegen die Bahn zeigt.
                double aim = active ? (t - activeSince < 3 && h < 45000 ? 30 : 0) : (h < 45000 ? 30 : 0);
                throttle = policy.Update(new EntryBurnPolicy.Flight
                {
                    Time = t, Ratio = ratio, Limit = limit, Altitude = h, VelocityUp = vy, VelocityHorizontal = vx,
                    Gravity = g, DragPerRhoV2 = 0.5 * (glides ? (h > glideBelow ? retroCda : 8) : cda) / mass,
                    BurnDragPerRhoV2 = glides ? 0.5 * retroCda / mass : double.NaN, Wait = glides && h > glideBelow,
                    ThrustAcceleration = thrust,
                    AvailableDeltaV = dv, LandingDeltaV = landingDv, AimError = aim, Allowed = allowed
                });
            }
            double a = drag + throttle * thrust;
            if (throttle > 0) dv -= throttle * thrust * dt;
            vx -= a * vx / v * dt; vy -= (a * vy / v + g) * dt;
            h += vy * dt; t += dt;
            double skinRate = Gain * conv * (shock - skin) - radiate * (Math.Pow(skin, 4) - Math.Pow(300, 4))
                - conduct * (skin - inside);
            inside += soak * (skin - inside) * dt;
            skin += skinRate * dt;
        }
        r.SpeedAtEnd = Math.Sqrt(vx * vx + vy * vy);
        r.Spent = availableDv - dv;
        if (policy != null) { r.Burns = policy.Burns; r.Exhausted = policy.Exhausted; }
        return r;
    }

    // Der kleinste feste Burn gleich am Rand der Atmosphaere, mit dem die Spitze unter peak bleibt.
    static double EdgeBurn(double speed, double gamma, double peak, Air truth = null)
    {
        double lo = 0, hi = 2000;
        for (int i = 0; i < 30; i++)
        {
            double mid = (lo + hi) / 2;
            if (Fly(speed, gamma, null, 3000, double.NaN, mid, true, truth).Peak < peak) hi = mid; else lo = mid;
        }
        return hi;
    }

    static int Main(string[] args)
    {
        if (args.Length > 0) Gain = double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
        // Steil und schnell: ohne Burn ueber der Grenze.
        Result hot = Fly(2300, 35, null);
        Console.WriteLine("  ohne Burn: Spitze " + (100 * hot.Peak).ToString("0") + " %");
        Check(hot.Peak > 1.1, "Model: a steep 2300 m/s entry overheats without a burn");

        EntryBurnPolicy policy = New();
        Result saved = Fly(2100, 30, policy);
        Result hotter = Fly(2100, 30, null);
        double edge = EdgeBurn(2100, 30, saved.Peak);
        Console.WriteLine("  2100 m/s, 30 Grad: ohne Burn " + (100 * hotter.Peak).ToString("0") + " %, mit Burn "
            + (100 * saved.Peak).ToString("0") + " %, " + saved.Spent.ToString("0") + " m/s in " + saved.Burns
            + " Burns; fester Burn am Rand fuer dieselbe Spitze: " + edge.ToString("0") + " m/s");
        Check(hotter.Peak > 1.1 && saved.Peak < 1, "The entry burn keeps the hottest part below the limit");
        // Seit 0.9.51 zuendet die Regel so spaet wie moeglich (das Modell lernt dabei weiter); bei einem
        // genauen Modell wie hier kostet das etwas mehr als der ideale Burn.
        Check(saved.Spent < 1.35 * edge, "No more than 35 % above an ideal instant burn at the edge of the atmosphere ("
            + saved.Spent.ToString("0") + " vs " + edge.ToString("0") + " m/s), although it only learns in the air");
        // Der steilste Fall: die Regel wartet 5 Punkte Anstieg ab (Flug vom 26.09.2026) und muss dann
        // kraeftiger bremsen - teurer, aber heil.
        EntryBurnPolicy steep = New();
        Result steepSaved = Fly(2300, 35, steep);
        Console.WriteLine("  2300 m/s, 35 Grad: ohne Burn " + (100 * hot.Peak).ToString("0") + " %, mit Burn "
            + (100 * steepSaved.Peak).ToString("0") + " %, " + steepSaved.Spent.ToString("0") + " m/s");
        Check(steepSaved.Peak < 1, "A very steep 2300 m/s entry is still kept below the limit");

        // Die Form des Waermeuebergangs weicht ab (Dichte hoch 0,4 statt 0,5).
        Air other = new Air { DensityExponent = 0.4 };
        Result otherHot = Fly(2100, 30, null, 1500, double.NaN, 0, true, other);
        EntryBurnPolicy robust = New();
        Result otherSaved = Fly(2100, 30, robust, 2500, double.NaN, 0, true, other);
        double otherEdge = EdgeBurn(2100, 30, 1, other);
        Console.WriteLine("  anderes Waermemodell: ohne Burn " + (100 * otherHot.Peak).ToString("0") + " %, mit "
            + (100 * otherSaved.Peak).ToString("0") + " % fuer " + otherSaved.Spent.ToString("0") + " m/s (fest am Rand: "
            + otherEdge.ToString("0") + " m/s fuer 100 %)");
        Check(otherHot.Peak > 1.1 && otherSaved.Peak < 1, "A heat model the rule does not know is still kept below the limit");
        // Hier sagt das Modell die Spitze zu hoch voraus und bremst zu viel (bekannte Schwaeche): Grenze 70 %.
        Check(otherSaved.Spent < 1.7 * otherEdge, "... spending at most 70 % more than an ideal instant burn at the edge");

        // Wie im Flug vom 26.09.2026: bis 48 km rueckwaerts mit kleinem Widerstand, dann Gleiten. Mit dem
        // Rueckwaerts-Wert haette die Vorhersage die Stufe viel zu schnell gesehen; die Regel wartet das
        // Gleiten ab und rechnet dann mit dem Gleitwiderstand.
        Result glideHot = Fly(2000, 30, null, 1500, double.NaN, 0, true, null, 48000);
        EntryBurnPolicy glider = New();
        Result glideSaved = Fly(2000, 30, glider, 1500, double.NaN, 0, true, null, 48000);
        Console.WriteLine("  mit Gleiten ab 48 km: ohne Burn " + (100 * glideHot.Peak).ToString("0") + " %, mit Burn "
            + (100 * glideSaved.Peak).ToString("0") + " % fuer " + glideSaved.Spent.ToString("0") + " m/s");
        Check(glideHot.Peak > 1.1 && glideSaved.Peak < 1, "Waiting for the glide still keeps the booster below the limit");
        // Bekannte Grenze: bei 2100 m/s reicht der Vorrat ab dem Gleiten nicht mehr (nur ein Burn am Rand
        // der Atmosphaere haette gereicht) - die Regel entscheidet erst mit dem Gleitwiderstand.

        // Flach und langsamer: bleibt kuehl, kein Treibstoff.
        EntryBurnPolicy mild = New();
        Result cool = Fly(1500, 10, mild);
        Console.WriteLine("  mild: Spitze " + (100 * cool.Peak).ToString("0") + " %");
        Check(cool.Peak < 0.8 && cool.Spent == 0 && mild.Burns == 0, "A mild entry costs no propellant");

        // Nicht freigegeben (Grad Leicht, Einstellung aus): nie Schub.
        EntryBurnPolicy off = New();
        Result none = Fly(2300, 35, off, 1500, double.NaN, 0, false);
        Check(none.Spent == 0 && off.Burns == 0, "Without release the burn never fires");

        // Wenig Treibstoff: der Landevorhalt bleibt.
        EntryBurnPolicy tight = New();
        Result limited = Fly(2300, 35, tight, 500, 300);
        Console.WriteLine("  knapp: " + limited.Spent.ToString("0") + " m/s ausgegeben, Vorhalt " + tight.Floor.ToString("0"));
        Check(500 - limited.Spent >= EntryBurnPolicy.LandingMargin * 300 - 1,
            "The landing reserve (1.25 x the predicted landing) is never spent");
        EntryBurnPolicy share = New();
        Result shared = Fly(2300, 35, share, 600);
        Console.WriteLine("  600 m/s: " + shared.Spent.ToString("0") + " ausgegeben, Spitze " + (100 * shared.Peak).ToString("0") + " %");
        Check(600 - shared.Spent >= EntryBurnPolicy.ReserveShare * 600 - 1,
            "Without a landing forecast at least 40 % of the propellant stays");

        // Schub erst, wenn die Stufe gegen die Bahn zeigt (Sicherung ohne Modell).
        EntryBurnPolicy aim = New();
        Func<double, double, double, double> step = (t, r, err) => aim.Update(new EntryBurnPolicy.Flight
        {
            Time = t, Ratio = r, Limit = double.NaN, Altitude = 40000, VelocityUp = -500, VelocityHorizontal = 1500,
            Gravity = 9.81, DragPerRhoV2 = double.NaN, ThrustAcceleration = 25, AvailableDeltaV = 1000,
            LandingDeltaV = double.NaN, AimError = err, Allowed = true
        });
        Check(step(0, 0.5, 0) == 0, "Cool: no thrust");
        step(1, 0.7, 0);
        double wrongAim = step(2, 0.9, 40);
        Check(aim.Active && wrongAim == 0, "Fast rising heat activates the burn, but no thrust while 40 degrees off");
        Check(step(2.1, 0.92, 3) == 1, "Aligned: full thrust");
        Console.WriteLine(checks + " entry burn checks passed.");
        return 0;
    }
}
