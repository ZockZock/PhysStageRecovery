# Baseline 0.9.48

Eingefroren am 26.09.2026 nach der Flugserie vom 25.09.2026 (21:13 bis 23:09, 0.9.40 bis 0.9.44, danach Tests mit 0.9.46)
und den Zeitraffer-Tests mit 0.9.47/0.9.48. Kern dieses Stands: Die Kernstufe **gleitet statt zu
brennen**. Ein Trimmtank verlegt dafür den Schwerpunkt, und die Steuerflächen arbeiten im
Rückwärtsflug richtig herum. Der Landeverbrauch fiel so von 12–16 t auf **~2,6–2,9 t**.

## Wo der Stand liegt

```
build/backups/baseline-0.9.48-20260926-075452/
    src/ tests/ packaging/ tools/ docs/     Quelltext, Prüfungen, diese Beschreibung
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE .gitignore .gitattributes
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/README.md                          README des ausgelieferten Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei
```

Das Binary ist identisch mit dem in KSP installierten:

```
SHA256 B2A3AD98FEFF22F21D562B66BAFB2E4B8810E0111EC1E79D60C2E5FB78C8CB14
```

In git ist der Stand der Commit, auf dem der Tag `v0.9.48` liegt. `git checkout v0.9.48 -- src/`
holt den Quelltext zurück. Die Prüfung eines Schnappschussordners steht in `docs/BASELINE-0.9.38.md`.

## Belege aus den Flügen (25.09.2026)

| Flug | Version | Fallschirm-Booster | Kernstufe (Triebwerk) |
|---|---|---|---|
| 21:13 | 0.9.40 | abgestürzt (128 / 275 m/s, Schirm zu tief) | abgestürzt, Rumpf-Abtrieb nicht eingeplant |
| 21:53 | 0.9.41 | geborgen | zündete bei 24 km, Tanks leer, 194 m/s Aufschlag |
| 22:28 | 0.9.42 | geborgen | gelandet, **12,3 t** verbraucht |
| 22:35 | 0.9.42 | geborgen | Absturz 95 m/s (Rückwärts-Cd·A 2,2 statt 0,8) |
| 22:53 | 0.9.43 | geborgen | Trimmtank 6,4 t, Gleiten 45 → 2 km, **2,85 t**; 4,4 m/s seitlich → nicht geborgen |
| 23:09 | 0.9.44 | geborgen, Schirm aber ab 9,4 km halb offen (→ 0.9.46) | Trimmtank 3,2 t, **2,65 t**, 2,5 m/s ab; 6,2 m/s seitlich → nicht geborgen |

- Nach 0.9.46 hat der Spieler die Schirmlandung als „perfekt“ bestätigt: Die Schirme gehen erst
  6 s Flugzeit vor der eingestellten Höhe scharf.
- 0.9.47 (Zeitraffer bis zum Eintritt) funktionierte im Spiel, war aber zu restriktiv. 0.9.48 lässt
  den normalen Zeitraffer bis 30 s Spielzeit vor dem Eintritt frei; das ist **noch nicht geflogen**.
- Die Korrektur der Seitenfahrt aus 0.9.45 (sanftes Aufrichten, Mindestzeit 3 s unter 5 m/s) ist
  nur im Testrahmen belegt (Gegenfahrt ≤ 0,4 m/s statt 3,6–4,5), **im Spiel noch nicht bestätigt**.

## Was in diesem Stand steckt (0.9.39 bis 0.9.48)

Die Einzelheiten stehen im README je Version. Kurz:

- **0.9.39** Seitwärtsgeschwindigkeit nach Floating-Origin-Verschiebung über die eigene Bodenspur.
- **0.9.40** Mitdrehendes Bezugssystem und feines Gelände um ferne Booster (`RotatingFrameHold`,
  `TerrainDetailBubble`), Abschaltentscheidung (`CutoffPolicy`). Die MechJeb-Landekette,
  `TerrainProbe` und `FuelReserveAttach` wurden entfernt, dazu 16 Fehler aus der Code-Durchsicht behoben.
  Die leeren Platzhalterdateien dieser Aufräumrunde sind mit diesem Commit aus dem Repository entfernt.
- **0.9.41** Öffnungshöhe wächst mit der Sinkrate, Rumpf-Abtrieb gemessen und eingeplant
  (`LiftPolicy`), Steuerflächen im Rückwärtsflug umgekehrt (`ControlSurfaceFlow`).
- **0.9.42** Gleiten geht vor Zünden, Vorhersage mit Gleitabschnitt, Zeitraffer → Physikwarp
  (`RailWarpGuard`).
- **0.9.43** Rückwärts-Cd·A direkt aus den Widerstandswürfeln, Trimmtank (`FuelTrim`, 1,5 t/s).
- **0.9.44 / 0.9.45** Eigene Frist für die Seitenfahrt, Rest unter 5 m/s sanft über ≥ 3 s.
- **0.9.46** Schirme erst kurz vor der Öffnungshöhe scharf (`ParachuteDeployment.ArmingHeight`).
- **0.9.47 / 0.9.48** Normaler Zeitraffer über der Atmosphäre bis 30 s vor dem Eintritt
  (`WarpWindow`), danach Physikwarp bis 4x, während einer Triebwerkslandung kein Warp.

## Prüfungen

Alle Testprogramme laufen durch, mit einer bekannten Ausnahme in `GuidanceTests`
(65 bestanden, 1 offen):

```
FAIL: Aufsetzgeschwindigkeit mit Rauschen 9.08 m/s     (Grenze 9 m/s)
```

Das ist der seit Längerem offene Rauschtest (siehe `docs/ENTWICKLUNG.md`); er ist in dieser Serie
unverändert geblieben.

## Offen

- Seitenfahrt beim Aufsetzen im Spiel bestätigen (0.9.45).
- Zeitraffer-Fenster 30 s im Spiel bestätigen (0.9.48).
- Nach dem Gleiten wird nicht zurückgepumpt; bisher unkritisch.
- Idee, noch nicht eingebaut: Eintrittsbrennen als Hitzeschutz statt des Hitzeschutz-Tricks.
