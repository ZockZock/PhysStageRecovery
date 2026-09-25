# Baseline 0.9.38

Eingefroren am 25.09.2026 nach zwei Flügen mit 0.9.38. Sie enthält eine Änderung seit 0.9.37: der Mod
baut einem getrackten Booster **eigenen Boden**, wenn KSP weit weg keinen baut. Im Flug ist er
zweimal als **echter Kollisionskontakt** belegt (`bodenkontakt=collider`).

## Wo der Stand liegt

```
build/backups/baseline-0.9.38-20260925-165631/
    src/ tests/ packaging/ tools/ docs/     Quelltext, Prüfungen, diese Beschreibung
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE .gitignore .gitattributes
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/README.md                          README des ausgelieferten Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei (109 Einträge)
```

Das Binary ist identisch mit dem in KSP installierten:

```
SHA256 E88282DA5BF56667E9AA923A6564E8DAE59338A2711D9BE08580203477A19A4F
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

In git ist der Stand der Commit, auf dem der Tag `v0.9.38` liegt; der Tag enthält zusätzlich diese
Beschreibung. `git checkout v0.9.38 -- src/` holt den Quelltext zurück.

**Unterschied zum geflogenen Binary.** Der Beweismoment (echter Kollisionskontakt) wurde mit dem
Build `95322EA2AF9E8FB2C07788699C577CB7AB21D2541AEB38508BF4F00208459D15` geflogen — das ist der
Stand mit Wasser-Sperre **und** Diagnosefeldern im Log. Danach kam eine letzte Absicherung dazu: Der
Patch prüft die Wasserlage jetzt **selbst** (`body.ocean && SurfaceAltitude < 0` → kein Bau) statt
sich auf die Entscheidung des Aufrufers zu verlassen. Das ist die einzige Abweichung; an Geometrie,
Ebene, Zeitpunkt und Aufhängung hat sich nichts geändert. Diese letzte Fassung ist noch nicht
geflogen.

## Was in diesem Stand steckt

- Version 0.9.38, `AssemblyVersion 0.9.38.0`, Paketversion 0.9.38.
- **Eigener Boden unter fernen Boostern (0.9.38).** KSP baut Gelände und dessen Bodenkollision nur um
  das aktive Schiff; ein Booster, der hunderte Kilometer entfernt landet, fällt dort durch die
  sichtbare Landschaft, und der Aufsetzkontakt musste aus der prozeduralen Höhe geschätzt werden
  (`TouchdownPolicy.HeightContact`, im Log `KONTAKT=hoehe`). Jetzt gilt:
  * `src/GroundField.cs` — Netzgeometrie ohne KSP: 33 × 33 Punkte über 700 m (rund 22 m Abstand),
    Dreiecksliste und Windungsrichtung (Ost × Nord = oben, sonst wäre die Fläche von oben unsichtbar).
  * `src/GroundPatchPolicy.cs` — wann gebaut wird: weiter als **2,5 km** vom aktiven Schiff (dort
    baut KSP nichts, und innerhalb würde sich der Patch mit echtem Boden doppeln), tiefer als
    **2,5 km** über Grund, **nicht über Wasser**; nachgeführt ab **150 m** Versatz oder nach **3 s**.
    Die Entscheidung hängt bewusst nicht davon ab, ob gerade ein fremder Kollider gefunden wurde —
    der Patch ist selbst einer, sonst würde er im Sekundentakt gebaut und verworfen.
  * `src/GroundPatch.cs` — KSP-Anbindung: Höhenfeld aus der **exakten** prozeduralen Oberfläche
    (`PQS.GetSurfaceHeight`), `MeshCollider` auf **derselben Ebene wie KSPs Gelände**
    (`Local Scenery`, Ebene 15), Wurzelknoten **unter `PQS`** (Planetenrotation und jede
    Floating-Origin-Verschiebung gehen damit automatisch mit), Kollider nach jedem Umbau neu
    zugewiesen (PhysX merkt eine Änderung an derselben Mesh-Instanz sonst nicht).
  * Eingehängt in `TrackedBooster.Measure`: gebaut, solange die Bedingung gilt; abgeräumt bei
    Abschluss der Verfolgung, bei gepacktem Schiff oder wenn er wieder in KSPs Bodenblase kommt.
    Im Landelog steht `BODEN=eigen`, wenn er aktiv ist.
- Unverändert aus 0.9.37: Kontrollmodul-Pflicht für die Triebwerkslandung, Anleitung im Fenster,
  Vorhalt nur um Kerbin, „Ziel-Sinken" als einziges Ziel, nur ein noch geschlossener Schirm zählt,
  alle sichtbaren Texte aus Sprachdateien (143 Tags).
- Werte aus `settings.cfg` des Flugs: Physikreichweite 1000 km, Autostaging an, Landeautomat an,
  Ziel-Sinken 5 m/s, Schirmhöhe 1100 m, Sinkgrenze 12 m/s, `guidanceMode = predictive`,
  `configVersion 7`.
- Testlage: die neue Suite `tests/GroundPatchPolicyTests.cs` hat **28 Prüfungen**, alle grün (Regel
  und Netzgeometrie). `.\test.ps1` bricht weiterhin am bekannten Rauschtest ab (siehe unten); die
  neue Suite läuft **vor** der Abbruchstelle.
- Die Baselines 0.9.17, 0.9.20, 0.9.31, 0.9.32 und 0.9.37 liegen unverändert daneben.

## Womit dieser Stand belegt ist

**Zwei Starts mit 0.9.38** (`KSP.log`):

```
[LOG 16:46:31.570] [PhysStageRecovery] 0.9.38 started; physics range 1000000 m.
[LOG 16:49:58.876] [PhysStageRecovery] 0.9.38 started; physics range 1000000 m.
```

**Der Boden entsteht dort, wo er gebaut werden soll** — Entfernung, Höhe über Grund und Wasserlage
stehen seit dieser Version im Log:

```
[PhysStageRecovery] Eigener Boden unter Booster: 32x32 Punkte, 700 m breit, Hoehe -1055.2 m,
    Ebene 15 (Local Scenery), Entfernung 174245 m, ueberGrund 2492 m, Wasser ja
