# Baseline 0.9.17

Eingefroren am 24.09.2026 um 09:22 nach dem Flug, mit dem der Stand bestätigt wurde:
Der schwere Booster zündet wieder früh und **landet**.

## Wo der Stand liegt

```
build/backups/baseline-0.9.17-20260924-092207/
    src/ tests/ packaging/ tools/ docs/     Quelltext und Prüfungen
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei
```

Wiederherstellen: Inhalt von `src`, `tests`, `packaging` über die gleichnamigen Ordner im
Projekt kopieren, dann `.\test.ps1` und `.\build.ps1` und `.\install.ps1` ausführen. Die Datei
`SHA256SUMS.txt` prüft, ob ein Ordner noch unverändert ist:

```powershell
Get-Content SHA256SUMS.txt | ForEach-Object {
    $hash, $file = $_ -split '  ', 2
    $now = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($hash -ne $now) { "geaendert: $file" }
}
```

## Was in diesem Stand steckt

- Version 0.9.17, `AssemblyVersion 0.9.17.0`, Paketversion 0.9.17.
- Seitliche Auffanggrenze des Planers: **0,5 m/s** über dem Ziel von 2 m/s. Eng mit Absicht:
  Sie hält die Zündung bei Eintritten mit viel Seitwärtsfahrt früh, und früh ist das, was
  diese Eintritte landen lässt. Die Erweiterung auf 4 m/s (0.9.16) ist hier zurückgenommen.
- Vorausberechnung im Hintergrundthread, der Regler wartet nie darauf.
- Zielhöhe 50 m, Endphase 150 m, Aufsetzgeschwindigkeit 2 m/s (Werte aus `settings.cfg`;
  die Voreinstellungen der Quelle sind 100 m / 50 m / 5 m/s).
- Testlage: `.\test.ps1` läuft bis auf einen bekannten Fall durch — der Stresstest mit
  ±3 m künstlichem Rauschen auf den Bodenabstand setzt mit 9,5 m/s auf statt unter 9 m/s.
  Das besteht seit vor 0.9.15 und ist eine Frage der Filterung des letzten Höhenwerts.

## Was dieser Stand noch nicht kann

Die Vorhersage nimmt an, dass der Triebwerksschub in die befohlene Richtung wirkt. Dieses
Fahrzeug kann die befohlene Achse oberhalb etwa eines Kilometers nicht halten; der Luftstrom
stellt es rückwärts an. Deshalb überschätzt die Vorhersage bei schnellen Eintritten die
verfügbare Bremsleistung nach oben (Aufschlag vom 24.09.2026, 09:07). Die frühe Zündung, die
dieser Stand fliegt, ist die sichere Antwort darauf, aber sie ist nicht die begründete: Ein
Plan, der die Schubachse kennt, könnte später und sparsamer zünden, ohne das Risiko einzugehen.
Das ist der nächste Schritt (0.9.18).
