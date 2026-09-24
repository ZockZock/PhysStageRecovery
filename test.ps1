param([string]$KspDir = 'H:\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
$compiler = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$testDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$testExe = Join-Path $testDir 'RecoveryPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $testExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'tests\RecoveryPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Recovery policy tests failed.' }
$descentExe = Join-Path $testDir 'DescentGateTests.exe'
& $compiler /nologo /target:exe ('/out:' + $descentExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\DescentGate.cs') (Join-Path $PSScriptRoot 'tests\DescentGateTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Descent test compilation failed.' }
& $descentExe
if ($LASTEXITCODE -ne 0) { throw 'Descent gate tests failed.' }
# The landing reserve is arithmetic only: which share of an engine's own tanks holds it back, what
# that share is worth and the text the menu shows. No KSP, no Unity.
$reserveExe = Join-Path $testDir 'FuelReserveTests.exe'
& $compiler /nologo /target:exe ('/out:' + $reserveExe) (Join-Path $PSScriptRoot 'src\FuelReserve.cs') (Join-Path $PSScriptRoot 'tests\FuelReserveTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Reserve test compilation failed.' }
& $reserveExe
if ($LASTEXITCODE -ne 0) { throw 'Landing reserve tests failed.' }
$harmony = Join-Path $KspDir 'GameData\000_Harmony\0Harmony.dll'
$guardExe = Join-Path $testDir 'ParachuteGuardTests.exe'
Copy-Item -LiteralPath $harmony -Destination (Join-Path $testDir '0Harmony.dll') -Force
& $compiler /nologo /target:exe ('/reference:' + $harmony) ('/out:' + $guardExe) (Join-Path $PSScriptRoot 'src\ParachuteGuard.cs') (Join-Path $PSScriptRoot 'src\ParachuteDeployment.cs') (Join-Path $PSScriptRoot 'tests\ParachuteGuardTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Guard integration test compilation failed.' }
& $guardExe
if ($LASTEXITCODE -ne 0) { throw 'Guard integration tests failed.' }
$warpExe = Join-Path $testDir 'RailWarpGuardTests.exe'
& $compiler /nologo /target:exe ('/reference:' + $harmony) ('/out:' + $warpExe) (Join-Path $PSScriptRoot 'src\RailWarpGuard.cs') (Join-Path $PSScriptRoot 'tests\RailWarpGuardTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Warp test compilation failed.' }
& $warpExe
if ($LASTEXITCODE -ne 0) { throw 'Warp integration tests failed.' }
$managed = Join-Path $KspDir 'KSP_x64_Data\Managed'
$automationExe = Join-Path $testDir 'AutomationTests.exe'
& $compiler /nologo /target:exe ('/out:' + $automationExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\StagePolicy.cs') (Join-Path $PSScriptRoot 'tests\StageTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Automation test compilation failed.' }
& $automationExe
if ($LASTEXITCODE -ne 0) { throw 'Automation tests failed.' }
$settingsExe = Join-Path $testDir 'KspSettingsTests.exe'
& $compiler /nologo /target:exe ('/out:' + $settingsExe) ('/reference:' + (Join-Path $managed 'Assembly-CSharp.dll')) ('/reference:' + (Join-Path $managed 'UnityEngine.dll')) ('/reference:' + (Join-Path $managed 'UnityEngine.CoreModule.dll')) (Join-Path $PSScriptRoot 'src\Settings.cs') (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\VesselRangeTransition.cs') (Join-Path $PSScriptRoot 'src\RecoveryJournal.cs') (Join-Path $PSScriptRoot 'src\StagePolicy.cs') (Join-Path $PSScriptRoot 'tests\KspSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settings test compilation failed.' }
& $settingsExe $managed (Join-Path $testDir 'settings-tests')
if ($LASTEXITCODE -ne 0) { throw 'Settings and range tests failed.' }

$touchdownExe = Join-Path $testDir 'TouchdownTests.exe'
& $compiler /nologo /target:exe ('/out:' + $touchdownExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\TouchdownPolicy.cs') (Join-Path $PSScriptRoot 'tests\TouchdownTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Touchdown tests did not compile.' }
& $touchdownExe
if ($LASTEXITCODE -ne 0) { throw 'Touchdown tests failed.' }

# The predictive landing law is plain arithmetic: the whole approach flies against a closed-form
# model here, with no KSP and no Unity. It runs before the source-port suite so that a failure
# there cannot hide the results of the new law.
#
# The one thing it borrows from the game is Vector3d, because the law works in KSP's world
# coordinates and a private copy of that type would mean the tests exercise a different arithmetic
# from the flight. So the executable is built against Assembly-CSharp and run with the managed
# folder on the library path.
$guidanceExe = Join-Path $testDir 'GuidanceTests.exe'
$guidanceArgs = @('/nologo','/target:exe','/warn:4','/nowarn:0436,0649',('/out:' + $guidanceExe))
foreach ($ref in @('Assembly-CSharp','UnityEngine','UnityEngine.CoreModule')) {
    $guidanceArgs += '/reference:' + (Join-Path $managed ($ref + '.dll'))
}
foreach ($file in @('src/DescentTypes.cs','src/GuidanceConfig.cs','src/DescentPrediction.cs',
    'src/CaptureBraking.cs','src/DescentGuidance.cs','src/GroundScan.cs','tests/GuidanceTests.cs')) {
    $guidanceArgs += Join-Path $PSScriptRoot $file
}
& $compiler @guidanceArgs
if ($LASTEXITCODE -ne 0) { throw 'Guidance tests did not compile.' }
$env:PATH = $managed + ';' + $env:PATH
& $guidanceExe $managed
if ($LASTEXITCODE -ne 0) { throw 'Guidance tests failed.' }

$portExe = Join-Path $testDir 'LandingPortTests.exe'
$portArgs = @('/nologo','/target:exe','/nowarn:0436,0649',('/out:' + $portExe))
foreach ($ref in @('Assembly-CSharp','Assembly-CSharp-firstpass','UnityEngine','UnityEngine.CoreModule')) {
    $portArgs += '/reference:' + (Join-Path $managed ($ref + '.dll'))
}
foreach ($file in @('src/GroundSurface.cs','src/BrakingEnvelope.cs','src/MJFinalDescent.cs','src/MJUntargetedDeorbit.cs','src/MJCoastToDeceleration.cs',
    'src/MJDecelerationBurn.cs','src/MJKillHorizontalVelocity.cs','src/MJGravityTurn.cs','src/ApproachDescentSpeedPolicy.cs','src/MJThrustDrive.cs',
    'src/MJPIDController.cs','src/MJPIDLoop2.cs','src/MJDirectionTracker.cs','src/MJMathExtensions.cs','src/MJPortMath.cs','src/MJBetterController.cs',
    'tests/LandingPortHarness.cs','tests/LandingPortTests.cs')) { $portArgs += Join-Path $PSScriptRoot $file }
& $compiler @portArgs
if ($LASTEXITCODE -ne 0) { throw 'Landing source-port tests did not compile.' }
& $portExe $managed
if ($LASTEXITCODE -ne 0) { throw 'Landing source-port tests failed.' }

$windowExe = Join-Path $testDir 'WindowOpenPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $windowExe) (Join-Path $PSScriptRoot 'src/WindowOpenPolicy.cs') (Join-Path $PSScriptRoot 'tests/WindowOpenPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Window policy tests did not compile.' }
& $windowExe
if ($LASTEXITCODE -ne 0) { throw 'Window policy tests failed.' }



