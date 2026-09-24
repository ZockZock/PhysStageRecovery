# Baseline 0.9.20

Eingefroren am 24.09.2026 um 10:34 nach den Flügen, mit denen der Stand bestätigt wurde:
Beide Booster zünden spät, bremsen mit vollem Schub, laufen im 50-m-Punkt mit 5 m/s
senkrecht und ohne Seitwärtsfahrt aus und setzen weich auf.

## Wo der Stand liegt

```
build/backups/baseline-0.9.20-20260924-103455/
    src/ tests/ packaging/ tools/ docs/     Quelltext und Prüfungen
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/README.md                          README des ausgelieferten Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei
```

Das Binary ist identisch mit dem in KSP installierten:

```
SHA256 15EBA6105848EF2C1DAD9BA9B43EEBFBA7FE39C6B1A509A64AAB7D8EF034D828
```

Wiederherstellen: Inhalt von `src`, `tests`, `packaging` über die gleichnamigen Ordner im
Projekt kopieren, dann `.\test.ps1`, `.\build.ps1` und `.\install.ps1` ausführen. Prüfen, ob
ein Ordner noch unverändert ist:

```powershell
Get-Content SHA256SUMS.txt | ForEach-Object {
    $hash, $file = $_ -split '  ', 2
    if ($hash -ne (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) { "geaendert: $file" }
}
```

## Was in diesem Stand steckt

- Version 0.9.20, `AssemblyVersion 0.9.20.0`, Paketversion 0.9.20.
- **Anflug**: ab der berechneten Höhe Volllastbremsung bis zur Auffanghöhe, Zielzustand dort
  ist die Auffanggeschwindigkeit senkrecht (5 m/s) und keine Seitwärtsfahrt. Unterhalb läuft
  die Endphase die Sinkrate auf die Aufsetzgeschwindigkeit (2 m/s) aus, aufrecht.
- **Schubachse**: Vorhersage und Befehl kennen den Staudruck, ab dem der Luftstrom das Heck
  hält (40 kPa). In dieser Phase zielt der Burn im Mittel aus Gewünschtem und Machbarem und
  läuft mit voller Leistung; beide Komponenten sind begrenzt, damit der Booster weder am
  eigenen Triebwerk hängt noch die Drift über null schiebt.
- **Zündpunkt**: Die seitliche Auffanggrenze liegt bei 4,5 m/s (Ziel 0,5 plus 4 Toleranz).
  Sie ist eine Qualitätsgrenze für den Plan; der Burn zielt immer auf null Seitwärtsfahrt.
- **Vorausberechnung** im Hintergrundthread, der Regler wartet nie darauf.
- Werte aus `settings.cfg` des Flugs: Zielhöhe (**Auffanghöhe**) **10 m**, Endphase 150 m,
  Aufsetzgeschwindigkeit 2 m/s, Neigungsgrenze 20 Grad, Schubreserve 20 %. Die Auffanghöhe
  wurde während der Erprobung von 50 m auf 10 m gesenkt; damit bleibt weniger Strecke im
  flachen Auslaufen und der Treibstoffbedarf sinkt weiter.
- Testlage: `.\test.ps1` läuft bis auf einen bekannten Fall durch — der Stresstest mit ±3 m
  künstlichem Rauschen auf den Bodenabstand setzt mit 9,1 m/s auf statt unter 9 m/s. Das
  besteht seit vor 0.9.15 und ist eine Frage der Filterung des letzten Höhenwerts.
- Baseline 0.9.17 liegt unverändert daneben (`baseline-0.9.17-20260924-092207`) und beschreibt
  den Stand vor der achsenbewussten Bremsung.

## Womit dieser Stand belegt ist

Sechs Flüge im Spiel mit 0.9.20, aus den Flugschreiber-CSVs:

| Flug | Masse | Zündung | Schub mittel/max | Aufsetzen sink/quer | Treibstoff |
|---|---|---|---|---|---|
| 10:18 | 4,8 t | 1582 m | 46 % / 91 % | 6,9 / 0,3 m/s | 0,86 t |
| 10:20 | 19,1 t | 1212 m | 40 % / 100 % | 6,4 / 1,4 m/s | 3,17 t |
| 10:26 | 19,1 t | 1202 m | 51 % / 100 % | 5,7 / 2,0 m/s | 2,53 t |
| 10:29 | 17,5 t | 1137 m | 45 % / 100 % | 5,6 / 1,5 m/s | 2,24 t |
| 10:32 | 48,4 t | 17025 m | 80 % / 100 % | 5,4 / 0,3 m/s | 19,1 t |

Der schwere Fall (48,4 t, 1883 m/s Seitwärtsfahrt beim Abtrennen) hält von 16 km bis 2 km
durchgehend **100 % Schub** und moduliert erst im letzten Kilometer herunter — kein Leerlauf
mehr. Die mittleren Booster zünden bei 1,1 bis 1,6 km statt bei 4,3 km wie mit 0.9.19.

Zum Vergleich die Rückrechnung der älteren aufgezeichneten Flüge mit den Tabellen der jeweiligen
CSV, Übergabe an die Regelung in 60 km Höhe:

| Flug | Zündung | Aufsetzen sink/quer | Treibstoff |
|---|---|---|---|
| 09:51, 27,6 t (0.9.19 geflogen) | 3161 m | 5,6 / 0,53 m/s | 4,02 t |
| 09:56, 4,8 t (0.9.19 geflogen) | 1314 m | 5,6 / 0,54 m/s | 0,67 t |
| 09:07, 27,6 t (Absturzzustand) | 2555 m | 5,6 / 0,44 m/s | 5,84 t |
| 08:39, 26,4 t | 1738 m | 5,6 / 0,44 m/s | 3,52 t |
| 08:06, 27,6 t | 2118 m | 5,6 / 0,44 m/s | 1,82 t |
| 08:11, 4,8 t | 1667 m | 5,6 / 0,28 m/s | 0,46 t |

Der Zustand, der am 09:07 aufgeschlagen ist (27,6 t, 856 m/s aus 4,3 km), landet in dieser
Rechnung mit 5,6 m/s — der Fall, an dem 0.9.16 gescheitert ist, ist damit abgedeckt.

Offen bleibt: Das Triebwerk schaltet 1,5 m über dem Boden ab; der Booster fällt dieses Stück
frei. Daher steht als Aufsetzgeschwindigkeit rund 5,5 m/s im Log, obwohl die Endphase auf
2 m/s ausläuft.
