# Wartet, bis der Nutzer KSP schliesst, und installiert dann die gebaute DLL.
# Damit muss niemand auf einen Zeitpunkt warten: Spiel beenden, der Rest passiert von selbst.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$deadline = (Get-Date).AddMinutes(30)
$gone = $false
while ((Get-Date) -lt $deadline)
{
    if (!(Get-Process -Name KSP_x64,KSP -ErrorAction SilentlyContinue)) { $gone = $true; break }
    Start-Sleep -Seconds 5
}
if (!$gone) { Write-Output 'KSP laeuft nach 30 Minuten noch - nicht installiert.'; exit 1 }
Start-Sleep -Seconds 5
Set-Location $root
for ($attempt = 1; $attempt -le 5; $attempt++)
{
    try
    {
        & .\install.ps1
        $built = (Get-FileHash (Join-Path $root 'dist\GameData\PhysStageRecovery\Plugins\PhysStageRecovery.dll')).Hash
        $installed = (Get-FileHash 'H:\Steam\steamapps\common\Kerbal Space Program\GameData\PhysStageRecovery\Plugins\PhysStageRecovery.dll').Hash
        Write-Output ('Installiert. Build == KSP: ' + ($built -eq $installed) + '  SHA256 ' + $installed)
        exit 0
    }
    catch
    {
        Write-Output ('Versuch ' + $attempt + ' fehlgeschlagen: ' + $_.Exception.Message)
        Start-Sleep -Seconds 15
    }
}
Write-Output 'Installation nicht moeglich.'
exit 1
