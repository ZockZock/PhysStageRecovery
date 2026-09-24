param([string]$KspDir = 'H:\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name KSP_x64,KSP -ErrorAction SilentlyContinue) { throw 'KSP ist noch gestartet. Bitte vor der Installation beenden.' }
$gameData = [IO.Path]::GetFullPath((Join-Path $KspDir 'GameData')).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $KspDir 'KSP_x64.exe'))) { throw 'Keine KSP-Installation an diesem Pfad.' }
if (!(Test-Path -LiteralPath (Join-Path $gameData '000_Harmony\0Harmony.dll'))) { throw 'Harmony 2 fehlt.' }
$target = [IO.Path]::GetFullPath((Join-Path $gameData 'PhysStageRecovery'))
$legacy = [IO.Path]::GetFullPath((Join-Path $gameData 'BoosterWatch'))
if ($target -ne "$gameData\PhysStageRecovery" -or $legacy -ne "$gameData\BoosterWatch") { throw 'Unerwarteter Installationspfad.' }
$source = Join-Path $PSScriptRoot 'dist\GameData\PhysStageRecovery'
foreach ($relative in @('Plugins\PhysStageRecovery.dll','PhysStageRecovery.version','README.md','LICENSE','THIRD_PARTY.md','PhysStageRecovery-Source.zip','PluginData\settings.default.cfg','Localization\en-us.cfg','Localization\de-de.cfg')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $relative))) { throw "Paket unvollständig: $relative. Zuerst build.ps1 ausführen." }
}
if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath (Join-Path $target 'PhysStageRecovery.version'))) { throw 'Unbekannter Zielordner wird nicht überschrieben.' }
if ((Test-Path -LiteralPath $legacy) -and !(Test-Path -LiteralPath (Join-Path $legacy 'BoosterWatch.version'))) { throw 'Unbekannter alter Ordner wird nicht verschoben.' }
$backupRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build\backups')).TrimEnd('\')
$backup = [IO.Path]::GetFullPath((Join-Path $backupRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')))
if (!$backup.StartsWith($backupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup-Pfad liegt außerhalb des Projekt-Backups.' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$dll = Join-Path $target 'Plugins\PhysStageRecovery.dll'
if (Test-Path -LiteralPath $dll) { Copy-Item -LiteralPath $dll -Destination $backup }
New-Item -ItemType Directory -Path (Join-Path $target 'Plugins'),(Join-Path $target 'PluginData'),(Join-Path $target 'Localization') -Force | Out-Null
$config = Join-Path $target 'PluginData\settings.cfg'
if (!(Test-Path -LiteralPath $config)) {
    $oldConfig = Join-Path $legacy 'PluginData\settings.cfg'
    $configSource = if (Test-Path -LiteralPath $oldConfig) { $oldConfig } else { Join-Path $source 'PluginData\settings.default.cfg' }
    Copy-Item -LiteralPath $configSource -Destination $config
}
$sourceDll = Join-Path $source 'Plugins\PhysStageRecovery.dll'
Copy-Item -LiteralPath $sourceDll -Destination $dll -Force
foreach ($name in @('PhysStageRecovery.version','README.md','LICENSE','THIRD_PARTY.md','PhysStageRecovery-Source.zip')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force
}
foreach ($name in @('en-us.cfg','de-de.cfg')) {
    Copy-Item -LiteralPath (Join-Path $source ('Localization\' + $name)) -Destination (Join-Path $target 'Localization') -Force
}
if ((Get-FileHash -LiteralPath $sourceDll).Hash -ne (Get-FileHash -LiteralPath $dll).Hash) { throw 'Installierte DLL stimmt nicht mit dem Build überein.' }
if (Test-Path -LiteralPath $legacy) {
    $legacyBackup = [IO.Path]::GetFullPath((Join-Path $backup 'BoosterWatch'))
    if ($legacy -ne "$gameData\BoosterWatch" -or !$legacyBackup.StartsWith($backupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ungültiger Migrationspfad.' }
    # Move the verified old mod outside GameData so its DLL cannot load a second time.
    Move-Item -LiteralPath $legacy -Destination $legacyBackup
    Write-Output ('Vorversion außerhalb von GameData gesichert: ' + $legacyBackup)
}
Write-Output ('Installiert und Hash geprüft: ' + $target)