```

**Er wird im Anflug benutzt und liegt über Land** (`terrain` positiv, durchgehend `BODEN=eigen`):

```
clearance=1859.31757879259 terrain=788.670796660939 BODEN=eigen
clearance=428.507469763297 terrain=895.474421732361 BODEN=eigen
clearance=144.85226258921  terrain=996.997420662548 BODEN=eigen
```

**Der Beweis: echter Kollisionskontakt** — nicht mehr die geschätzte Höhe, und einmal mit sanftem
Sinken:

```
[PhysStageRecovery] Contact outcome=Crashed vessel=5bd21bdb-… bodenkontakt=collider preImpactSink=5.85430817886662
[PhysStageRecovery] Contact outcome=Crashed vessel=462d44bd-… bodenkontakt=collider preImpactSink=148.776755018908
```

**Sprache und Texte kommen weiterhin an:**

```
[PhysStageRecovery] Sprache 'de-de': 143 Texte, davon 138 uebersetzt und 5 wie im englischen Ersatz, 0 fehlend.
```

**Was in diesen zwei Starts nicht vorkam:**

* Die beiden Kontakte wurden als **Absturz** eingeordnet, obwohl einer mit 5,85 m/s aufsetzte. Der
  Grund liegt nicht im Boden: beide Stufen kamen mit rund **175,6 m/s seitlich** herein (dreimal
  praktisch derselbe Wert — die Signatur einer Stufe, die fast senkrecht fällt, während sich der
  Planet unter ihr wegdreht) und verloren beim Aufprall Teile. Das ist der nächste getrennte Punkt.
* Ein **sanfter** Landefall mit Bergung auf dem eigenen Boden steht damit noch aus: der Beweis ist
  der Kontakttyp (`bodenkontakt=collider`), nicht die Einordnung „sicher".

## Offen in diesem Stand

- **Seitliche Bewegung beim Aufsetzen (~175 m/s).** Die Stufen kommen mit der Rotationsgeschwindigkeit
  des Planeten herein; der Mod sieht sie (`horizontal=150 … 129` in den letzten Sekunden), kann sie
  aber nicht wegnehmen. Ob das an fehlendem Schub, fehlender Zeit oder einem Fehler im Landegesetz
  liegt, ist **nicht** untersucht. Solange das so ist, zerschellen solche Stufen unabhängig vom Boden.
- **Der bekannte Rauschtest** (`Aufsetzgeschwindigkeit mit Rauschen 9,08 m/s`) schlägt weiter fehl —
  unverändert seit vor 0.9.15. Weil `.\test.ps1` dort abbricht, laufen die Suiten danach nicht mehr
  automatisch.
- **Die Wasser-Sperre ist noch nicht geflogen** (siehe oben). Beim nächsten Flug müsste im Log
  entweder `Wasser nein` stehen oder gar keine Bau-Zeile mehr über Wasser erscheinen.
- Weitere offene Punkte unverändert aus 0.9.37: einmalige Aufstiegs-Warnungen (`kein Stufen-Icon …`,
  `kein Ressourcen-Kaestchen …`), `Vorhalt freigegeben …` auch für Triebwerke ohne Vorhalt, m/s-Wert
  im Menü als ideale Delta-v, Triebwerksabschaltung 1,5 m über dem Grund, folgenlose alte Schlüssel
  in `settings.cfg` (`reserveHudX`/`reserveHudY`).
- Die Messsonde (`tools/terrainprobe/`, `tools/build-probe.ps1`) gehört **nicht** zum Mod: sie ist das
  Werkzeug, mit dem Netz, Ebene und Sichtbarkeit gemessen wurden, liegt in einem eigenen
  GameData-Verzeichnis `PhysStageRecoveryProbe` und ist mit `-Uninstall` restlos zu entfernen. Sie
  war während der Messflüge im Spiel und ist inzwischen wieder entfernt.
