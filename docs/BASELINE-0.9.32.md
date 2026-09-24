# Baseline 0.9.32

Eingefroren am 24.09.2026 um 14:59 nach der Bestätigung im Spiel: **Die Oberfläche folgt der
Sprache, die in KSP eingestellt ist.** Deutsch und Englisch sind beide gemessen — Deutsch im
Vergleich zum Stand davor (zeichengleich), Englisch im Flug vom Nutzer bestätigt.

## Wo der Stand liegt

```
build/backups/baseline-0.9.32-20260924-145919/
    src/ tests/ packaging/ tools/ docs/     Quelltext, Prüfungen, diese Beschreibung
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE .gitignore .gitattributes
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/README.md                          README des ausgelieferten Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei (94 Einträge)
```

Das Binary ist identisch mit dem in KSP installierten:

```
SHA256 63BC138FE04D0D4E7152C6DB240EF49926F97397B4FE21357268E93B2C45BFEC
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

In git ist der Stand der Commit, auf dem der Tag `v0.9.32` liegt; der Tag enthält zusätzlich diese
Beschreibung. `git checkout v0.9.32 -- src/` holt den Quelltext zurück.

## Was in diesem Stand steckt

- Version 0.9.32, `AssemblyVersion 0.9.32.0`, Paketversion 0.9.32.
- **Alle sichtbaren Texte liegen in Sprachdateien**: 125 Tags in
  `GameData/PhysStageRecovery/Localization/en-us.cfg` und `de-de.cfg`. Gelesen werden sie über
  `Loc.Get("#PSR_…")` (`src/Localization.cs`), mit `<<1>>`, `<<2>>` für Werte.
- **Der englische Wortlaut steht zusätzlich in der DLL** (`src/LocalizationTable.cs`). Fehlt der
  `Localization`-Ordner, greift `Loc.Get` darauf zurück und zeigt englischen Text statt roher Tags.
- **Wie KSP die Sprache wählt** (in der installierten 1.12.5 nachgesehen): `buildID64.txt`, Zeile
  `language = <id>` → `Localizer.GetLanguageIdFromFile` → `Localizer.CurrentLanguage`. Ein Mod liefert
  Texte als beliebigen `Localization { <id> { #tag = Text } }`-Knoten unter GameData
  (`GameDatabase.GetConfigNodes`); `RefreshTagValues()` legt zuerst `en-us` aus allen Dateien an und
  dann die eingestellte Sprache darüber. **Kein ModuleManager nötig.**
- `Localizer.Format` hat **keinen Null-Schutz** (`Format` → `Instance._Format`). Vor `Init()` wirft er;
  jeder Zugriff läuft deshalb über `Loc.Get` mit Schutz und einmaliger Warnung.
- **Bewusst nicht übersetzt**: Flugschreiber-Spalten und reine Diagnosezeilen. Die Statuswörter des
  Bergungsjournals werden weiter englisch gespeichert (`Tracking`, `Recovered`, …) und nur für die
  Anzeige übersetzt. Auf einem deutschen KSP steht in Fenster, Menü und Log zeichengleich das, was
  vorher im Quelltext stand.
- Das Menü am Triebwerk wird zur Laufzeit umbenannt (`reservePercent`, `reserveInfo`, `reserveStatus`,
  `ReleaseReserve`); der Gruppenkopf `Lande-Vorhalt` steckt in `PartModule.guiName`, das KSP nicht
  selbst lokalisiert, und wird deshalb per Reflection gesetzt.
- Der Lande-Vorhalt aus 0.9.31 ist unverändert enthalten (Regler 0–80 % auf den eigenen Tanks,
  Abschaltung bei Erreichen, MechJeb trennt darauf, Schraffur in der Stock-FT-Box).
- Werte aus `settings.cfg` des Flugs: Physikreichweite 1000 km, Autostaging an ab 50000 m,
  Landeautomat an, Ziel-Sinken **5 m/s**, Schirmhöhe 1100 m, Sinkgrenze 12 m/s, Quergrenze 3 m/s,
  Aufsetzgeschwindigkeit 2 m/s, Endphase 150 m, Auffanghöhe 10 m, Neigungsgrenze 20 Grad,
  Schubreserve 20 %, `guidanceMode = predictive`.
- Testlage: 30 + 19 + 37 + **12 (neu, Sprache)** + 26 + 9 + 13 + 49 + 25 Prüfungen grün. `.\test.ps1`
  bricht danach am bekannten Rauschtest ab (siehe unten); `LandingPortTests` (59) und
  `WindowOpenPolicyTests` (8) wurden deshalb einzeln gestartet — beide grün.
- Die Baselines 0.9.17, 0.9.20 und 0.9.31 liegen unverändert daneben.

## Womit dieser Stand belegt ist

**Die drei Zustände der Sprachdateien, jeder mit einem eigenen KSP-Start gemessen** (ich habe die
Dateien dafür kurz weggelegt und danach wiederhergestellt):

| Zustand | Zeile aus `KSP.log` |
|---|---|
| `de-de.cfg` + `en-us.cfg` | `Sprache 'de-de': 125 Texte, davon 121 uebersetzt und 4 wie im englischen Ersatz, 0 fehlend.` |
| nur `en-us.cfg` | `Sprache 'de-de': 125 Texte, davon 0 uebersetzt und 125 wie im englischen Ersatz, 0 fehlend.` |
| kein `Localization`-Ordner | `Sprache 'de-de': 125 Texte, davon 0 uebersetzt und 0 wie im englischen Ersatz, 125 fehlend.` |

Die vier „wie im englischen Ersatz" sind Wörter, die in beiden Sprachen gleich lauten (`Mod`,
`Autostaging`, `SURFACE SPEED`, `5–2000 km`).

Der erste Versuch für den dritten Zustand war **falsch gemessen**: Ich hatte den Ordner *innerhalb*
von GameData umbenannt — KSP sammelt jede `.cfg` unter GameData ein, unabhängig vom Ordnernamen, und
die Texte waren weiter da. Erst außerhalb von GameData zeigt sich der Notnagel. Das ist zugleich der
Beweis für die Aussage „jede `.cfg` unter GameData zählt".

**Was der Notnagel liefert**, direkt an der ausgelieferten DLL abgerufen (ohne Spiel, dort ist
`Localizer.Instance` null — also genau der Zweig, der ohne Sprachdatei läuft):

```
Sprache          : '' (KSP fehlt)
Audit            : Sprache 'unbekannt': 125 Texte, davon 0 uebersetzt und 0 wie im englischen Ersatz, 125 fehlend.
Tagline:          LAND STAGES. CONTINUE THE MISSION.
Fusszeile:        1000 km range  ·  recovery on ground contact
Vorhalt haelt:    RESERVE REACHED: 1 engine(s) off, 7.0 % left (limit 20 %)
unbekannter Tag:  #PSR_GibtEsNicht
```

**Der englische Flug** (Nutzer hat KSP auf Englisch gestellt, `buildID64.txt` steht auf
`language = en-us`, Squad liefert dafür das englische Wörterbuch mit 12034 Tags):

```
[LOG 14:56:29.253] [PhysStageRecovery] Lande-Vorhalt: 48 von 48 Triebwerksteilen mit Regler.
[LOG 14:56:29.255] [PhysStageRecovery] Sprache 'en-us': 125 Texte, davon 0 uebersetzt und 125 wie im englischen Ersatz, 0 fehlend.
[LOG 14:56:52.730] [PhysStageRecovery] 0.9.32 started; physics range 1000000 m.
```

Fenster, Reiter, Zustandszeile und das Triebwerksmenü wurden in diesem Lauf am Bildschirm bestätigt.

**Dass die deutschen Texte zeichengleich geblieben sind**, wurde nicht nur gelesen, sondern mit KSPs
eigenem `ConfigNode.Load` außerhalb des Spiels nachgestellt — dieselben zusammengesetzten Zeilen wie
vorher, inklusive der doppelten Leerzeichen der Fußzeile:

```
Fusszeile:              1000 km Reichweite  ·  Bergung bei Bodenkontakt
Zustand:                Zustand: Triebwerkslandung
Vorhalt haelt:          VORHALT ERREICHT: 1 Triebwerk(e) aus, Rest 7.0 % (Grenze 20 %)
Vorhalt frei:           freigegeben (Stufentrennung), Rest 30.1 %
Nicht erfasst:          Nicht erfasst: Booster - gilt als gelandet oder gewassert.
```

**Die Prüfungen der Sprachdateien** (`tests/LocalizationTests.cs`, 12 Prüfungen, laufen bei jedem
`.\test.ps1`): gleicher englischer Wortlaut in Tabelle und `en-us.cfg`, keine Waise in einer der
Dateien, gleiche `<<n>>`-Platzhalter in beiden Sprachen, keine führenden oder abschließenden
Leerzeichen (die schneidet der Config-Parser ab), kein `=` im Text — und **jeder in `src/` benutzte
Tag hat einen Text** (125 Tags in 56 Dateien).

## Offen in diesem Stand

- Die **Schraffur wurde im englischen Lauf nicht bestätigt**: Es gibt keinen `Schraffur in …`-Eintrag,
  weil vor dem Schließen kein Kästchen am Stufen-Icon auftauchte. Sprachabhängig ist die Suche
  trotzdem nicht, und das zeigen die beiden Läufe im Log:
  `gesucht: FT|Flüssigtreibstoff|LiquidFuel|Ox|Oxidator|Oxidizer` (deutsch) gegen
  `gesucht: LF|Liquid Fuel|LiquidFuel|Ox|Oxidizer` (englisch). Die Marker entstehen aus Abkürzung,
  Anzeigename **und** Ressourcennamen; `ReserveStageBox.Matches` vergleicht sie ohne
  Groß-/Kleinschreibung als Teilzeichenkette. Das nächste Mal im englischen Flug bitte mitprüfen.
- Der Mod loggt beim Aufstieg einmalig `kein Stufen-Icon fuer das Triebwerk gefunden` und
  `kein Ressourcen-Kaestchen zum Vorhalt gefunden`, solange Icon bzw. FT-Box noch nicht existieren
  (im englischen Lauf um 14:56:52 und 14:56:54). Kosmetisch, gehört aber geglättet.
- `Vorhalt freigegeben …` wird auch für Triebwerke ohne Vorhalt geloggt (Feststoffbooster mit
  `Rest=0.0 %`). Kosmetisch.
- Der Wert in m/s im Triebwerksmenü ist die ideale Delta-v ohne Schwerkraft- und Luftverluste.
- Ein Schiff, das **ohne** Abtrennung selbst landen soll, braucht den Knopf `Vorhalt freigeben`.
- Das Triebwerk schaltet 1,5 m über dem Boden ab; daher rund 5,5 bis 6,1 m/s Aufsetzgeschwindigkeit
  im Log, obwohl die Endphase auf 2 m/s ausläuft.
- `.\test.ps1` bricht am bekannten Rauschtest ab (`Aufsetzgeschwindigkeit mit Rauschen 9,08 m/s`).
  Der Fehlschlag besteht unverändert seit vor 0.9.15 — aus dem Schnappschuss von 0.9.31 nachkompiliert
  und dort **genau derselbe** Fehlschlag. Weil das Skript dort abbricht, laufen die beiden Suiten
  danach nicht mehr automatisch; sie wurden einzeln gestartet.
- Log und CSV bleiben absichtlich unabhängig von der Sprache (siehe oben). Wer die
  Zustandsbeschreibungen auch dort übersetzen will, müsste den Flugschreiber sprachabhängig machen —
  das würde jeden CSV-Vergleich zwischen zwei Sprachen brechen.
- In `settings.cfg` stehen noch die folgenlosen Schlüssel `reserveHudX`/`reserveHudY` aus der in 0.9.26
  entfernten HUD-Anzeige; kein Code liest sie mehr.
- Auf dieser Installation ist KSP jetzt auf **Englisch** gestellt. Für einen deutschen Beleglauf muss
  die Sprache in KSP wieder auf Deutsch umgestellt werden; die Squad-Datei ist dann wieder die
  deutsche (nur die aktuelle Sprache wird mitgeliefert).
