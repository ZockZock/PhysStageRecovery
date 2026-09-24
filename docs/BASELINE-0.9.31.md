# Baseline 0.9.31

Eingefroren am 24.09.2026 um 14:25 nach den Flügen, mit denen der Stand bestätigt wurde: Der
**Lande-Vorhalt** hält beim Aufstieg Treibstoff zurück, MechJeb trennt daraufhin von selbst, und die
Reserve ist im Spiel **in der FT-Box** der Stufenliste schraffiert zu sehen.

## Wo der Stand liegt

```
build/backups/baseline-0.9.31-20260924-142530/
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
SHA256 BA565B814F2D1B4C4F3C6DC57001A6D7A23BB0C022B323F8EDED5BA401CE8477
```

Wiederherstellen: Inhalt von `src`, `tests`, `packaging` über die gleichnamigen Ordner im Projekt
kopieren, dann `.\test.ps1`, `.\build.ps1` und `.\install.ps1` ausführen. Prüfen, ob ein Ordner noch
unverändert ist (im Schnappschussordner ausführen):

```powershell
Get-Content SHA256SUMS.txt | ForEach-Object {
    $hash, $file = $_ -split '  ', 2
    if ($hash -ne (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) { "geaendert: $file" }
}
```

In git ist der Stand der Commit, auf dem der Tag `v0.9.31` liegt; der Tag enthält zusätzlich diese
Beschreibung. `git checkout v0.9.31 -- src/` holt den Quelltext zurück.

## Was in diesem Stand steckt

- Version 0.9.31, `AssemblyVersion 0.9.31.0`, Paketversion 0.9.31.
- **Lande-Vorhalt am Triebwerk** (0.9.21 bis 0.9.31): Im Triebwerksmenü (Werkstatt und Flug) steht der
  Regler `Lande-Vorhalt (%)` von 0 bis 80. Er bezieht sich auf die **eigenen Tanks** des Triebwerks —
  den Zweig des Bauteilbaums bis zum ersten Trenner, Dockingport oder Bauteil ohne Treibstoffdurchlass.
  Abwurftanks hinter einem Trenner und Tanks an einer Treibstoffleitung zählen nicht mit.
- Ist der Vorhalt erreicht, schaltet der Mod dieses Triebwerk (und die Triebwerke an denselben Tanks)
  ab und meldet es als ausgebrannt. **MechJebs Autostage trennt daraufhin von selbst**, weil es für
  „hat die Stufe noch Treibstoff?" genau `!flameout && !engineShutdown` abfragt. Der Mod stagt die
  aktive Rakete nie selbst.
- **Freigegeben** wird der Vorhalt genau einmal: bei der Stufentrennung, beim Aufsetzen oder von Hand.
- Das Modul ist ein **Bauteilmodul** — ein Harmony-Prefix auf `PartLoader.ParsePart` trägt es in jede
  Triebwerkskonfiguration ein, bevor KSP sie parst. Deshalb überlebt der Wert Werkstatt, Startrampe
  und Spielstand. Dafür ist **kein ModuleManager** nötig.
- **Anzeige** (0.9.30, Ecken in 0.9.31): Die Schraffur sitzt **in der Stock-FT-Box** am Stufen-Icon des
  Triebwerks. Anfang und Breite kommen aus dem uGUI-`Slider` der Box (`value`, `minValue`, `maxValue`,
  `fillRect`, `fillRect.parent`, `direction`), das richtige Kästchen wird über seinen eigenen Text
  gefunden. Die linken Ecken der Schraffur sind wie beim Originalbalken gerundet (eigener Sprite mit
  linkem Rand und `Image.Type.Tiled`).
- Werte aus `settings.cfg` des Flugs: Auffanghöhe **10 m**, Endphase 150 m, Aufsetzgeschwindigkeit
  2 m/s, Neigungsgrenze 20 Grad, Schubreserve 20 %, Physikreichweite 1000 km, Autostaging ab 30 km,
  Landeautomat an, `guidanceMode = predictive`.
