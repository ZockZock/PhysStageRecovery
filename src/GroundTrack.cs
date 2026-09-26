using System;

namespace BoosterWatch
{
    // Die eigene Bodenspur als Gegenprobe zu KSPs Geschwindigkeitsanzeige.
    //
    // KSPs horizontale Geschwindigkeit ist nach einer Floating-Origin-Verschiebung falsch: eine
    // Stufe am Fallschirm, die mit 8 m/s niederging, las 175 m/s seitlich - genau die
    // Rotationsgeschwindigkeit des Planeten. Die Spur aus Breiten- und Laengengrad kann das nicht
    // treffen, weil sie aus zwei Positionen desselben Koerpers entsteht.
    //
    // Entscheidend ist das Fenster: wird die Referenzprobe bei jedem Tick erneuert, ist ihr
    // Abstand immer der Tick-Abstand - bei 50 Hz genau 0.02 s - und die Bedingung "dt > 0.02"
    // laeuft nie. Genau daran war die alte Pruefung in TrackedBooster praktisch tot. Hier rueckt
    // die Referenz deshalb erst weiter, wenn das Fenster abgelaufen ist, und dann wird verglichen.
    public sealed class GroundTrack
    {
        // Abstand zwischen zwei Proben. 0,3 s sind bei 175 m/s rund 50 m Bogen - genau genug, um
        // die Geschwindigkeit auf wenige Prozent zu treffen.
        public const double Window = 0.3;
        // Ab dieser Abweichung gilt die Anzeige als falsch und der kleinere Wert gewinnt.
        public const double Tolerance = 25.0;

        private bool hasReference;
        private double latitude, longitude, time;
        private bool lastUsedTrack;
        private double lastDerived = double.NaN;

        // Wahr, wenn im letzten Vergleich die Bodenspur den Ausschlag gab (Anzeige war falsch).
        public bool UsedTrack { get; private set; }
        // Die zuletzt gerechnete Spurgeschwindigkeit, fuer Log und Diagnose.
        public double Derived { get; private set; }

        public double Resolve(double reading, double latitudeNow, double longitudeNow, double timeNow,
            double bodyRadius)
        {
            UsedTrack = false;
            Derived = double.NaN;
            if (!RecoveryPolicy.Finite(reading)) return reading;
            if (!hasReference)
            {
                Remember(latitudeNow, longitudeNow, timeNow);
                return reading;
            }
            // Zwischen zwei Vergleichen gilt der letzte Befund weiter. Vorher kam hier die rohe
            // Anzeige zurueck - bei 0,1 s Messtakt und 0,3 s Fenster an drei von vier Ticks die
            // falschen 175 m/s, genau an den Ticks, an denen z. B. die Abschaltentscheidung faellt.
            if (timeNow - time < Window)
            {
                if (lastUsedTrack && RecoveryPolicy.Finite(lastDerived))
                {
                    UsedTrack = true; Derived = lastDerived;
                    return Math.Min(reading, lastDerived);
                }
                return reading;
            }

            double derived = Speed(latitude, longitude, time, latitudeNow, longitudeNow, timeNow, bodyRadius);
            Derived = derived;
            double resolved = reading;
            if (RecoveryPolicy.Finite(derived) && Math.Abs(derived - reading) > Tolerance)
            {
                resolved = Math.Min(derived, reading);
                UsedTrack = true;
            }
            lastUsedTrack = UsedTrack; lastDerived = derived;
            Remember(latitudeNow, longitudeNow, timeNow);
            return resolved;
        }

        private void Remember(double latitudeNow, double longitudeNow, double timeNow)
        {
            if (!RecoveryPolicy.Finite(latitudeNow) || !RecoveryPolicy.Finite(longitudeNow)
                || !RecoveryPolicy.Finite(timeNow)) return;
            latitude = latitudeNow; longitude = longitudeNow; time = timeNow; hasReference = true;
        }

        // Geschwindigkeit ueber Grund aus zwei Positionen. Der Laengengrad wird ueber den
        // 180-Grad-Sprung richtig gefaltet, sonst ergaebe eine Datumsgrenze 3,8 Mio m/s.
        public static double Speed(double lat1, double lon1, double t1, double lat2, double lon2,
            double t2, double bodyRadius)
        {
            if (!RecoveryPolicy.Finite(lat1) || !RecoveryPolicy.Finite(lon1) || !RecoveryPolicy.Finite(t1))
                return double.NaN;
            if (!RecoveryPolicy.Finite(lat2) || !RecoveryPolicy.Finite(lon2) || !RecoveryPolicy.Finite(t2))
                return double.NaN;
            if (!RecoveryPolicy.Finite(bodyRadius) || bodyRadius <= 0) return double.NaN;
            double dt = t2 - t1;
            if (dt <= 0) return double.NaN;
            const double degree = Math.PI / 180.0;
            double north = (lat2 - lat1) * degree * bodyRadius;
            double deltaLongitude = lon2 - lon1;
            while (deltaLongitude > 180) deltaLongitude -= 360;
            while (deltaLongitude < -180) deltaLongitude += 360;
            double east = deltaLongitude * degree * bodyRadius * Math.Cos(lat2 * degree);
            return Math.Sqrt(north * north + east * east) / dt;
        }
    }
}
