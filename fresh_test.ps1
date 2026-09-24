$ErrorActionPreference = 'Stop'
$managed = 'H:\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed'
$compiler = 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$files = @('src/DescentTypes.cs','src/GuidanceConfig.cs','src/DescentPrediction.cs',
    'src/CaptureBraking.cs','src/DescentGuidance.cs','src/GroundScan.cs','tests/GuidanceTests.cs')
$refs = @('Assembly-CSharp','UnityEngine','UnityEngine.CoreModule') | ForEach-Object { '/reference:' + (Join-Path $managed ($_.Name + '.dll')) }
& $compiler /nologo /target:exe /warn:4 /nowarn:0436,0649 ('/out:build\FreshGuidanceTests.exe') @refs (@files | ForEach-Object { Join-Path (Get-Location) $_ })
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
$env:PATH = $managed + ';' + $env:PATH
$out = & (Join-Path (Get-Location) 'build\FreshGuidanceTests.exe') $managed 2>&1
$failIdx = @($out | Select-String -Pattern 'FAIL' | ForEach-Object { $_.LineNumber })
foreach ($i in $failIdx) {
    Write-Output ('--- FAIL bei Zeile ' + $i + ' ---')
    $start = [Math]::Max(0, $i - 6); $end = [Math]::Min($out.Count - 1, $i + 5)
    $out[$start..$end] | ForEach-Object { Write-Output $_ }
}
Write-Output ('Test exit: ' + $LASTEXITCODE)
