using System;

namespace BoosterWatch
{
    internal static class ParachuteDeployment
    {
        // The canopy opens this high above the ground.
        //
        // KSP decides the opening itself in ModuleParachute.ShouldDeploy: it compares the part's
        // deployAltitude against the altitude of the part (above the sea) and against a downward ray
        // to the terrain, and it adds a pure speed check. So a plain number here means "this high
        // above the ground, or this high above the sea, whichever comes first" - which is exactly the
        // wanted behaviour over land and over water. Adding the terrain height on top, as an earlier
        // attempt did, counts the ground twice.
        //
        // 1000 m is enough: from the semi-deploy the stock canopy is fully inflated about 1.6 s
        // later, so the booster is under a full canopy well before it lands, and opening any higher
        // only means a longer drift under the chute. The value comes from the settings and is written
        // here before each measurement, so a change in the window applies to running boosters.
        public const float DefaultOpenAboveGround = 1000f;
        public static float OpenAboveGround = DefaultOpenAboveGround;

        // MechJebModuleLandingAutopilot.DeployParachutes arms a canopy as soon as the vessel is below
        // its opening height and KSP calls the deployment safe. The mod does the same; KSP's own
        // height, pressure and speed checks still run afterwards.
        public static bool Allowed(ModuleParachute.deploymentSafeStates state)
        {
            return state != ModuleParachute.deploymentSafeStates.UNSAFE;
        }

        // Oeffnungshoehe fuer die aktuelle Fahrt. Die eingestellte Hoehe gilt fuer einen normalen
        // Fall. Schneller muss der Schirm hoeher aufgehen: Aufgehzeit plus Bremsweg plus Reserve.
        //
        // Nachgemessen im Flug vom 25.09.2026, 21:57 (je zwei Mk16 an einem Seitenbooster):
        // aufgegangen bei 1897 m mit 554 m/s -> 13 m/s bei 666 m, also ~1230 m gebraucht;
        // aufgegangen bei 3458 m mit 742 m/s -> 15 m/s bei 1631 m, also ~1830 m gebraucht.
        // Die erste Fassung (2,5 s, 150 m/s^2, 300 m auf die Sinkrate) war viel zu vorsichtig: der
        // Booster hing danach ueber 1,5 km am Schirm. Jetzt 1,5 s, 400 m/s^2 und 200 m Reserve auf
        // die ganze Fahrt - das deckt beide Messungen mit ~200 m Luft.
        public const double InflateSeconds = 1.5, CanopyDeceleration = 400, FinalMargin = 200;
        // Hoeher lohnt nicht: dort ist die Luft zu duenn, als dass der Schirm frueher fertig wuerde.
        public const double MaximumOpeningHeight = 5000;

        public static double OpeningHeight(double setting, double speed)
        {
            if (!PortMathFinite(speed) || speed <= 0) return setting;
            double needed = speed * InflateSeconds + speed * speed / (2 * CanopyDeceleration) + FinalMargin;
            return Math.Max(setting, Math.Min(MaximumOpeningHeight, needed));
        }

        // Scharf geschaltet wird erst kurz davor. Ein scharfer Stock-Schirm geht sofort halb auf, sobald
        // die Luft reicht, und haelt den Booster dann den ganzen Weg bis zur Oeffnungshoehe langsam:
        // im Flug vom 25.09.2026, 23:10 halb offen ab 9,4 km, mit ~130 m/s bis 1,1 km - rund eine
        // Minute laenger in der Luft, und so lange kein normaler Zeitraffer. Jetzt ArmLeadSeconds
        // Flugzeit vor der Oeffnungshoehe.
        public const double ArmLeadSeconds = 6;

        public static double ArmingHeight(double setting, double speed)
        {
            double open = OpeningHeight(setting, speed);
            return open + (PortMathFinite(speed) && speed > 0 ? speed * ArmLeadSeconds : 0);
        }

        // Moves the part's own opening height to OpeningHeight. Safe to call every measurement.
        public static float SetOpeningHeight(ModuleParachute chute, double groundAltitude, double speed = 0)
        {
            if (chute == null) return 0;
            float wanted = (float)OpeningHeight(OpenAboveGround, speed);
            if (Math.Abs(chute.deployAltitude - wanted) > 0.5f) chute.deployAltitude = wanted;
            return chute.deployAltitude;
        }

