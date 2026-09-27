using System;

namespace BoosterWatch
{
    // Die Luft entlang der Bahn, so wie KSP sie fuer die Erwaermung rechnet (FlightIntegrator):
    // Dichte, Waermeuebergang h und Stosstemperatur Ts. Ein Teil erwaermt sich mit h * (Ts - T).
    public interface IEntryAtmosphere
    {
        void At(double altitude, double speed, out double density, out double convection, out double shockTemperature);
    }

    // Wiedereintrittsburn: bremst nur, wenn die Hitze sonst die Grenze erreichen wuerde, und nur so
    // viel, dass der Rest des Eintritts ohne Schub kuehl genug bleibt.
    //
    // KSP erwaermt die Haut eines Teils mit h * (Ts - T): h waechst mit Wurzel(Dichte) mal
    // Geschwindigkeit hoch drei, Ts mit der Geschwindigkeit. Die Dichte waechst beim Sinken
    // exponentiell, und die Temperatur folgt mit Verzug - ein Blick auf den bisherigen Anstieg sieht
    // die Spitze zu spaet kommen. Die Regel lernt deshalb waehrend des Eintritts, wie das heisseste
    // Teil auf die Anstroemung antwortet (r = Anteil an seiner Zerstoerungsgrenze L):
    //     dr/dt = a * h * (Ts/L - r)  -  b * (r - r0)
    // a: Flaeche durch Waermekapazitaet, b: Leitung ins Innere und Abstrahlung. Eine eigene
    // Abstrahlung mit T^4 ist aus dem Anfang des Eintritts nicht zu bestimmen und sagte im Testrahmen
    // viel zu viel Kuehlung voraus (Spitze 136 % statt der erwarteten 91 %). Damit rechnet sie den
    // Rest des Eintritts voraus: ohne Burn und mit dem kuerzesten ausreichenden Burn. Gezuendet wird
    // erst, wenn Warten nicht mehr reicht; gestoppt, sobald der Rest ohne Schub unter der Zielspitze
    // bleibt. Jeder m/s, den das Triebwerk nicht wegnimmt, baut die Luft umsonst ab.
    //
    // Als Sicherung zaehlt zusaetzlich der gemessene Anstieg: steigt die Hitze schneller, als das
    // Modell erwartet, wird gebremst. Ein Rest bleibt immer fuer die Landung. Reine Logik ohne KSP,
    // damit sie im Testrahmen mit einem Waermemodell laufen kann.
    public sealed class EntryBurnPolicy
    {
        // Hoechster vorausgesagter Anteil an der Zerstoerungsgrenze, den der Burn zulaesst.
        // 0.9.51: 0,95 statt 0,9. Beide Fluege vom 26.09.2026 blieben mit Burn weit darunter (37 und
        // 42 %), und das gelernte Waermemodell sagte selbst entlang der echten Bahn eine um die Haelfte
        // zu hohe Erwaermung voraus - der Rand liegt also schon im Modell.
        public const double TargetRatio = 0.95;
        // Sicherung aus dem Anstieg: Start, wenn r + Anstieg * LookaheadSeconds diesen Anteil erreicht.
        public const double StartRatio = 1.0;
        public const double StopRatio = 0.85;
        public const double LookaheadSeconds = 6;
        public const double TrendSeconds = 1.5;
        // Drehen aus dem Gleiten gegen die Bahn und Anlaufen des Triebwerks [s].
        public const double ReactionSeconds = 4;
        // So oft wird vorausgerechnet [s].
        public const double PlanInterval = 0.5;
        // Reserve beim Warten [s]: gezuendet wird, wenn ein Burn so viel spaeter nicht mehr reichen wuerde.
        public const double LaterSeconds = 3;
        // Der Burn laeuft, bis die Vorhersage so weit unter TargetRatio liegt.
        public const double StopMargin = 0.05;
        // Schub erst, wenn die Schubachse hoechstens so weit neben der Gegenrichtung zur Bahn liegt.
        public const double AimToleranceDegrees = 10;
        // Mindestens dieser Anteil des Delta-v beim ersten Eintritt bleibt fuer die Landung ...
        public const double ReserveShare = 0.4;
        // ... und mindestens der vorausgesagte Landebedarf mit diesem Aufschlag.
        public const double LandingMargin = 1.25;
        // Gegen Flattern an der Schwelle: jeder Burn und jede Pause dauert mindestens so lange [s].
        public const double MinimumSwitchSeconds = 2;
        // So weit muss die Hitze im Eintritt schon gestiegen sein, bevor das Modell entscheidet (Anteil
        // an der Grenze). Flug vom 26.09.2026, 08:33: nach 2 Punkten Anstieg (16 auf 18 %) sagte es
        // 121 % voraus, der Burn kostete 558 m/s - erreicht wurden danach 37 %. Aus so wenig Anstieg ist
        // die Abkuehlung nicht zu bestimmen. Der Flug, noch einmal durch die Regel geschickt: mit 5
        // Punkten steht das Modell erst bei 36 km und sagt 49 % voraus (erreicht: 37 %), kein Burn. Im
        // Testrahmen bleiben mit 5 Punkten noch alle steilen Eintritte bis 2300 m/s unter der Grenze;
        // mit 10 Punkten kaeme der Burn fuer die steilsten zu spaet.
        public const double MinimumRise = 0.05;

