# Legt den physischen Schnappschuss einer Baseline an: alle versionierten Dateien, die
# ausgelieferten Paketdateien und eine Pruefsumme je Datei.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$version = $args[0]
if (-not $version) { throw 'Version angeben, z. B. 0.9.32' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$snap = if ($args[1]) { [IO.Path]::GetFullPath($args[1]) } else { Join-Path $root ('build\backups\baseline-' + $version + '-' + $stamp) }
New-Item -ItemType Directory -Path $snap -Force | Out-Null

$files = New-Object System.Collections.Generic.List[string]
foreach ($file in (git ls-files)) { $files.Add($file) }
foreach ($file in @('PhysStageRecovery.version', 'README.md', 'Plugins\PhysStageRecovery.dll')) {
    $files.Add('dist\' + $file)
}

$sums = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $source = if ($file.StartsWith('dist\')) { Join-Path (Join-Path $root 'dist\GameData\PhysStageRecovery') $file.Substring(5) } else { Join-Path $root $file }
    if (!(Test-Path -LiteralPath $source)) { throw ('fehlt: ' + $source) }
    $target = Join-Path $snap $file
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $sums.Add($hash + '  ' + $file)
}
$sums | Set-Content -LiteralPath (Join-Path $snap 'SHA256SUMS.txt') -Encoding UTF8

Write-Output ('Schnappschuss: ' + $snap)
Write-Output ('Dateien: ' + $files.Count)

# Pruefen: jede Zeile gegen die Datei im Schnappschuss.
$bad = 0
foreach ($line in Get-Content -LiteralPath (Join-Path $snap 'SHA256SUMS.txt')) {
    $hash, $file = $line -split '  ', 2
    if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $snap $file) -Algorithm SHA256).Hash) { Write-Output ('ABWEICHUNG: ' + $file); $bad++ }
}
Write-Output ('Abweichungen: ' + $bad)
Write-Output ('DLL SHA256: ' + (Get-FileHash -LiteralPath (Join-Path $snap 'dist\Plugins\PhysStageRecovery.dll') -Algorithm SHA256).Hash)