- Testlage: `.\test.ps1` läuft bis auf einen bekannten Fall durch — der Stresstest mit ±3 m
  künstlichem Rauschen auf den Bodenabstand setzt mit 9,1 m/s auf statt unter 9 m/s. Das besteht seit
  vor 0.9.15 und ist eine Frage der Filterung des letzten Höhenwerts. Neu in diesem Stand: 37
  Prüfungen der Vorhalt-Rechnung (`tests/FuelReserveTests.cs`).
- Die Baselines 0.9.17 und 0.9.20 liegen unverändert daneben (`baseline-0.9.17-20260924-092207`,
  `baseline-0.9.20-20260924-103455`).

## Womit dieser Stand belegt ist

**Der Vorhalt im Log** (`KSP.log`, letzte Sitzung): Bei jeder Stufentrennung steht, mit wie viel
Treibstoff der Booster weggeht — die Reserve ist also angekommen:

```
Vorhalt freigegeben part=2992912406 RE-M3 "Hauptsegel" Grund=Stufentrennung Rest=7.0 %
Vorhalt freigegeben part=2992912406 RE-M3 "Hauptsegel" Grund=Stufentrennung Rest=11.8 %
Vorhalt freigegeben part=2992912406 RE-M3 "Hauptsegel" Grund=Stufentrennung Rest=28.3 %
Vorhalt freigegeben part=1691288251 RE-M3 "Hauptsegel" Grund=Stufentrennung Rest=30.1 %
```

**Die Anzeige im Log:**

```
Lande-Vorhalt: Schraffur in 1 Stock-Kaestchen des Triebwerks (Kaestchen: 'FT').
```

Am Bildschirm bestätigt: die Schraffur liegt im linken Teil des FT-Balkens, mit denselben runden
Ecken wie der Originalbalken.

**Die Landungen** aus den Flugschreiber-CSVs der letzten Sitzung:

| Flug | Masse beim Abtrennen | Zündung | Schub mittel/max | Aufsetzen sink/quer | Treibstoff |
|---|---|---|---|---|---|
| 14:03 | 39,2 t | 2483 m | 58 % / 100 % | 5,7 / 1,2 m/s | 6,62 t |
| 14:16/14:18 | 41,3 t → 40,7 t | 5843 m | 59 % / 100 % | 6,1 / 1,7 m/s | 14,34 t |

Der zweite Fall ist ein schneller Eintritt (1883 m/s Seitwärtsfahrt beim Abtrennen, 68 km Höhe im
Coast-Datensatz `1416`) und landet mit 6,1 m/s senkrecht und 1,7 m/s quer. Beide Flüge bremsen mit
vollem Schub an, wie es der Stand seit 0.9.20 tut.

## Offen in diesem Stand

- Der Mod loggt beim Aufstieg einmalig `kein Stufen-Icon fuer das Triebwerk gefunden` und
  `kein Ressourcen-Kaestchen zum Vorhalt gefunden`, solange das Stufen-Icon bzw. die FT-Box noch nicht
  existiert. Kurz darauf hängt die Schraffur und die Meldung bleibt aus. Kosmetisch, aber es gehört
  beim nächsten Mal geglättet (erst nach einigen Sekunden warnen).
- `Vorhalt freigegeben ...` wird auch für Triebwerke ohne Vorhalt geloggt (Beispiel: die
  Feststoffbooster mit `Rest=0.0 %`). Ebenfalls kosmetisch.
- Der Wert in m/s im Triebwerksmenü ist die ideale Delta-v ohne Schwerkraft- und Luftverluste des
  echten Bremsflugs — eine Einordnung, kein Versprechen.
- Ein Schiff, das **ohne** Abtrennung selbst landen soll, braucht den Knopf `Vorhalt freigeben`; die
  automatische Freigabe hängt an der Stufentrennung.
- Das Triebwerk schaltet 1,5 m über dem Boden ab; der Booster fällt dieses Stück frei. Daher steht als
  Aufsetzgeschwindigkeit rund 5,5 bis 6,1 m/s im Log, obwohl die Endphase auf 2 m/s ausläuft.