        public struct Flight
        {
            public double Time;
            // Anteil an der Zerstoerungsgrenze des heissesten Teils (ohne Triebwerke, die selbst heizen)
            // und diese Grenze in Kelvin.
            public double Ratio, Limit;
            public double Altitude, VelocityUp, VelocityHorizontal;
            public double Gravity;
            // Luftwiderstand pro rho*v^2 [1/m]: Widerstandsbeschleunigung / (rho * v^2), ohne Schub
            // (beim Gleiten der Gleitwert) und waehrend des Burns (rueckwaerts; NaN = derselbe).
            public double DragPerRhoV2, BurnDragPerRhoV2;
            // Das Modell soll noch nicht entscheiden: die Stufe wird gleich gleiten, und ihr Widerstand
            // ist dann ein Vielfaches (Flug vom 26.09.2026: Cd*A 0,7 rueckwaerts, 6 beim Gleiten). Die
            // Vorhersage mit dem Rueckwaerts-Wert sah sie viel zu schnell und viel zu heiss.
            public bool Wait;
            public double ThrustAcceleration;
            public double AvailableDeltaV;
            // Vorausgesagter Bedarf der Landung (NaN = unbekannt).
            public double LandingDeltaV;
            // Winkel zwischen Schubachse und Gegenrichtung zur Bahn [Grad].
            public double AimError;
            // Einstellung an, Grad mit echter Hitze, Sinkflug vor der Landezuendung, Triebwerk da.
            public bool Allowed;
        }

        private readonly IEntryAtmosphere air;
        public EntryBurnPolicy(IEntryAtmosphere atmosphere) { air = atmosphere; }

        public bool Active { get; private set; }
        public bool Exhausted { get; private set; }
        public bool ModelReady { get; private set; }
        public double HeatGain { get; private set; } = double.NaN;
        public double Cooling { get; private set; }
        public double Trend { get; private set; }
        // Vorausgesagte Spitze ohne weiteren Burn (NaN, solange das Modell nicht steht).
        public double Predicted { get; private set; } = double.NaN;
        public double Peak { get; private set; }
        public double StartDeltaV { get; private set; } = double.NaN;
        public double Floor { get; private set; } = double.NaN;
        public double Spent { get; private set; }
        public int Burns { get; private set; }
        public string Reason { get; private set; } = "";

        private double lastTime = double.NaN, lastRatio = double.NaN, lastDv = double.NaN;
        private double switched = double.NaN, nextPlan = double.NaN, baseRatio = double.NaN;
        private double sampleTime = double.NaN, sampleRatio = double.NaN, sampleHeat, sampleShock;
        private int samples;
        private double riseSeen;

