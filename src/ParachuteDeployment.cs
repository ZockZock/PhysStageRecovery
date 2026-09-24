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

        // Moves the part's own opening height to OpenAboveGround. Safe to call every measurement.
        public static float SetOpeningHeight(ModuleParachute chute, double groundAltitude)
        {
            if (chute == null) return 0;
            if (Math.Abs(chute.deployAltitude - OpenAboveGround) > 0.5f) chute.deployAltitude = OpenAboveGround;
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
            reason = "";
            if (chute == null || !enabled) { reason = "Automatik aus"; return false; }
            if (chute.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED
                || chute.deploymentState == ModuleParachute.deploymentStates.DEPLOYED
                || chute.deploymentState == ModuleParachute.deploymentStates.CUT) return false;
            if (!descending) { reason = "kein Sinkflug"; return false; }
            if (!PortMathFinite(altitudeAsl)) { reason = "keine Hoehe"; return false; }
            double ground = PortMathGround(groundAltitude);
            if (altitudeAsl >= ground + OpenAboveGround && altitudeAsl >= OpenAboveGround) return false;
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
