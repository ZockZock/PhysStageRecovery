param([string]$KspDir = 'H:\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$managed = Join-Path $KspDir 'KSP_x64_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'KSP-Bibliotheken fehlen.' }
$compiler = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Roslyn-C#-Compiler nicht gefunden. Visual Studio Build Tools installieren und den Compilerpfad anpassen.' }
$outputDir = Join-Path $projectRoot 'dist\GameData\PhysStageRecovery\Plugins'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path (Split-Path $outputDir) 'PluginData') -Force | Out-Null
# The language files sit next to the DLL, where KSP's Localizer looks for a mod's Localization node.
New-Item -ItemType Directory -Path (Join-Path (Split-Path $outputDir) 'Localization') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/Localization/en-us.cfg'),(Join-Path $projectRoot 'packaging/Localization/de-de.cfg') -Destination (Join-Path (Split-Path $outputDir) 'Localization') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/PhysStageRecovery.version') -Destination (Split-Path $outputDir) -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/settings.default.cfg') -Destination (Join-Path (Split-Path $outputDir) 'PluginData') -Force
$refs = @('Assembly-CSharp','Assembly-CSharp-firstpass','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule','UnityEngine.UIModule','UnityEngine.UI','UnityEngine.AnimationModule','UnityEngine.TextRenderingModule')
$compilerArgs = @('/nologo','/target:library','/optimize+','/warn:4','/deterministic+',('/out:' + (Join-Path $outputDir 'PhysStageRecovery.dll')))
foreach ($ref in $refs) { $compilerArgs += '/reference:' + (Join-Path $managed ($ref + '.dll')) }
$harmony = Join-Path $KspDir 'GameData\000_Harmony\0Harmony.dll'
if (!(Test-Path -LiteralPath $harmony)) { throw 'Harmony 2 (GameData/000_Harmony/0Harmony.dll) wird für den Fallschirmschutz benötigt.' }
$compilerArgs += '/reference:' + $harmony
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY.md') -Destination (Split-Path $outputDir) -Force
# Ship the corresponding source with the GPL binary, without game or other mod DLLs.
Compress-Archive -LiteralPath (Join-Path $projectRoot 'src'),(Join-Path $projectRoot 'tests'),(Join-Path $projectRoot 'tools'),(Join-Path $projectRoot 'packaging'),(Join-Path $projectRoot 'BoosterWatch.csproj'),(Join-Path $projectRoot 'build.ps1'),(Join-Path $projectRoot 'test.ps1'),(Join-Path $projectRoot 'install.ps1'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY.md'),(Join-Path $projectRoot 'README.md') -DestinationPath (Join-Path (Split-Path $outputDir) 'PhysStageRecovery-Source.zip') -Force
Write-Output ('Build erfolgreich: ' + (Join-Path $outputDir 'PhysStageRecovery.dll'))