        public double Update(Flight f)
        {
            double ratio = f.Ratio;
            if (Finite(ratio) && ratio > Peak) Peak = ratio;
            double speed = Math.Sqrt(f.VelocityUp * f.VelocityUp + f.VelocityHorizontal * f.VelocityHorizontal);
            if (Finite(lastTime) && Finite(lastRatio) && Finite(ratio) && f.Time > lastTime)
            {
                double dt = f.Time - lastTime;
                Trend += ((ratio - lastRatio) / dt - Trend) * (dt / (TrendSeconds + dt));
            }
            if (Active && Finite(lastDv) && Finite(f.AvailableDeltaV) && f.AvailableDeltaV < lastDv)
                Spent += lastDv - f.AvailableDeltaV;
            if (!Finite(lastTime) || f.Time > lastTime) { lastTime = f.Time; lastRatio = ratio; }
            lastDv = f.AvailableDeltaV;
            if (Finite(f.Limit) && f.Limit > 0 && Finite(f.Altitude))
            {
                double rho, h, shock;
                air.At(f.Altitude, speed, out rho, out h, out shock);
                Learn(f.Time, ratio, h, shock / f.Limit);
            }

            if (!f.Allowed || !Finite(ratio) || !Finite(f.AvailableDeltaV))
            { Stop(!f.Allowed ? "nicht freigegeben" : "keine Messwerte", f.Time); return 0; }
            if (!Finite(StartDeltaV)) StartDeltaV = f.AvailableDeltaV;
            Floor = ReserveShare * StartDeltaV;
            if (Finite(f.LandingDeltaV) && f.LandingDeltaV > 0) Floor = Math.Max(Floor, LandingMargin * f.LandingDeltaV);
            if (f.AvailableDeltaV <= Floor)
            {
                if (Active || !Exhausted) Reason = "Landevorhalt erreicht";
                Active = false; Exhausted = true;
                return 0;
            }
            bool settled = !Finite(switched) || f.Time - switched >= MinimumSwitchSeconds;
            double rising = Finite(ratio) ? ratio + Math.Max(0, Trend) * LookaheadSeconds : double.NaN;
            bool climbing = Trend > 0 && rising >= StartRatio;
            bool planned = ModelReady && !f.Wait && Finite(f.DragPerRhoV2) && f.ThrustAcceleration > 0 && speed > 1
                && Finite(f.Limit);
            if (planned && (!Finite(nextPlan) || f.Time >= nextPlan))
            {
                nextPlan = f.Time + PlanInterval;
                Decide(f, ratio, settled, climbing, rising);
            }
            else if (!planned)
            {
                Predicted = double.NaN;
                if (!Active && settled && climbing)
                    Start(f.Time, "Hitze steigt auf " + Percent(rising) + " (noch ohne Modell)");
                else if (Active && settled && (Trend <= 0 || rising <= StopRatio))
                    Stop(Trend <= 0 ? "Hitze steigt nicht mehr" : "Hitze unter " + Percent(StopRatio), f.Time);
            }
            if (!Active) return 0;
            return Finite(f.AimError) && f.AimError <= AimToleranceDegrees ? 1 : 0;
        }

