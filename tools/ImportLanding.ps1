# Reproducible source extraction; input is the pinned MechJeb checkout documented in THIRD_PARTY.md.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$up = Join-Path $root 'build/mechjeb-upstream'
$notice = "// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.`n"
function Write-Port($name, $body) { [IO.File]::WriteAllText((Join-Path $root ('src/' + $name)), $notice + $body) }
foreach ($name in @('FinalDescent','UntargetedDeorbit')) {
    $s = Get-Content -Raw (Join-Path $up "MechJeb2/LandingAutopilot/$name.cs")
    $s = $s.Replace('namespace MuMech', 'namespace BoosterWatch.MechJebPort')
    $s = $s.Replace('public class ', 'internal class ')
    $s = [regex]::Replace($s, '/\*.*?\*/', '', 'Singleline')
    $s = $s.Replace('using KSP.Localization;', '')
    $s = [regex]::Replace($s, 'Status = Localizer.Format\("#MechJeb_LandingGuidance_Status9",\s*VesselState.AltitudeBottom.ToString\("F0"\)\);', 'Status = "Endanflug: " + VesselState.AltitudeBottom.ToString("F0") + " m";')
    $s = $s.Replace('Status = Localizer.Format("#MechJeb_LandingGuidance_Status16");', 'Status = "Deorbit-Bremsung";')
    Write-Port "MJ$name.cs" $s
}
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/MechJebModuleLandingAutopilot.cs')
$start = $s.IndexOf('    internal class GravityTurnDescentSpeedPolicy')
$end = $s.IndexOf('    // Class to hold all', $start)
Write-Port 'MJGravityTurn.cs' ("using System;`nnamespace BoosterWatch.MechJebPort {`n" + $s.Substring($start,$end-$start) + "}`n")
$s = Get-Content -Raw (Join-Path $up 'MechJebLib/Control/PIDLoop2.cs')
$s = $s.Replace('using static MechJebLib.Utils.Statics;', 'using static BoosterWatch.MechJebPort.PortMath;').Replace('namespace MechJebLib.Control','namespace BoosterWatch.MechJebPort').Replace('public class PIDLoop2 : IPIDLoop','internal class PIDLoop2')
Write-Port 'MJPIDLoop2.cs' $s
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/AttitudeControllers/DirectionTracker.cs')
$s = $s.Replace('using static MechJebLib.Utils.Statics;', 'using static BoosterWatch.MechJebPort.PortMath;').Replace('namespace MuMech.AttitudeControllers','namespace BoosterWatch.MechJebPort').Replace('public class DirectionTracker','internal class DirectionTracker')
Write-Port 'MJDirectionTracker.cs' $s
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/AttitudeControllers/BetterController.cs')
$s = $s.Substring(0, $s.IndexOf('        public override void GUI()')) + "    }`n}`n"
$s = [regex]::Replace($s, '(?m)^extern alias.*\r?\n|^using JetBrainsAnnotations.*\r?\n|^using KSP.Localization.*\r?\n|^using MechJebLib.Control.*\r?\n', '')
$s = [regex]::Replace($s, '(?m)^\s*\[UsedImplicitly.*\r?\n', '')
$s = $s.Replace('using static MechJebLib.Utils.Statics;', 'using static BoosterWatch.MechJebPort.PortMath;').Replace('namespace MuMech.AttitudeControllers','namespace BoosterWatch.MechJebPort')
$s = [regex]::Replace($s, 'public readonly EditableDouble (\w+) = new EditableDouble\(([^;]*)\);', 'public double $1 = $2;')
$s = $s.Replace('.Val','').Replace('Ac.OmegaTarget[i].IsFinite()', 'IsFinite(Ac.OmegaTarget[i])')
# Inject only measured attitude/inertia/rate; the controller equations remain unchanged.
$s = $s.Replace('        private Vessel _vessel => Ac.Vessel;', '')
$s = [regex]::Replace($s, '            Transform vesselTransform = _vessel.ReferenceTransform;\s*QuaternionD currentAttitude = \(QuaternionD\)vesselTransform.transform.rotation \* MathExtensions.Euler\(-90, 0, 0\);', '            QuaternionD currentAttitude = Ac.CurrentAttitude;')
$s = $s.Replace('_vessel.MOI', 'Ac.MomentOfInertia').Replace('_vessel.angularVelocityD', 'Ac.AngularVelocity')
Write-Port 'MJBetterController.cs' $s
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/MathExtensions.cs')
$start = $s.IndexOf('        public static Vector3d EulerAngles(')
$end = $s.IndexOf('        public static Vector3d RotateTowards(', $start)
Write-Port 'MJMathExtensions.cs' ("using System;`nusing UnityEngine;`nusing static BoosterWatch.MechJebPort.PortMath;`nnamespace BoosterWatch.MechJebPort { internal static class MathExtensions {`n" + $s.Substring($start,$end-$start) + "} }`n")
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/MechJebModuleThrustController.cs')
$start = $s.IndexOf('            if (Tmode != TMode.OFF && VesselState.ThrustAvailable > 0)')
$end = $s.IndexOf('            // Only command the throttle', $start)
$drive = $s.Substring($start,$end-$start)
# Original incremental throttle assumes 50 updates/s. Normalize the increment for PSR physics warp.
$drive = $drive.Replace('double tAct = _pid.Compute(tErr);', 'double tAct = _pid.Compute(tErr) * (TimeWarp.fixedDeltaTime / 0.02); // PSR: preserve gain during physics warp')
Write-Port 'MJThrustDrive.cs' ("using System;`nusing UnityEngine;`nnamespace BoosterWatch.MechJebPort { internal partial class MechJebModuleThrustController { public void Drive(FlightCtrlState s) {`n" + $drive + "s.mainThrottle = Mathf.Clamp01(TargetThrottle);`n} } }`n")
$s = Get-Content -Raw (Join-Path $up 'MechJeb2/PIDController.cs')
$start = $s.IndexOf('    public class PIDController : IConfigNode')
$end = $s.IndexOf('    public class PIDControllerV2')
$s = $s.Substring($start,$end-$start).Replace('public class PIDController : IConfigNode', 'internal class PIDController : IConfigNode')
Write-Port 'MJPIDController.cs' ("using System;`nnamespace BoosterWatch.MechJebPort {`n" + $s + "}`n")
