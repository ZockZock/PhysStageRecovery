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
# The texts: the English table in the DLL against the language files, German against English, and
# every tag used in src against the table. No KSP, no Unity - it reads the files directly.
$locExe = Join-Path $testDir 'LocalizationTests.exe'
& $compiler /nologo /target:exe ('/out:' + $locExe) (Join-Path $PSScriptRoot 'src\LocalizationTable.cs') (Join-Path $PSScriptRoot 'tests\LocalizationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Localization test compilation failed.' }
& $locExe $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Localization tests failed.' }
# Which stages are taken over: canopies always count, a powered landing needs an engine and a control
# module (without one KSP's engines ignore the throttle). Plain logic, no KSP.
$acceptExe = Join-Path $testDir 'TrackingAcceptanceTests.exe'
& $compiler /nologo /target:exe ('/out:' + $acceptExe) (Join-Path $PSScriptRoot 'src\TrackingAcceptance.cs') (Join-Path $PSScriptRoot 'tests\TrackingAcceptanceTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tracking acceptance test compilation failed.' }
& $acceptExe
if ($LASTEXITCODE -ne 0) { throw 'Tracking acceptance tests failed.' }
$harmony = Join-Path $KspDir 'GameData\000_Harmony\0Harmony.dll'
# Die Bodenspur als Gegenprobe zu KSPs Geschwindigkeitsanzeige: Fenster, Datumsgrenze, Ausreisser.
# Reine Arithmetik, kein KSP.
$trackExe = Join-Path $testDir 'GroundTrackTests.exe'
& $compiler /nologo /target:exe ('/out:' + $trackExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\GroundTrack.cs') (Join-Path $PSScriptRoot 'tests\GroundTrackTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Ground track test compilation failed.' }
& $trackExe
if ($LASTEXITCODE -ne 0) { throw 'Ground track tests failed.' }
# Der eigene Boden unter fernen Boostern: wann er gebaut wird und welche Form sein Netz hat.
# Reine Arithmetik, kein KSP.
$patchExe = Join-Path $testDir 'GroundPatchPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $patchExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\GroundPatchPolicy.cs') (Join-Path $PSScriptRoot 'src\GroundField.cs') (Join-Path $PSScriptRoot 'tests\GroundPatchPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Ground patch test compilation failed.' }
& $patchExe
if ($LASTEXITCODE -ne 0) { throw 'Ground patch tests failed.' }
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

# Die Bergungsentscheidung der Triebwerkslandung beim Abschalten in Schnitthoehe. Reine Logik.
$cutoffExe = Join-Path $testDir 'CutoffPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $cutoffExe) (Join-Path $PSScriptRoot 'src\CutoffPolicy.cs') (Join-Path $PSScriptRoot 'src\TouchdownPolicy.cs') (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'tests\CutoffPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cutoff tests did not compile.' }
& $cutoffExe
if ($LASTEXITCODE -ne 0) { throw 'Cutoff tests failed.' }
# Steuerflaechen im Rueckwaertsflug umkehren. Reine Logik.
$surfaceExe = Join-Path $testDir 'ControlSurfacePolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $surfaceExe) (Join-Path $PSScriptRoot 'src\ControlSurfacePolicy.cs') (Join-Path $PSScriptRoot 'tests\ControlSurfacePolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Control surface tests did not compile.' }
& $surfaceExe
if ($LASTEXITCODE -ne 0) { throw 'Control surface tests failed.' }
# Rumpfauftrieb aus der gemessenen Beschleunigung. Reine Arithmetik.
$liftExe = Join-Path $testDir 'LiftPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $liftExe) (Join-Path $PSScriptRoot 'src\LiftPolicy.cs') (Join-Path $PSScriptRoot 'tests\LiftPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Lift tests did not compile.' }
& $liftExe
if ($LASTEXITCODE -ne 0) { throw 'Lift tests failed.' }

$touchdownExe = Join-Path $testDir 'TouchdownTests.exe'
& $compiler /nologo /target:exe ('/out:' + $touchdownExe) (Join-Path $PSScriptRoot 'src\RecoveryPolicy.cs') (Join-Path $PSScriptRoot 'src\TouchdownPolicy.cs') (Join-Path $PSScriptRoot 'tests\TouchdownTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Touchdown tests did not compile.' }
& $touchdownExe
if ($LASTEXITCODE -ne 0) { throw 'Touchdown tests failed.' }

# The landing law is plain arithmetic: the whole approach flies against a closed-form model here,
# with no KSP and no Unity.
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

$windowExe = Join-Path $testDir 'WindowOpenPolicyTests.exe'
& $compiler /nologo /target:exe ('/out:' + $windowExe) (Join-Path $PSScriptRoot 'src/WindowOpenPolicy.cs') (Join-Path $PSScriptRoot 'tests/WindowOpenPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Window policy tests did not compile.' }
& $windowExe
if ($LASTEXITCODE -ne 0) { throw 'Window policy tests failed.' }