        private void Decide(Flight f, double ratio, bool settled, bool climbing, double rising)
        {
            double coast = Simulate(f, ratio, 0, 0);
            Predicted = coast;
            if (Active)
            {
                // Mit Abstand zur Startschwelle aufhoeren: wer genau an der Grenze stoppt, zuendet bei der
                // naechsten kleinen Abweichung wieder - dann oft zu spaet, weil die Stufe erst zurueckdrehen muss.
                if (settled && coast <= TargetRatio - StopMargin && !climbing)
                    Stop("Rest ohne Schub bleibt bei " + Percent(coast), f.Time);
                return;
            }
            if (!settled) return;
            if (climbing)
            {
                Start(f.Time, "Hitze steigt schneller als vorausgesagt (" + Percent(rising) + " in "
                    + LookaheadSeconds.ToString("0") + " s)");
                return;
            }
            if (coast <= TargetRatio) return;
            // Jetzt anfangen oder einen Planungsschritt spaeter? Entschieden wird nach dem Delta-v,
            // das der kuerzeste ausreichende Burn jeweils kostet. Schub kommt erst, wenn die Stufe
            // gegen die Bahn zeigt: aus dem Gleiten nach der Reaktionszeit.
            double spendable = Math.Max(0, f.AvailableDeltaV - Floor);
            double longest = spendable / f.ThrustAcceleration;
            double turn = Finite(f.AimError) && f.AimError <= AimToleranceDegrees ? 0 : ReactionSeconds;
            double now = ShortestBurn(f, ratio, turn, longest);
            // So spaet wie moeglich: solange ein Burn nach LaterSeconds noch reicht, wird gewartet. Das
            // kostet bei einem genauen Modell etwas mehr Delta-v, gibt dem Modell aber Zeit, weiter zu
            // lernen - und in beiden Fluegen vom 26.09.2026 sank die Vorhersage mit jeder Sekunde.
            if (Finite(ShortestBurn(f, ratio, turn + PlanInterval + LaterSeconds, longest))) return;
            Start(f.Time, "Spitze ohne Burn " + Percent(coast) + ", Burn ~"
                + (Finite(now) ? (now * f.ThrustAcceleration).ToString("0") + " m/s" : "so viel der Vorrat hergibt"));
        }

        // Kuerzester Burn nach delay Sekunden, der die Spitze unter TargetRatio haelt; NaN = keiner reicht.
        private double ShortestBurn(Flight f, double ratio, double delay, double longest)
        {
            double goal = TargetRatio - StopMargin;
            if (!(longest > 0) || Simulate(f, ratio, delay, longest) > goal) return double.NaN;
            double lo = 0, hi = longest;
            for (int i = 0; i < 12; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Simulate(f, ratio, delay, mid) <= goal) hi = mid; else lo = mid;
            }
            return hi;
        }

        // Rechnet den Rest des Eintritts voraus: erst delay Sekunden ohne Schub, dann burn Sekunden
        // mit vollem Schub gegen die Bahn, danach wieder ohne. Rueckgabe: hoechster Anteil.
        private double Simulate(Flight f, double ratio, double delay, double burn)
        {
            double h = f.Altitude, vu = f.VelocityUp, vh = f.VelocityHorizontal, r = ratio, peak = ratio;
            double r0 = Finite(baseRatio) ? baseRatio : ratio;
            const double step = 0.25;
            double lastInput = double.NaN;
            for (double t = 0; t < 400; t += step)
            {
                double v = Math.Sqrt(vu * vu + vh * vh);
                if (v < 1 || h < 0) break;
                double rho, conv, shock;
                air.At(h, v, out rho, out conv, out shock);
                double input = conv * shock / f.Limit;
                double rate = HeatGain * conv * (shock / f.Limit - r) - Cooling * (r - r0);
                r += rate * step;
                if (r > peak) peak = r;
                // Nach dem Burn, hinter der Spitze der Anstroemung und schon abkuehlend: es kommt nichts mehr.
                if (t >= delay + burn && Finite(lastInput) && input < lastInput && rate < 0) break;
                lastInput = input;
                bool thrust = t >= delay && t < delay + burn;
                double drag = thrust && Finite(f.BurnDragPerRhoV2) ? f.BurnDragPerRhoV2 : f.DragPerRhoV2;
                double a = drag * Math.Max(0, rho) * v * v + (thrust ? f.ThrustAcceleration : 0);
                vu += (-a * vu / v - f.Gravity) * step;
                vh += -a * vh / v * step;
                if (vh < 0) vh = 0;
                h += vu * step;
            }
            return peak;
        }

