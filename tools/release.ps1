# Baut den Mod und packt ihn als Release-Archiv: PhysStageRecovery-<version>-KSP<version>.zip
#
# Im Archiv liegt der Ordner GameData/PhysStageRecovery, wie er zum Installieren in den
# KSP-Wurzelordner entpackt wird. Daneben entsteht eine .sha256-Datei zum Pruefen.
#
# Die DLL im Archiv ist reproduzierbar: build.ps1 uebersetzt mit /deterministic+, zweimal bauen
# ergibt dieselbe Datei. Das ZIP selbst ist es nicht - ein Archiv traegt Zeitstempel seiner
# Eintraege -, deshalb prueft man nach dem Packen den Hash der DLL, nicht den des Archivs.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

# Version aus dem Paket, damit Archivname und Inhalt nicht auseinanderlaufen koennen.
$versionFile = Join-Path $root 'packaging\PhysStageRecovery.version'
$text = Get-Content -LiteralPath $versionFile -Raw
$major = [regex]::Match($text, '"MAJOR"\s*:\s*(\d+)').Groups[1].Value
$minor = [regex]::Match($text, '"MINOR"\s*:\s*(\d+)').Groups[1].Value
$patch = [regex]::Match($text, '"PATCH"\s*:\s*(\d+)').Groups[1].Value
$ksp = ($text -split '"KSP_VERSION"')[1]
$kspMajor = [regex]::Match($ksp, '"MAJOR"\s*:\s*(\d+)').Groups[1].Value
$kspMinor = [regex]::Match($ksp, '"MINOR"\s*:\s*(\d+)').Groups[1].Value
$kspPatch = [regex]::Match($ksp, '"PATCH"\s*:\s*(\d+)').Groups[1].Value
$name = 'PhysStageRecovery-' + $major + '.' + $minor + '.' + $patch + '-KSP' + $kspMajor + '.' + $kspMinor + '.' + $kspPatch
$zip = Join-Path $root ($name + '.zip')

Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($zip + '.sha256') -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $root 'dist\GameData') -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
($hash + '  ' + $name + '.zip') | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
$count = $archive.Entries.Count
$archive.Dispose()

$dll = (Get-FileHash -LiteralPath (Join-Path $root 'dist\GameData\PhysStageRecovery\Plugins\PhysStageRecovery.dll') -Algorithm SHA256).Hash
Write-Output ('Release: ' + $zip)
Write-Output ('  ' + $count + ' Dateien, ' + [math]::Round((Get-Item $zip).Length / 1KB, 1) + ' KB')
Write-Output ('  DLL   SHA256 ' + $dll)
Write-Output ('  Archiv SHA256 ' + $hash)