        // KSP cuts a canopy whose temperature passes chuteMaxTemp, which the stock parts set to 650.
        // Opening at 3000 m in a 300 m/s descent heats it past that at once - the canopies were
        // reported destroyed in the same second they opened. Raising the limit is the same
        // intervention as the part heat guard, and it is what makes an early opening survivable: the
        // canopy still has to carry the load and still decelerates the booster with its own drag.
        public const double ChuteMaxTemperature = 100000;

        public static void Protect(ModuleParachute chute)
        {
            if (chute == null) return;
            if (chute.chuteMaxTemp < ChuteMaxTemperature) chute.chuteMaxTemp = ChuteMaxTemperature;
            // autoCutSpeed cuts the canopy at high speed when a part sets it; the mod opens by height.
            if (chute.autoCutSpeed > 0) chute.autoCutSpeed = 0;
        }

        public static bool TryArm(ModuleParachute chute, bool enabled, bool descending)
        {
            string reason;
            return TryArm(chute, enabled, descending, 0, out reason);
        }

        // Arms one canopy. reason is filled in whenever arming was refused, so a flight log shows
        // why a canopy is still closed instead of needing a second flight to find out.
        public static bool TryArm(ModuleParachute chute, bool enabled, bool descending, double groundAltitude,
            out string reason)
        {
            reason = "";
            if (chute == null) { reason = "kein Schirm"; return false; }
            if (!enabled) { reason = "Automatik aus"; return false; }
            if (!descending) { reason = "kein Sinkflug"; return false; }
            if (chute.deploymentState != ModuleParachute.deploymentStates.STOWED)
            { reason = "Zustand " + chute.deploymentState; return false; }
            if (!Allowed(chute.deploymentSafeState))
            { reason = "Ausloesung unsicher (" + chute.deploymentSafeState + ")"; return false; }
            SetOpeningHeight(chute, groundAltitude);
            chute.Deploy();
            return true;
        }

        // Opens the canopy once the mod's own rule says it is due, instead of waiting for KSP's state
        // machine to agree. Arming alone was not enough: in a real flight the canopy carried a 3000 m
        // opening height and still only half-opened at 500 m, at the moment the descent happened to
        // slow to 266 m/s - KSP keeps its own counsel about when a canopy may inflate, and MechJeb,
        // which also only arms, cannot do better. From SEMIDEPLOYED on, KSP's own update animates and
        // inflates the canopy, so nothing about the deployment itself is bypassed.
        //
        // Due means below OpenAboveGround either measured against the ground or against the sea,
        // which is the same rule KSP uses in ShouldDeploy.
        public static bool TryOpen(ModuleParachute chute, bool enabled, bool descending, double altitudeAsl,
            double groundAltitude, out string reason)
        {
            return TryOpen(chute, enabled, descending, altitudeAsl, groundAltitude, 0, out reason);
        }

        public static bool TryOpen(ModuleParachute chute, bool enabled, bool descending, double altitudeAsl,
            double groundAltitude, double speed, out string reason)
        {
            reason = "";
            if (chute == null || !enabled) { reason = "Automatik aus"; return false; }
            if (chute.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED
                || chute.deploymentState == ModuleParachute.deploymentStates.DEPLOYED
                || chute.deploymentState == ModuleParachute.deploymentStates.CUT) return false;
            if (!descending) { reason = "kein Sinkflug"; return false; }
            if (!PortMathFinite(altitudeAsl)) { reason = "keine Hoehe"; return false; }
            double ground = PortMathGround(groundAltitude);
            double height = OpeningHeight(OpenAboveGround, speed);
            if (altitudeAsl >= ground + height && altitudeAsl >= height) return false;
            if (chute.deploymentState == ModuleParachute.deploymentStates.STOWED) chute.Deploy();
            chute.deploymentState = ModuleParachute.deploymentStates.SEMIDEPLOYED;
            return true;
        }

        private static bool PortMathFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // The sea floor under the ocean is far below zero; for a height above the ground the water
        // surface is the reference.
        private static double PortMathGround(double groundAltitude)
        {
            return PortMathFinite(groundAltitude) && groundAltitude > 0 ? groundAltitude : 0;
        }
    }
}