        // Lernt a und b aus den Messungen, in Abstaenden von mindestens 0,5 s, mit Vergessen (etwa
        // die letzten 15 s zaehlen). Beide sind nicht negativ; passt das nicht, gilt a allein (ohne
        // Kuehlung sagt das Modell die Spitze eher zu hoch voraus - der Burn kommt dann eher zu frueh).
        private readonly double[,] xx = new double[2, 2];
        private readonly double[] xy = new double[2];
        private double yy;
        private void Learn(double time, double ratio, double conv, double shock)
        {
            if (!Finite(ratio) || !Finite(conv) || !Finite(shock)) return;
            if (!Finite(baseRatio) || ratio < baseRatio) baseRatio = ratio;
            if (!Finite(sampleTime) || time < sampleTime)
            { sampleTime = time; sampleRatio = ratio; sampleHeat = conv; sampleShock = shock; return; }
            double dt = time - sampleTime;
            if (dt < 0.5) return;
            double y = (ratio - sampleRatio) / dt;
            double mid = 0.5 * (ratio + sampleRatio);
            double[] x =
            {
                0.5 * (conv + sampleHeat) * (0.5 * (shock + sampleShock) - mid),
                -(mid - baseRatio)
            };
            const double forget = 0.97;
            for (int i = 0; i < 2; i++)
            {
                xy[i] = forget * xy[i] + x[i] * y;
                for (int j = 0; j < 2; j++) xx[i, j] = forget * xx[i, j] + x[i] * x[j];
            }
            yy = forget * yy + y * y;
            samples++;
            riseSeen = Math.Max(riseSeen, ratio - baseRatio);
            sampleTime = time; sampleRatio = ratio; sampleHeat = conv; sampleShock = shock;
            double[] best = null; double bestError = double.PositiveInfinity;
            foreach (int[] set in Subsets)
            {
                double[] theta = Solve(set);
                if (theta == null || !(theta[0] > 0) || theta[1] < 0) continue;
                double error = yy;
                for (int i = 0; i < 2; i++)
                {
                    error -= 2 * theta[i] * xy[i];
                    for (int j = 0; j < 2; j++) error += theta[i] * xx[i, j] * theta[j];
                }
                // Ein Parameter mehr muss sich lohnen.
                error = Math.Max(0, error) * (1 + 0.05 * set.Length) + 1e-18 * set.Length;
                if (error < bestError) { bestError = error; best = theta; }
            }
            if (best == null) { ModelReady = false; return; }
            HeatGain = best[0]; Cooling = best[1];
            ModelReady = samples >= 6 && riseSeen >= MinimumRise;
        }
        private static readonly int[][] Subsets = { new[] { 0, 1 }, new[] { 0 } };
        private double[] Solve(int[] set)
        {
            int n = set.Length;
            double[,] m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] = xx[set[i], set[j]];
                m[i, n] = xy[set[i]];
            }
            for (int c = 0; c < n; c++)
            {
                int pivot = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[pivot, c])) pivot = r;
                if (Math.Abs(m[pivot, c]) < 1e-300) return null;
                for (int k = 0; k <= n; k++) { double t = m[c, k]; m[c, k] = m[pivot, k]; m[pivot, k] = t; }
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double factor = m[r, c] / m[c, c];
                    for (int k = c; k <= n; k++) m[r, k] -= factor * m[c, k];
                }
            }
            double[] theta = new double[2];
            for (int i = 0; i < n; i++) theta[set[i]] = m[i, n] / m[i, i];
            foreach (double v in theta) if (!Finite(v)) return null;
            return theta;
        }

        private void Start(double time, string reason)
        { Active = true; Burns++; switched = time; Reason = reason; }

        private void Stop(string reason, double time)
        {
            if (Active) { Reason = reason; switched = time; }
            Active = false;
        }

        private static string Percent(double x) { return (100 * x).ToString("0") + " %"; }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
