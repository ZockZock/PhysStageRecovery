# Baut die Messsonde fuer den Terrain-Test (Stufe 4) und legt sie als eigenes,
# jederzeit loeschbares GameData-Verzeichnis neben den Mod. Der Mod selbst bleibt unberuehrt:
# kein Quelltext, keine Einstellung, keine Version wird angefasst.
#
#   .\tools\build-probe.ps1              bauen und installieren
#   .\tools\build-probe.ps1 -Wait        warten, bis KSP geschlossen ist, dann installieren
#   .\tools\build-probe.ps1 -Uninstall   Sonde wieder entfernen
param(
    [string]$KspDir = 'H:\Steam\steamapps\common\Kerbal Space Program',
    [switch]$Wait,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $KspDir 'KSP_x64_Data\Managed'
$compiler = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$target = Join-Path $KspDir 'GameData\PhysStageRecoveryProbe'

if ($Uninstall)
{
    if (Test-Path -LiteralPath $target)
    {
        if (Get-Process -Name KSP_x64,KSP -ErrorAction SilentlyContinue) { throw 'KSP laeuft noch. Erst beenden.' }
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Output ('Sonde entfernt: ' + $target)
    }
    else { Write-Output 'Sonde war nicht installiert.' }
    exit 0
}

if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'KSP-Bibliotheken fehlen.' }
if (!(Test-Path -LiteralPath $compiler)) { throw 'Roslyn-C#-Compiler nicht gefunden.' }

$outputDir = Join-Path $projectRoot 'build\probe'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$dll = Join-Path $outputDir 'PSRTerrainProbe.dll'

$refs = @('Assembly-CSharp','Assembly-CSharp-firstpass','UnityEngine','UnityEngine.CoreModule',
    'UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule',
    'UnityEngine.UIModule','UnityEngine.TextRenderingModule')
$compilerArgs = @('/nologo','/target:library','/optimize+','/warn:4','/deterministic+',('/out:' + $dll))
foreach ($ref in $refs) { $compilerArgs += '/reference:' + (Join-Path $managed ($ref + '.dll')) }
# Harmony wird gebraucht, um KSP daran zu hindern, PQS.target jeden Frame zurueckzusetzen.
$harmony = Join-Path $KspDir 'GameData\000_Harmony\0Harmony.dll'
if (!(Test-Path -LiteralPath $harmony)) { throw 'Harmony 2 (GameData/000_Harmony/0Harmony.dll) fehlt.' }
$compilerArgs += '/reference:' + $harmony
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'terrainprobe') -Filter '*.cs' |
    ForEach-Object { $_.FullName })
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Probe-Build fehlgeschlagen.' }

if ($Wait)
{
    $deadline = (Get-Date).AddMinutes(30)
    while ((Get-Date) -lt $deadline)
    {
        if (!(Get-Process -Name KSP_x64,KSP -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Seconds 5
    }
    Start-Sleep -Seconds 5
}
if (Get-Process -Name KSP_x64,KSP -ErrorAction SilentlyContinue) { throw 'KSP laeuft noch. Mit -Wait wartet das Skript, bis du beendet hast.' }
if (!(Test-Path -LiteralPath (Join-Path $KspDir 'KSP_x64.exe'))) { throw 'Keine KSP-Installation an diesem Pfad.' }

New-Item -ItemType Directory -Path (Join-Path $target 'Plugins'),(Join-Path $target 'PluginData') -Force | Out-Null
$installed = Join-Path $target 'Plugins\PSRTerrainProbe.dll'
Copy-Item -LiteralPath $dll -Destination $installed -Force
if ((Get-FileHash -LiteralPath $dll).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) { throw 'Installierte Sonde stimmt nicht mit dem Build ueberein.' }
Write-Output ('Sonde installiert: ' + $installed)
Write-Output 'Im Flug: F7 baut beide Patches und setzt die Testkugel ab, F6 wechselt die Kamera, Pos1 setzt die Kugel neu, F8 raeumt auf.'
