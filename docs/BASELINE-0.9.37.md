# Baseline 0.9.37

Eingefroren am 24.09.2026 um 16:41 nach fünf Flügen mit genau dieser DLL. Sie enthält fünf Änderungen
seit 0.9.32: Kontrollmodul-Pflicht für die Triebwerkslandung, die Anleitung im Fenster, den Vorhalt
nur um Kerbin, „Ziel-Sinken" als einziges Ziel und die Schirm-Regel (nur ein noch geschlossener
Fallschirm zählt).

## Wo der Stand liegt

```
build/backups/baseline-0.9.37-20260924-164055/
    src/ tests/ packaging/ tools/ docs/     Quelltext, Prüfungen, diese Beschreibung
    build.ps1 test.ps1 install.ps1          Bau-, Test- und Installationsskript
    BoosterWatch.csproj README.md THIRD_PARTY.md LICENSE .gitignore .gitattributes
    dist/PhysStageRecovery.version          Versionseintrag des Pakets
    dist/README.md                          README des ausgelieferten Pakets
    dist/Plugins/PhysStageRecovery.dll      das installierte Binary
    SHA256SUMS.txt                          Prüfsumme jeder Datei (98 Einträge)
```

Das Binary ist identisch mit dem in KSP installierten:

```
SHA256 586369442E0D5AFB5E4F04E68FB042A14BFB17B87C3537A89D2586212F1F7E60
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

In git ist der Stand der Commit, auf dem der Tag `v0.9.37` liegt; der Tag enthält zusätzlich diese
Beschreibung. `git checkout v0.9.37 -- src/` holt den Quelltext zurück.

## Was in diesem Stand steckt

- Version 0.9.37, `AssemblyVersion 0.9.37.0`, Paketversion 0.9.37.
- **Triebwerkslandung verlangt ein Kontrollmodul (0.9.33).** Ohne Steuermodul ist
  `Vessel.isControllable` falsch, und `ModuleEngines.UpdateThrottle` setzt dann gar keinen Schub um
  (`Part.get_isControllable` → `Vessel.get_IsControllable`, gesetzt in `Vessel.CheckControllable` aus
  `GetControlLevel`). Eine Stufe mit Triebwerk, aber ohne Steuermodul wird mit dem Grund „kein
  Kontrollmodul am Booster" übersprungen; hat sie Fallschirme, wird sie verfolgt, der Landeautomat
  bleibt aus und die Zustandszeile sagt warum. Regel: `src/TrackingAcceptance.cs`.
- **Anleitung im Fenster (0.9.34).** Solange kein Booster erfasst ist, stehen dort acht Absätze
  (was der Mod macht, welche Stufen übernommen werden, Fallschirme, Triebwerkslandung, Landebeine und
  Bremsen, Autostaging, Bergung, während des Flugs) in einem Scrollbereich. Zwei Werte kommen aus den
  Einstellungen (`<<1>>`): Höchstzahl Booster und Schirmhöhe. Dabei behoben: die Zeile
  „Nicht erfasst: …" wurde seit 0.8.0 gesetzt, aber nie angezeigt — sie steht jetzt über der Anleitung.
- **Der Vorhalt greift nur um Kerbin (0.9.35).** Der Lande-Vorhalt hatte als einzige Funktion keine
  Prüfung des Himmelskörpers und hielt auch um den Mun Treibstoff zurück. Die Regel steht jetzt im
  Rechenkern (`FuelReserve.Acts(homeWorld, …)`); außerhalb der Heimatwelt ist er vollständig passiv,
  das Menü sagt „nur um Kerbin aktiv".
- **„Ziel-Sinken" ist das Ziel (0.9.36).** Vorher schrieb das Fenster `landingSpeed`, das Landegesetz
  las `touchdownSpeed`; beide wurden nur einmal gleichgesetzt. Jetzt ist `Settings.TouchdownSpeed` eine
  Eigenschaft auf `LandingSpeed`, der alte Schlüssel wird entfernt (`configVersion 7`). Diese
  Installation fliegt seit dem **5 m/s** statt der vorher versteckten 2 m/s; im Flug vom 24.09. um
  16:26 ist die Kette `Eintritt → Bremszuendung → Endphase → Aufsetzen` damit geflogen.
- **Nur ein noch geschlossener Schirm zählt (0.9.37).** Ein Schirm, der schon offen ist
  (`SEMIDEPLOYED`/`DEPLOYED`), schließt die Stufe aus — auch mit Triebwerk und Kontrollmodul; ein
  gekappter oder abgeschalteter Schirm ist kein Schirm. Der Schirm **deiner Rakete** bleibt unangetastet.
- Unverändert aus 0.9.32: alle sichtbaren Texte aus Sprachdateien (jetzt **143 Tags**, englisch in
  `en-us.cfg`, deutsch in `de-de.cfg`, englischer Wortlaut zusätzlich in der DLL), Flugschreiber-Spalten
  und reine Diagnosezeilen bewusst nicht übersetzt.
- Werte aus `settings.cfg` des Flugs: Physikreichweite 1000 km, Autostaging an, Landeautomat an,
  **Ziel-Sinken 5 m/s**, Schirmhöhe 1100 m, Sinkgrenze 12 m/s, Aufsetzgeschwindigkeit (`touchdownSpeed`
  entfällt), Endphase 150 m, Auffanghöhe 10 m, Neigungsgrenze 20 Grad, Schubreserve 20 %,
  `guidanceMode = predictive`, `configVersion 7`.
- Testlage: 30 + 19 + 44 + 12 (Sprache) + 12 (Annahme) + 26 + 9 + 13 + **56 (Einstellungen)** + 25
  Prüfungen grün; die beiden Suiten hinter der Abbruchstelle (`LandingPort` 59, `WindowOpenPolicy` 8)
  einzeln ebenfalls grün. `.\test.ps1` bricht am bekannten Rauschtest ab (siehe unten).
- Die Baselines 0.9.17, 0.9.20, 0.9.31 und 0.9.32 liegen unverändert daneben.

## Womit dieser Stand belegt ist

**Fünf Starts mit genau dieser DLL** (`KSP.log`):

```
[LOG 16:16:54.231] [PhysStageRecovery] 0.9.37 started; physics range 1000000 m.
[LOG 16:18:04.193] [PhysStageRecovery] 0.9.37 started; physics range 1000000 m.
[LOG 16:23:33.184] [PhysStageRecovery] 0.9.37 started; physics range 1000000 m.
[LOG 16:30:01.043] [PhysStageRecovery] 0.9.37 started; physics range 1000000 m.
[LOG 16:31:23.417] [PhysStageRecovery] 0.9.37 started; physics range 1000000 m.
```

**Alle Texte kommen an** (KSP steht wieder auf Deutsch, 143 Tags):

```
[LOG 16:15:11.303] [PhysStageRecovery] Lande-Vorhalt: 48 von 48 Triebwerksteilen mit Regler.
[LOG 16:15:11.305] [PhysStageRecovery] Sprache 'de-de': 143 Texte, davon 138 uebersetzt und 5 wie im englischen Ersatz, 0 fehlend.
```

**Die Einstellungen sind im Spiel migriert** — aus der Datei nach dem Flug:

```
configVersion = 7
landingSpeed = 5
```

kein `touchdownSpeed` mehr; genau das, was zuvor an einer Kopie der Datei außerhalb des Spiels
nachgestellt wurde.

**Die Kontrollmodul-Prüfung greift nicht ins Leere** — die neuen Starts sind steuerbar, das Feld ist
seit 0.9.33 in der Logzeile:

```
[LOG 16:18:39.213] [PhysStageRecovery] Tracking 724a2517-… Unbenanntes Raumfahrzeug Sonde control=True
[LOG 16:25:49.286] [PhysStageRecovery] Tracking 1ba1073a-… Unbenanntes Raumfahrzeug Sonde control=True
```

**Eine Triebwerkslandung mit dem neuen Ziel** (16:26): die Zustandszeile läuft
`Eintritt → Bremszuendung → Endphase → Aufsetzen`, der Schub geht bis auf 22 % zurück, und die Landung
wird als sicher eingeordnet und geborgen:

```
guidance=Landung Eintritt: 414.4 m/s, Schub 0%
guidance=Landung Bremszuendung: 146.0 m/s, Schub 56%
guidance=Landung Endphase: 4.9 m/s, Schub 23%
guidance=Landung Aufsetzen: 1.5 m/s, Schub 0%
Contact outcome=Touchdown vessel=1ba1073a-… bodenkontakt=hoehe preImpactSink=7.16 horizontal=0.07
Recovered 724a2517-… groundContact=true clearance=-0.636 sink=6.93 horizontal=0.49 funds=0
```

Dazu die Bergungen im Spiel (`[VesselRecovery]: … recovered … Recovery Value: 96–98 %`) und die
Schraffur im Stock-Kästchen, inzwischen wieder auf Deutsch:

```
[LOG 16:25:13.684] [PhysStageRecovery] Lande-Vorhalt: Schraffur in 1 Stock-Kaestchen des Triebwerks (Kaestchen: 'FT').
[LOG 16:25:49.222] [PhysStageRecovery] Vorhalt freigegeben part=2476433477 … Grund=Stufentrennung Rest=13.0 %
```

Bemerkenswert daran: Der Marker heißt auf Deutsch `FT`, auf Englisch `LF` — die Suche nach dem
Kästchen ist damit in beiden Sprachen belegt (die Marker entstehen aus Abkürzung, Anzeigename und
Ressourcenname).

**Was in diesen fünf Starts nicht vorkam** (deshalb nur durch Tests belegt, nicht durch einen Flug):

* **Kein Fallschirm.** Beide verfolgten Stufen flogen mit `chutes=0/0`, also reine
  Triebwerksstufen. Die Schirm-Automatik und die neue Regel „nur ein noch geschlossener Schirm zählt"
  sind damit ausschließlich durch `tests/TrackingAcceptanceTests.cs` (12 Prüfungen) belegt. Beim
  nächsten Flug mit Schirmen wäre `Deferred staged chute until safe descent`, `Arming chute …` bzw.
  `Chute opened …` die Zeile, die es zeigt.
* **Kein Flug außerhalb Kerbins.** Der Mun-Fall aus 0.9.35 (`Vorhalt` passiv außerhalb der Heimatwelt)
  ist über den Rechenkern-Test belegt, nicht über einen Flug; die Setup-Zeile trägt dafür das Feld
  `Koerper=<Name>`, das in dieser Log-Datei nicht auftaucht.
* **Die Ablehnung selbst.** Dass eine Stufe *ohne* Kontrollmodul oder mit *offenem* Schirm
  übersprungen wird, ist durch die zwölf Annahme-Prüfungen belegt; im Flug gab es nur den
  Normalfall (`control=True`, Schirme geschlossen). Eine Übersicht, warum eine Stufe nicht erfasst
  wurde, steht jetzt als Zeile „Nicht erfasst: …" im Fenster — im Log erschien der Skip-Pfad:

```
[LOG 16:31:25.775] [PhysStageRecovery] Skip vessel=Reuse1-Trümmer type=Debris situation=SPLASHED packed=True landed=True crew=0 homeBody=True distance=724.74 reason=nicht entpackt (packed)
```

## Offen in diesem Stand

- **Der bekannte Rauschtest** (`Aufsetzgeschwindigkeit mit Rauschen 9,08 m/s`) schlägt weiter fehl —
  unverändert seit vor 0.9.15, auch aus dem Schnappschuss von 0.9.31 nachkompiliert. Weil `.\test.ps1`
  dort abbricht, laufen die beiden Suiten danach nicht mehr automatisch; sie wurden einzeln gestartet.
- Der Mod loggt beim Aufstieg einmalig `kein Stufen-Icon fuer das Triebwerk gefunden` und
  `kein Ressourcen-Kaestchen zum Vorhalt gefunden`, solange Icon bzw. FT-Box noch nicht existieren.
  Kosmetisch, gehört geglättet.
- `Vorhalt freigegeben …` wird auch für Triebwerke ohne Vorhalt geloggt (im Flug vom 16:31 mit
  `Rest=0.0 %` für drei LV-T30). Kosmetisch.
- Der Wert in m/s im Triebwerksmenü ist die ideale Delta-v ohne Schwerkraft- und Luftverluste.
- Ein Schiff, das **ohne** Abtrennung selbst landen soll, braucht den Knopf `Vorhalt freigeben`.
- Das Triebwerk schaltet 1,5 m über dem Boden ab; daher liegt die Aufsetzgeschwindigkeit über dem
  Zielwert (im Flug 7,16 m/s bei 5 m/s Ziel, als sicher eingeordnet).
- In `settings.cfg` können folgenlose alte Schlüssel stehen (`reserveHudX`/`reserveHudY` aus der in
  0.9.26 entfernten Anzeige); kein Code liest sie.
- Die Anleitung im Fenster ist sprachlich geprüft (Audit: 0 fehlende Texte), die Darstellung selbst
  kann ich nicht sehen — sie ist vom Nutzer beim Fliegen gesehen worden.
