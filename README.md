# PhysStageRecovery 0.9.26 — KSP 1.12.5

PhysStageRecovery hält abgetrennte, unbemannte Booster in einer einstellbaren Physikreichweite aktiv. Ein frei skalierbares Kamerafenster zeigt ihren Sinkflug. Der eingebaute Landeautomat steuert Schub und Lage, öffnet sichere Stock-Fallschirme und fährt Landebeine aus.

**MechJeb muss weder installiert noch als Bauteil am Booster vorhanden sein.** Die benötigten Algorithmen sind im eigenen Plugin enthalten. Erforderlich ist weiterhin Harmony 2 unter `GameData/000_Harmony/0Harmony.dll`.

## 0.9.22: Der Vorhalt bleibt gespeichert — und man sieht ihn

Zwei Befunde aus dem ersten Flugtest: Die Stufentrennung am Vorhalt hat funktioniert, aber der in der
Werkstatt eingestellte Wert war auf der Startrampe weg. Und der Vorhalt soll im Flug sichtbar sein.

**Warum der Wert verloren ging — gemessen, nicht geraten.** KSP nimmt die Module eines Bauteils aus
dessen **Bauteilkonfiguration**. Ein Modul, das einem Bauteil erst danach angehängt wird, überlebt den
nächsten Neuaufbau dieses Bauteils nicht:

* Ein gestartetes Schiff entsteht aus `ProtoPartSnapshot.CreatePart()` → `Instantiate(prefab)`.
* `ProtoPartModuleSnapshot.Load()` → `Part.LoadModule(node, out index)` füllt danach nur **Werte** in
  Module, die das neue Bauteil **schon hat**. Es legt kein fehlendes Modul an — in der installierten
  `Assembly-CSharp.dll` nachgeprüft: kein `AddComponent`, kein `PartModuleList.Add` in dieser Kette.
* Das nachträglich angehängte Modul war auf der Rampe also nicht mehr vorhanden, und das Nachhängen im
  Flug erzeugte ein frisches Modul mit **0 %**. Genau das war zu sehen.

Deshalb trägt der Mod das Modul jetzt **in die Bauteilkonfiguration** ein: Ein Harmony-Prefix auf
`PartLoader.ParsePart` hängt `MODULE { name = ModuleFuelReserve }` an jedes Teil mit Triebwerk, *bevor*
KSP es parst (`src/FuelReserveConfig.cs`). Damit ist der Vorhalt ein Bauteilmodul wie jedes Stock-Modul
— in der Werkstatt, in der Werkstattdatei, im Spielstand und auf der Startrampe. Weiterhin **kein
ModuleManager nötig**; Harmony ist ohnehin Voraussetzung.

Beim Start steht die Kontrolle in `KSP.log`:

```
[PhysStageRecovery] Lande-Vorhalt: 42 von 42 Triebwerksteilen mit Regler.
```

Steht dort weniger, fehlt an einem Teil der Regler — das Log meldet es dann als Fehler, und ein dort
gesetzter Vorhalt überlebt Werkstatt und Rampe nicht. Triebwerke ohne Abschaltung (Feststoffbooster)
bekommen den Regler weiterhin nicht zu sehen.

## 0.9.26: Das eigene Feld ist wieder weg

Mit der Schraffur auf der Stock-Tankanzeige war der frei verschiebbare Balken aus 0.9.23 nur noch eine
zweite Anzeige derselben Zahl — er ist entfernt: das Feld selbst, sein Ziehbereich, die Einstellungen
`reserveHudX`/`reserveHudY` und die eigene Markierungsfarbe im Theme. Der Vorhalt steht jetzt an genau
**einer** Stelle: auf der Stock-Tankanzeige.

Wird die Stock-Anzeige wider Erwarten nicht gefunden, sagt das Log es, und der Vorhalt bleibt im
Triebwerksmenü ablesbar (Regler `Lande-Vorhalt (%)`, Statuszeile, Werkzeugtip). Gehalten, gemeldet und
freigegeben wird die Reserve in beiden Fällen identisch — es fehlt dann nur die Schraffur im
Stufenfeld, nicht die Funktion.

## 0.9.25: Die Schraffur sitzt auf der Balkenfläche

Der erste Wurf lag auf der **Box** statt auf dem Balken: `StageGroup.DeltaVHeadingImage` ist der Rahmen,
der das Kürzel (`FT`) und den Balken trägt — die Schraffur lag damit über dem Kürzel und war so hoch
wie die Box. Der Balken ist die **breiteste Grafik innerhalb dieser Box** (ein Kürzel ist ein
Textobjekt ohne Grafik und kann nicht gewinnen); an ihr hängt das Overlay jetzt, mit ihrer Breite als
Bezug für den Anteil und ihrer Höhe:

```
[PhysStageRecovery] Lande-Vorhalt auf der Stock-Tankanzeige: Stufe 2 Box=92x14 Flaeche='Image' 74x10 Typ=Filled fill=0.75
[PhysStageRecovery] Lande-Vorhalt: Schraffur an 'Image' eingehaengt (25 % von 74x10).
```

Die Logzeile nennt Box, Balkenfläche, Grafiktyp, Füllstand und die eingehängte Fläche — ein
danebengehendes Overlay ist damit in einem Start erklärt statt geraten.

## 0.9.24: Der Vorhalt liegt auf der Stock-Tankanzeige

Der Vorhalt ist jetzt **direkt auf der Tankanzeige des Spiels** zu sehen — dort, wo im Stufenfeld der
Balken mit `FT` steht. Der reservierte Anteil bekommt dort die Schraffur, der Rest bleibt der
Stock-Balken; das eigene Feld aus 0.9.23 entfiel in 0.9.26 wieder.

**Position und Größe sind abfragbar — ohne Ratespiel.** Die Stock-Anzeige ist öffentliche API:

```csharp
StageManager.Instance.Stages            // ein StageGroup je Stufe
group.inverseStageIndex                 // die Stufennummer, verglichen mit Vessel.currentStage
group.DeltaVHeadingImage                // der Balken selbst (UnityEngine.UI.Image)
```

`DeltaVHeadingImage` ist das einzige `Image`-Feld einer `StageGroup`, und `FT` ist die deutsche
Stock-Abkürzung für `LiquidFuel` (`#autoLOC_6002095 = FT` in `dictionary.cfg`) — der Balken gehört also
dem Spiel, nicht einem Mod. Das Feld selbst ist `private` und wird deshalb einmalig per Namen gelesen;
fehlt es, sagt das Log es und das eigene Feld bleibt die Anzeige.

Statt das Rechteck in Bildschirmkoordinaten umzurechnen, hängt das Overlay als Kind **an diesem
Balken**:

```csharp
rect.SetParent(bar.rectTransform, false);   // linke Kante auf der linken Kante des Balkens
rect.anchorMin = new Vector2(0f, 0f);
rect.anchorMax = new Vector2(0f, 1f);
rect.sizeDelta = new Vector2(bar.rect.width * reserveShare, 0f);
```

Damit folgen Platz, Größe, Ankermaße, UI-Skalierung und Auflösung dem Stock-Balken von selbst, und die
Streifen liegen darüber, weil ein Kind nach seinem Elternteil gezeichnet wird. Der Werkzeugtip des
Feldes nennt weiter die Zahlen; nach der Stufentrennung (Vorhalt freigegeben) verschwindet die
Schraffur, weil der Treibstoff dann der Landung gehört.

Beim ersten Einblenden steht im Log, was gefunden wurde — oder warum nicht:

```
[PhysStageRecovery] Lande-Vorhalt auf der Stock-Tankanzeige: Stufe 2 rect=92x14 fill=0.63
[PhysStageRecovery] Lande-Vorhalt: Stock-Tankanzeige nicht nutzbar - kein Stufenfeld fuer Stufe 2 (Felder: 5,4,3,2,1)
```

## 0.9.23: Der Vorhalt als eigenes Feld im Flug — in 0.9.26 wieder entfernt

Der Vorhalt war in 0.9.22 nur im Mod-Fenster zu sehen — und das Fenster öffnet sich erst, wenn sich
eine Stufe trennt. Während des Aufstiegs war die Anzeige damit unsichtbar. **Das Mod-Fenster bleibt
unverändert**; der Vorhalt bekommt stattdessen ein **eigenes kleines Feld**, das sofort erscheint,
sobald an der fliegenden Rakete oder an einem verfolgten Booster ein Vorhalt eingestellt ist, und das
sonst vollständig verschwindet:

```
LANDE-VORHALT                                        25 %
▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
aktiv: Rest 41.2 % (Grenze 25 %)
```

* Der reservierte Anteil steht **schraffiert** am linken Ende des Balkens, eine weiße Markierung trennt
  ihn vom nutzbaren Treibstoff. Wandert der Füllstand in die Schraffur, lebt die Landung von ihrem
  Vorhalt. Genau die Darstellung der Stock-Treibstoffanzeige, nur eben für den Vorhalt.
* Der Balken wird gegen die **eigenen Tanks** des Landetriebwerks gelesen — die Skala, von der der
  Vorhalt ein Anteil ist —, nicht gegen das ganze Schiff. Am Booster (nach der Trennung) zeigt das Feld
  dessen Vorhalt, davor den der fliegenden Rakete.
* Das Feld ist **frei verschiebbar** (linke Maustaste ziehen) und merkt sich seinen Platz in
  `settings.cfg`. Es lässt sich damit genau neben die Stock-Treibstoffanzeige oder die
  Stufen-Delta-v-Anzeige legen. Voreinstellung: linker Rand auf Höhe der Stufenliste.
* Beim Ziehen bleibt die Stock-Kamera stehen (derselbe Mausschutz wie beim Mod-Fenster), und der
  Werkzeugtip am Balken nennt den Vorhalt im Klartext (`Vorhalt 25 % der eigenen Tanks, Rest 41.2 %`,
  dazu `25 % = 4.89 t aus 2 Tanks, ca. 780 m/s`).

## 0.9.21: Lande-Vorhalt am Triebwerk

Der Mod rechnete bisher erst ab dem Abkoppeln. Was davor passiert, entscheidet aber, ob es überhaupt
etwas zu landen gibt: Wer mit MechJeb fliegt, dessen Autostage trennt die Stufe erst, wenn MechJeb sie
für ausgebrannt hält — ein Booster, der beim Aufstieg bis auf den letzten Tropfen leergesaugt wird,
kann danach nicht mehr bremsen. Ab jetzt lässt sich am Triebwerk einstellen, wie viel Treibstoff für
die Landung zurückgehalten wird.

**Rechte Maustaste auf das Triebwerk** (in der Fahrzeugfabrik und im Flug) → **`Lande-Vorhalt (%)`**.
Der Regler geht von 0 bis 80 % und bezieht sich **nur auf die Tanks, die direkt am Triebwerk hängen**:

* Gerechnet wird über den Zweig des Bauteilbaums, in dem das Triebwerk sitzt, bis zum ersten
  **Trenner, Dockingport oder Bauteil, das keinen Treibstoff durchlässt** (`fuelCrossFeed`). Ein
  Abwurftank hinter einem radialen Trenner und ein Tank, der nur über eine **Treibstoffleitung**
  angebunden ist, zählen nicht mit: eine Leitung ist keine Baumverbindung, ein Trenner trennt sie.
* Das Menü nennt daneben, was der Vorhalt gerade bedeutet: `25 % = 0.62 t aus 2 Tanks, ca. 210 m/s`.
  Die **Tankzahl ist die Kontrolle** — stehen dort weniger Tanks als erwartet, sind die Zusatztanks
  wirklich draußen. Der Wert in m/s ist die ideale Delta-v der Reserve aus Trockenmasse und Isp des
  Zweigs; Schwerkraft- und Luftverluste des echten Bremsflugs sind darin nicht enthalten.
* Erreicht der Tankinhalt den Vorhalt, **schaltet der Mod das Triebwerk ab und meldet es als
  ausgebrannt**. KSP lässt sich dabei nicht belügen: ein gefälschtes `flameout` wird im selben Tick von
  `ModuleEngines.RequestPropellant` → `UnFlameout` wieder gelöscht. Ein **abgeschaltetes** Triebwerk
  (`EngineIgnited = false`) verbraucht dagegen nichts, und KSP lässt seine Kennzeichen in Ruhe.
* **MechJeb trennt daraufhin von selbst.** In `MechJebModuleStagingController` (2.15.3, in der
  installierten `MechJeb2.dll` nachgeprüft) lautet die Frage „hat die laufende Stufe noch Treibstoff?"
  wörtlich `!flameout && !engineShutdown` — die Tanks werden dafür **gar nicht** abgefragt. Hat kein
  Triebwerk der laufenden Stufe mehr „Treibstoff", feuert MechJeb die nächste Stufe. Der Mod greift
  **nie** in die Stufung der aktiven Rakete ein; ohne MechJeb trennt die Leertaste wie gewohnt.
* **Triebwerke an denselben Tanks werden mit abgeschaltet.** Sonst saugt der Nachbar den Vorhalt leer,
  und MechJeb sieht weiter Treibstoff in der Stufe. Deshalb genügt es, den Regler an **einem**
  Triebwerk eines Clusters zu setzen.
* **Freigegeben** wird der Vorhalt genau einmal, danach steht der Treibstoff der Landung zur Verfügung:
  bei der **Stufentrennung** (das Bauteil landet in einem anderen Schiff), beim **Aufsetzen**, oder von
  Hand über `Vorhalt freigeben`. Die Statuszeile sagt, was gilt: `aktiv: Rest 41.2 % (Grenze 25 %)`,
  `VORHALT ERREICHT: 1 Triebwerk(e) aus, Rest 24.8 %` oder `freigegeben (Stufentrennung), Rest 24.8 %`.
* Der Vorhalt wird **mit dem Schiff und mit dem Spielstand gespeichert**. Ein Spielstand aus dem
  Sinkflug heraus lässt ihn also nicht wieder zuschnappen; eine Rakete, die noch auf dem Boden steht,
  startet dagegen mit scharfem Vorhalt in einen neuen Flug.
* Der Regler wird von diesem Plugin an die Triebwerksteile gehängt (`Part.AddModule` — dieselbe API,
  die KSP für den Düsenrucksack des Kerbal benutzt): in der Werkstatt, sobald ein Triebwerksteil
  erscheint, und im Flug für alle geladenen Schiffe. Dafür ist **kein ModuleManager nötig**. Nur
  Triebwerke, die sich abschalten lassen (`allowShutdown`), bekommen den Regler — ein Feststoffbooster
  ohne Abschaltung kann keinen Vorhalt halten.

**Geprüft ohne Spiel** (`tests/FuelReserveTests.cs`, 37 Prüfungen): die Grenze ist inklusiv (genau auf
dem Vorhalt wird gehalten), der leerste Treibstoff entscheidet (LF/Ox unsymmetrisch), ein Triebwerk
ohne eigenen Tank hat keinen Vorhalt, die 80-%-Grenze greift, Masse und ideale Delta-v stimmen
(10 t trocken + 2 t bei Isp 300 → 536 m/s; dieselben 2 t auf 20 t → 280 m/s) und die Textzeilen des
Menüs passen.

**Noch offen:** Der Vorhalt gilt für den Aufstieg. Ein Schiff, das ohne Abtrennung selbst landen soll,
braucht den Knopf `Vorhalt freigeben` — die automatische Freigabe hängt an der Stufentrennung.

## 0.9.7: Flugschreiber — ein Flug, alle Zahlen

Die Logzeilen entstehen einmal pro Sekunde und sind zum Lesen gedacht. Für einen Vorzeichenfehler,
der nur wenige Ticks sichtbar ist, reicht das nicht — jede der letzten Landungen hat einen Start
gekostet, um eine Frage zu beantworten, die im Log nicht stand. Jetzt schreibt der Mod **jeden Tick**
mit, alle Rohwerte aus KSP neben allen abgeleiteten Werten:

```
GameData/PhysStageRecovery/PluginData/Flights/flight-<id>-<datum>.csv
```

51 Spalten: Höhe/Terrain/Abstand/Neigung, Geschwindigkeit (up/east/north, Sink, Quer, Betrag, Mach),
Dichte (Spiel **und** Korridortabelle **und** Eichfaktor), Druck, Temperatur, Cd·A, Cd, Widerstand,
Schub, Masse, Treibstoff-dV, Zielsinkrate, freies a, benötigtes dV, Vorhersage (Aufsetzen, Reserve,
unhaltbar, Zündung fällig, Brenndauer), Befehle (vertikal/quer, Drossel, Grund einer Abschaltung),
Zielachse und ihr Winkel zu Retrograde, Lagefehler, gemessene Beschleunigung, Triebwerkszustand,
Phase, Coast, Zündlatche, Abbruch.

Der Pfad steht nach der Landung in `KSP.log` (`Flugschreiber: …`). Aufzeichnung: jeder Tick unter
1 km Höhe und ab der Zündung, sonst alle 0,1 s; geschrieben wird laufend, ein Absturz verliert also
nichts.

**Und der Widerstand kommt jetzt aus dem Spiel statt aus einer Nachbildung.** MechJeb ist die
Referenz (`VesselState`):

```csharp
AreaDrag += p.DragCubes.AreaDrag * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier;
```

Ich hatte das aus `WeightedDrag[face] * AreaOccluded[face] / 6` nachgebaut, dabei
`DragMultiplier` weggelassen und eine Summe, die schon ein Rumpfprodukt ist, durch sechs geteilt. Der
Fehler multipliziert sich mit v²: bei 100 m/s unsichtbar, bei 2000 m/s wurden aus 2 m/s² Widerstand
**884** — und das Gesetz hat es geglaubt, zwei Flüge hintereinander. Der Eichfaktor `rho=` stand bei
**1.0**, die Dichte war also die ganze Zeit in Ordnung.

## Bodensuche in Flugrichtung (0.9.1)

Die Landehöhe wurde bisher nur **senkrecht unter** dem Booster gemessen. Das beantwortet die falsche Frage, solange er sich noch bewegt: Ein Booster, der auf einen ansteigenden Hang zusinkt, hat weniger Boden unter sich, als sein Radarhöhenmesser sagt, und der Unterschied wächst mit der Seitwärtsgeschwindigkeit — bei 50 m/s Drift und 10 % Steigung sind das rund zehn Meter Boden, die der Autopilot noch nicht gesehen hat.

`src/GroundScan.cs` tastet deshalb die Geländehöhe entlang der tatsächlichen Flugrichtung ab, über zwei Sekunden Vorhaltezeit und höchstens 400 m. Gemeldet wird der **niedrigste** Boden auf diesem Weg, nicht der Mittelwert: der Punkt, den der Booster zuerst treffen würde. Der kleinere der beiden Werte — senkrecht oder voraus — ist der, an den sich der Abstieg halten muss.

Zusätzlich wird die **Neigung unter dem voraussichtlichen Aufsetzpunkt** gemessen. Auf einem Hang setzt ein Booster zuerst mit einer Kante seines Rumpfes auf, nicht mit der Mitte. Die Ausschalthöhe der Triebwerke wird deshalb um `Rumpfradius · (tan Neigung + tan Restlage)` angehoben; bei 15° Hang und 3 m Rumpfradius sind das 2,30 m statt 1,50 m. Ohne diesen Zuschlag gräbt sich die talseitige Kante in den Boden.

Die Werte stehen im Log: `slope=` nennt die Neigung, `voraus=` den senkrechten Abstand, wenn das Gelände voraus niedriger liegt als das darunter, und `cut=` die angehobene Ausschalthöhe.

## Diagnosezeile `Landing predict` (0.9.1, erweitert 0.9.4)

Zusätzlich zur bestehenden `Landing check`-Zeile schreibt der Landeautomat einmal pro Sekunde (oberhalb 100 m alle fünf Sekunden) **eine** Zeile mit **allen** Werten, die die Entscheidung getragen haben (hier umbrochen dargestellt, im Log steht sie in einer Reihe):

```
Landing predict <id> state=Bremszuendung clearance=412.3m slope=6.2deg voraus=505.1m
  sink=87.4m/s lateral=12.1m/s speed=88.2m/s drag=8.1m/s2 zielSink=64.2m/s aFrei=10.4m/s2
  cmd=78.0% aus=19.8m/s2 cut=2.3m tilt=18.2deg err=4.1deg torque=[2.10, 2.10, 2.10]
  mass=28.4t tdPred=6.2m/s reserve=-12.4m/s zuenden=ja brennt=11.3s dV=214.0m/s
  dVda=214.0m/s dVvorrat=612.0m/s
```

In der Eintrittsphase steht statt `(Coast: Luft bremst)` zusätzlich das Coast-Budget:

```
Landing predict <id> state=Eintritt (Coast: Luft bremst) clearance=28450.0m slope=0.0deg
  sink=61.2m/s lateral=2183.4m/s speed=2184.3m/s drag=2.4m/s2 coastBudget=2918m/s
  zielSink=205.1m/s aFrei=10.2m/s2 cmd=0.0% aus=19.9m/s2 cut=1.5m tilt=40.0deg ...
```

Diese eine Zeile ersetzt das Suchen in mehreren Zeilen: `zielSink` gegen `sink` zeigt, ob das Profil hält; `aFrei` gegen das, was `cmd` bewirkt, zeigt, ob die Triebwerke liefern; `reserve` wird negativ, bevor etwas schiefgeht; `dVda` gegen `dVvorrat` zeigt, ob der Treibstoff reicht. **Für einen Eintritt sind `speed` gegen `coastBudget` und `drag` die entscheidenden Zahlen**: Solange die geflogene Geschwindigkeit im Budget liegt, bleiben die Triebwerke aus, und `drag` sagt, ob die Luft diese Arbeit tatsächlich macht. `NICHT-ABFANGBAR` und `ABBRUCH` erscheinen nur, wenn der Automat den Absturz erkennt.

## Neu in 0.9.0: vorausberechnete Triebwerkslandung

Die Triebwerkslandung fliegt jetzt ein eigenes Regelgesetz (`src/DescentGuidance.cs`, `src/DescentPrediction.cs`) statt der übernommenen MechJeb-Zustandskette. Das Gesetz ist bewusst **frei von KSP und Unity** und wird im Testlauf gegen ein geschlossenes Modell geflogen, bevor es im Spiel fliegt.

**Der Kern ist eine einzige Gleichung.** Jeden Physikschritt wird ein Beschleunigungsvektor im lokalen Horizontsystem kommandiert; Drossel und Zielachse folgen daraus mechanisch:

```
Drossel  = |a| / (T_max/m)          Triebwerksachse zeigt entlang a
a_vert   = g + min( freies_a , 0.5·(v² − v*²)/Resthöhe )
```

* `0.5·(v² − v*²)/Resthöhe` ist die Verzögerung, die die verbleibende Sinkrate genau auf die Zielrate bringt — die Definition von „noch abfangbar". Weit oben ist sie größer als der Schub, also Vollschub; unten läuft sie von selbst gegen null aus.
* `g` wird als Vorhalt direkt mitkommandiert. Deshalb fliegt **dieselbe** Gleichung einen Booster mit TWR 1,5 und einen mit TWR 3,4.
* `v*` ist ein Sinkratenprofil, das bei der Zielsinkrate am Boden verankert ist und mit der Resthöhe wächst. Es gibt kein Umschalten zwischen „Coast", „Bremsung" und „Endanflug": der Sollwert ist eine stetige Funktion der Höhe.
* Unterhalb der Mindest-Steuergeschwindigkeit (2 m/s) zeigt der Regler nach oben statt retrograd. Das ist die Zeile, die den früheren „88 Grad bei 103 m" beseitigt hat.
* Die Neigung ist selbstbegrenzend: der Querbefehl wird auf `freies_a·tan(Neigung)` begrenzt, damit der Booster sich nicht so weit neigt, dass ihm die Höhenregelung entgleitet.

**Die Zündung entscheidet die Vorausberechnung** (`src/DescentPrediction.cs`): eine Zeitschritt-Integration des restlichen Abstiegs mit Zündverzug, Schubaufbau, leerlaufenden Tanks und KSPs echter Luftdichte im Korridor. Sie fragt: „Wenn ich jetzt zünde, wie schnell komme ich an?" Ist die Antwort zu schnell, wird gezündet. Ersetzt die früheren drei unabhängigen Schätzungen (`DecelerationEndAltitude`, `BrakingEnvelope`, `DragFreeAllowedSpeed`).

**Gemessen im Testlauf** (Punktmassenmodell, `tests/GuidanceTests.cs`):

| Anflug | Ergebnis |
|---|---|
| senkrecht 1500 m / 60 m/s | Landung mit 1,8 m/s |
| Hoverslam 2500 m / 120 m/s, TWR 2,5 | Landung mit 1,8 m/s |
| starker Schub TWR 3,4 | Landung mit 1,8 m/s |
| Seitendrift 30 m/s | Landung, Restdrift 0,9 m/s |
| Höhenrauschen ±3 m | Landung mit 2,6 m/s (ohne Rauschen 1,8) |
| TWR 0,8 (nicht landbar) | läuft bis zum Boden, wird als Absturz gemeldet |

## 0.9.5: was der erste Flugtest gefunden hat

Geflogen wurde 0.9.4. Der Booster drehte sich **prograd statt retrograd**, zündete nur kurz und
schlug ungebremst auf. Beides hatte dieselbe Ursache, und die Logzeilen zeigen sie:

```
state=Ausrichten clearance=66882m sink=447.9m/s lateral=1860.4m/s tilt=102.2deg err=0.8deg
```

`sink=447,9` und `lateral=1860,4` ergeben eine Bahnneigung von 13,5° unter dem Horizont. Retrograd
wäre eine Lage von **77°** ab Lot, prograd **103°**. Der Automat kommandierte 102,2° — und `err=0,8°`
sagt, dass der Booster diese Lage **exakt** eingehalten hat. Der Regler war also einwandfrei; das
Gesetz hat die falsche Richtung verlangt:

```csharp
// bis 0.9.4 – zeigte in Wahrheit PROGRAD
output.Up = -sink / speed;
output.East = state.VelocityEast / speed;
output.North = state.VelocityNorth / speed;
```

Die Geschwindigkeit im Horizontsystem ist `(-sink, +East, +North)`. Diese drei Zeilen gaben genau
`+v` zurück, also die Richtung *mit* der Geschwindigkeit. Richtig ist das Negative:
`(+sink, -East, -North)` — Nase rückwärts, Triebwerkssektion voraus in den Luftstrom.

**Warum daraus ein Absturz wurde.** Die Freigabe der Eintrittsphase fragte, ob der Booster
retrograd oder aufrecht steht. Mit einer prograden Lage war beides nie erfüllt: Der Automat blieb
den **ganzen Eintritt** in `Ausrichten` — kein Coast, keine Vorhersage (`zielSink=--`, `aFrei=--`
in jeder Zeile), keine Zündplanung. Erst bei 10 km ging es in `Bremszuendung`, mit `err` zwischen
100° und 160°: Die Bremszündung schob schräg bis rückwärts, und die Sicherheitsgrenze
(`Lagefehler > 80° → Schub auf 0`) machte daraus die kurzen Stöße, die man sieht. Aufgesetzt wurde
mit 82 m/s Sink und 130 m/s Querfahrt.

Behoben:

* `AimCoast` zeigt jetzt gegen die Geschwindigkeit; ein Test prüft das Skalarprodukt mit der
  geflogenen Geschwindigkeit (−1 = retrograd) und die Lage ab Lot (77° statt 103°).
* Die Freigabe der Eintrittsphase fragt nicht mehr nach „retrograd oder aufrecht“, sondern ob der
  Booster **die Lage hält, die das Gesetz gerade kommandiert** — die Frage, die sie stellen sollte.
* `dVvorrat` stand mit **164 632 m/s** im Log (zwei Minuten Treibstoff an Bord). Ursache: die
  Ressourcenmenge wurde in *Einheiten* durch den Verbrauch geteilt, und ein Propellant mit winzigem
  Anteil sprengt diese Division. Gerechnet wird jetzt über die **Masse** (`Dichte × Menge`), was
  jede Propellant-Kombination verträgt.
* Wird ein angeforderter Schub von der Sicherheitsgrenze abgeschaltet, steht das jetzt als
  `schubAus=Lagefehler 145 Grad > 80` in der Zeile, statt stillschweigend auf 0 % zu fallen.
* Die Diagnosezeile nennt mit `lage=` den Winkel zwischen kommandierter Lage und Retrograde — die
  Zahl, die diesen Fehler sofort gezeigt hätte.

Offen für den nächsten Flug: Der Eintritt war **steil** (450 m/s Sinken bei 68 km). Das Budget der
Eintrittsphase rechnet nur mit den Triebwerken, nicht mit der Luft, und lag dort bei 1420 m/s gegen
1953 m/s geflogene Geschwindigkeit — die Zündung kommt also weiterhin hoch. Ob `CoastMargin`
angehoben gehört, entscheidet der nächste Log.

## 0.9.6: der Widerstand war um Faktor 500 zu groß

Der dritte Flug lief bis 9 km gut: Ausrichtung retrograd (`lage=0.0`), Querfahrt von **1891 auf 100
m/s** abgebaut, Sinkrate auf der Leiter. Dann brach es ab:

```
11:33:53  h=9143  sink=217  lat=100.6  drag=884.7m/s2   cmd=0.0%
11:34:03  h=6927  sink=212  lat=106.1  drag=2472.6m/s2  cmd=0.0%
11:34:27  h=130   sink=88   lat=85.4   drag=1655.9m/s2  cmd=0.0%
```

`drag=884 m/s²` ist physikalisch unmöglich (real sind dort ~2 m/s²). Das Gesetz zieht den
Widerstand von der Schwerkraft ab, um die Vertikalvorgabe zu bilden — bei 2472 m/s² wird daraus
`max(0, 9,81 − 2472 + …) = 0`: **die Triebwerke blieben aus**, und der Booster fiel die letzten neun
Kilometer mit halbvollen Tanks ungebremst.

Die Quelle war die Dichtheitstabelle des Korridors, die aus `FlightGlobals.getAtmDensity(...)` gebaut
wird: Bei 9 km lieferte sie rund **500-mal** die Dichte, die das Spiel für den Booster selbst meldete
(`Vessel.atmDensity`). Dieselbe Tabelle speist die Zündvorhersage — deshalb stand dort
`tdPred=554840611541905000000000.0 m/s`.

Behoben:

* Der **gemessene** Widerstand rechnet mit `Vessel.atmDensity`, der Zahl, aus der KSP seine eigene
  Widerstandskraft bildet — nicht mehr mit der eigenen Tabelle. Zusätzlich auf 10 g gedeckelt: kein
  Booster wird härter gebremst als das Zehnfache seines Gewichts.
* Die Korridor-Tabelle **eicht sich selbst** gegen `Vessel.atmDensity` und meldet den Faktor als
  `rho=` in der Diagnosezeile. Weicht sie ab, wird sie skaliert statt geglaubt.
* Das Gesetz deckelt den anrechenbaren Widerstand auf 5 g — eine kaputte Messung kann die
  Triebwerke nicht mehr stumm schalten.

`rho=1.0x` in der nächsten Zeile heißt: Tabelle und Spiel sind einig. Steht dort etwas anderes, ist
die KSP-Abfrage die Ursache und ich sehe es sofort.

## 0.9.5, zweiter Flug: zu früh gezündet und weggeflogen

Der zweite Flug zeigte die Ausrichtung korrekt (`lage=0.0`, `tilt=77.8` — exakt retrograd), aber der
Booster zündete bei 60 km, hob die Sinkrate in 18 s auf **0** und stieg dann auf 98 km, während die
Querfahrt von 1900 auf **3679 m/s wuchs**. Drei Fehler, alle drei behoben:

**1. Der Bremsbefehl zeigte MIT der Drift.** In `AimBurn` stand

```csharp
double east = lateral * (East·f);     // f = Richtung, in die der Booster driftet
```

`lateral` ist positiv, solange gedriftet wird — der Befehl zeigte also in Driftrichtung und
beschleunigte sie. Richtig ist das Negative. Der Test prüft jetzt die einzige Eigenschaft, die ein
Bremsbefehl nie verletzen darf: `dot(a, v) < 0`.

**2. Unterhalb der Sinkratenleiter wurde gehalten statt fallen gelassen.** Die vertikale Vorgabe war
`g + Profilbremsung`, also *über* der Schwerkraft — ein Booster, der langsamer sinkt als die Leiter
erlaubt, wurde damit festgehalten: gemessen 600 s Schweben auf 31 km, Sinkrate 0, Tanks leer. Unterhalb
der Leiter sind die Triebwerke jetzt aus und die Schwerkraft schließt die Lücke. Dazu entfiel eine
Zeile, die bei *jedem* Querbefehl die vertikale Vorgabe auf `g` anhob — sie bedeutete in Wahrheit
„wer noch Drift hat, darf nie sinken".

**3. Der Testlauf selbst rechnete den Luftwiderstand verkehrt.** Im Modell wurde die
Widerstandsbremse zur Sinkrate *addiert* statt abgezogen. Bei 2 m/s Sinkrate fällt das nicht auf, bei
2200 m/s quer ist es ein Runaway — und es ist der Grund, warum das Modell den Eintritt vorher nie
landen konnte:

| Fall (Punktmassenmodell) | vorher | 0.9.5 |
|---|---|---|
| 2200 m/s quer aus 40 km | fliegt auf 750 km davon, nie am Boden | **Landung mit 1,74 m/s**, 4,6 m/s Restdrift, 14,4 t Treibstoff übrig |
| Eintritt 1200 m/s aus 25 km | Aufschlag mit 2070 m/s | Aufschlag mit 806 m/s |

Damit stimmen die Vorzeichen in `tests/GuidanceTests.cs` und im Gesetz überein, statt sich
gegenseitig zu kompensieren — der Testlauf ist jetzt ein ehrlicheres Abbild des Spiels.

## 0.9.4: die Vorhersage rechnete verkehrt herum

Die Zündvorhersage (`src/DescentPrediction.cs`) integriert den Rest des Abstiegs mit gezündeten
Triebwerken. In dieser Integration hatten **Schwerkraft und Schub das falsche Vorzeichen** — die
Zeile ließ den Schub den Sturz *beschleunigen*. Die Vorhersage war damit in jeder Lage
pessimistisch und behauptete, ein Booster komme mit 122 m/s an, wo 5 m/s herauskommen:

| Fall (Testlauf) | 0.9.3 | 0.9.4 |
|---|---|---|
| 500 m Fall, 0,5 m/s² Schub | 3727 m/s | 96,5 m/s (freier Fall, korrekt) |
| 500 m Fall, 25 m/s² Schub | 122 m/s | 5,0 m/s |
| 4000 m, 200 m/s Sink, 25 m/s² | „nicht abfangbar" | 4,5 m/s |

**Was das im Flug bewirkt hat:** `NICHT-ABFANGBAR` war praktisch immer gesetzt, und genau dieses
Kennzeichen schaltet die Eintrittsphase wieder ab. Der Coast wurde also in jedem Fall verworfen,
in dem er hätte greifen sollen — der Booster zündete oben und verbrannte den Treibstoff, der die
Landung trägt. Dieselben Zahlen aus dem Testfall: 8000 m / 100 m/s mit Coast in 0.9.4, kein Coast
in 0.9.3.

**Was sich an den Landungen selbst nicht ändert:** Bei den senkrechten Anflügen entscheidet das
Sicherheitsnetz des Profils, nicht die Vorhersage — der Vergleich der ausgelieferten 0.9.3 mit
0.9.4 liefert dort dieselben Zahlen (Aufsetzen 1,77 m/s, gleiche Massen, gleiche Zündhöhen). Neu
richtig sind die **Diagnosewerte** `tdPred` und `reserve` im Log; sie waren vorher erfunden.

Dazu zwei Korrekturen an der Integration selbst: der Brand wird jetzt **retrograd** gerechnet
(gegen die geflogene Geschwindigkeit, nicht nur gegen die Sinkrate) und mit
**Drosselrücknahme** auf die Zielsinkrate — vorher lief er mit Vollschub bis zum Boden, was einen
Booster mit TWR > 1 nach oben fliegt und die Steiggeschwindigkeit als Aufsetzgeschwindigkeit
meldet. Geprüft wird das jetzt direkt: freier Fall ohne Schub, sanfter Fall mit Schub, und die
Grenze „200 m/s aus 4 km sind zu schaffen, aus 300 m nicht".

### Eintrittsphase: die Luft bremst zuerst (0.9.3)

Das Gesetz hat jetzt eine Eintrittsphase. Solange die Triebwerke die gesamte verbleibende
Geschwindigkeit noch entfernen **könnten**, bleiben sie aus, der Booster zeigt rückwärts und der
Luftwiderstand erledigt die Arbeit:

```
Budget = freies_a · (Resthöhe / Sinkrate) · CoastMargin      CoastMargin = 0,5
Coast, solange  Gesamtgeschwindigkeit <= Budget
```

Das ist bewusst großzügig gerechnet: Wer zu früh bremst, verbraucht den Treibstoff, der die
Landung trägt; wer zu spät bremst, verbraucht den Booster. Während des Coasts ist auch der
**Querregler gesperrt** — Seitwärtsfahrt ist vor dem Bremsbrand kein Fehler, sondern das, was der
Luftwiderstand abbaut, und ein Booster, der quer steht, zeigt dem Luftstrom seine breite Seite.

`CoastMargin = 0` schaltet die Phase ab und stellt das alte Verhalten wieder her.

### Bekannte Grenze: der Wiedereintritt mit großer Bahngeschwindigkeit

Die Eintrittsphase ist erst der Anfang. Was weiterhin fehlt, ist eine **Zündentscheidung auf Höhe
*und* Gesamtgeschwindigkeit** sowie eine Landeprofilplanung, die die horizontale Geschwindigkeit
einbezieht. Im Testlauf gegen ein Punktmassenmodell ist ein Booster mit 2200 m/s quer aus 40 km
**nicht landbar**: die Zündung kommt bei 39,8 km, der Brand verbraucht sich damit, den Abstieg
abzufangen statt die Querfahrt, und am Ende steht der Booster mit leeren Tanks und 197 m/s
Restgeschwindigkeit in 750 km Höhe. Das Modell sagt in dieser Lage „nicht abfangbar", und mit
seinen eigenen Zahlen hat es recht — es hält eine einzelne Exponentialatmosphäre mit 5,6 km
Skalenhöhe, und Kerbin ist oberhalb von 15 km mehrere Male dichter als das; KSPs Drag-Cubes kommen
mit Mach-Abhängigkeit dazu. Beides wirkt in Richtung *mehr* Bremsung.

Für diesen Flug bleibt `guidanceMode = legacy` die sichere Wahl, bis die Eintrittsphase an echten
Drag-Cubes eingemessen ist. Die Phase ist im Modell nur eingeschränkt prüfbar: die eigentliche
Arbeit leistet der Überschallwiderstand, und den liefert nur KSP.

**Noch nicht im Spiel erprobt.** Der Testlauf verwendet ein idealisiertes Modell; KSPs echte Triebwerksrampen, Drag-Cubes und Gelände können davon abweichen. Es gibt deshalb einen Schalter:

```cfg
guidanceMode = predictive    // oder: legacy
```

`legacy` legt die übernommene MechJeb-Kette wieder in die Hand des Autopiloten — ohne Neubau, direkt in `PluginData/settings.cfg`.

## Einstellungen der neuen Landung

Alle in `GameData/PhysStageRecovery/PluginData/settings.cfg`, bei geschlossenem Spiel zu ändern:

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `guidanceMode` | `predictive` | `legacy` = alte MechJeb-Kette |
| `touchdownSpeed` | 5 | Zielsinkrate der letzten Meter [m/s] |
| `terminalAltitude` | 50 | Höhe über Grund, ab der senkrecht gesunken wird [m] |
| `tiltLimit` | 20 | größte Neigung beim Bremsen [Grad] |
| `thrustReserve` | 0,2 | Anteil des Schubs, der für Lageregelung und Zündverzug zurückgehalten wird |

`touchdownSpeed` und `terminalAltitude` sind die beiden Werte, die die Landung am stärksten prägen.

Die Statuszeile im Flugübersichtsfenster zeigt während der Triebwerkslandung Phase, Zielsinkrate, **voraussichtliche Aufsetzgeschwindigkeit**, **Reserve** und Schub. Die Reserve ist die Zahl, an der ein Fehlflug zuerst ablesbar ist: Sie wird kleiner, bevor etwas schiefgeht, und `Landing predict` im Log nennt dieselbe Zahl mit allen anderen Entscheidungswerten.

## Bodenerkennung in 0.8.2

Die Bodensuche ignoriert jetzt KSPs Layer 10 (Scaled Scenery). Dort liegt die verkleinerte Planetendarstellung, deren Collider keine Bodenentfernung in Flugkoordinaten liefern. In einem Flug mit 0.8.1 wurde dadurch in rund 69 km Höhe ein Bodenabstand von nur 11 m gemeldet. Die neue Bremswegprüfung wechselte daraufhin sofort in den Endanflug mit Vollschub; auch Landebeine und Autostaging konnten zu früh ausgelöst werden.

Der Layer wird bereits aus der Physikabfrage ausgeschlossen und zusätzlich bei der Trefferauswertung verworfen. Echte lokale Boden- und Gebäudecollider bleiben nutzbar; in großer Entfernung bleibt die prozedurale Geländeoberfläche maßgeblich. Der Regressionstest verwendet die protokollierten Höhen-, Geschwindigkeits- und Schubwerte: Der alte Treffer reproduziert den Vollschub, die korrigierte Messung hält die Triebwerke im schubfreien Flug. Die Anflugtests aus 0.8.1 bleiben bestehen.
## Landekorrekturen in 0.8.1

* Fallschirme werden im bestätigten Sinkflug beim ersten sicheren KSP-Zustand scharfgeschaltet. Die zusätzliche Höhen- und Stufensperre der früheren Öffnungsplanung entfällt, auch bei eingeschalteter Triebwerkslandung. Stock-Druckgrenze, Teilöffnung, volle Öffnungshöhe und Sicherheitsprüfungen bleiben wirksam.
* Der Anflug prüft zusätzlich den benötigten Bremsweg aus Geschwindigkeit einschließlich Seitwärtsbewegung, Schub und Schwerkraft. Die Prüfung berücksichtigt die Boosterunterkante, 200 m Anflugreserve und mindestens eine Sekunde Reaktionszeit. Sie benötigt weder Landebeine noch einen Kalibrierungsbrand.
* Die Widerstandsschätzung berücksichtigt alle sechs Bauteilseiten statt versehentlich nur der letzten. Zusätzlicher zukünftiger Fallschirmwiderstand wird nicht mehr vorausgesetzt. Bereits geöffnete Schirme wirken über ihre aktuellen Widerstandswürfel und die tatsächliche Geschwindigkeit.
* Die vorhandene Schubregelung bestimmt weiterhin den tatsächlichen Brennverlauf. Es gibt keinen erzwungenen Fünf-Sekunden-Brand auf einer festen Höhe. Lagekontrolle, Treibstoffbedarf und Absturzerkennung bleiben bestehen.

## Neues Fenster in 0.8.0

Die **Flugübersicht** zeigt das Kamerabild, Bodenabstand, Sinken, Entfernung, Fallschirmstatus und den aktuellen Flugabschnitt. **Einstellungen** enthält Reichweite, Autostaging mit Auslösehöhe und letzter Stufe sowie Triebwerkslandung mit Ziel-Sinken. Das dunkle Fenster ist frei skalierbar; bei breiten Fenstern stehen die Einstellungsbereiche nebeneinander, bei kleinen untereinander in einem Scrollbereich.

Das Fenster startet geschlossen und öffnet sich automatisch nur beim Erfassen einer **neu abgetrennten landefähigen Stufe**: mindestens ein nicht gekappter Stock-Fallschirm oder – bei aktivierter Triebwerkslandung – ein geeignetes Triebwerk mit nutzbarem Treibstoff. Reine Trümmer, leere Triebwerksstufen und das Wiederherstellen verfolgter Booster beim Laden öffnen es nicht. Ein geschlossenes Fenster bleibt für bereits erfasste Booster geschlossen. Die Sidebar und **Alt+B** erlauben jederzeit manuelles Öffnen.

**Mod** und **Auto-Bergung** werden unmittelbar gespeichert. Im Einstellungsreiter Änderungen mit **Speichern** übernehmen. Fenstergröße/-position, Kameradistanz und Kamerawinkel werden automatisch gespeichert. Drehen mit rechter Maustaste und Zoomen mit dem Mausrad funktionieren direkt über dem Kamerabild; über den Einstellungen scrollt das Mausrad nur die Einstellungen.

Fortgeschrittene Optionen stehen ausschließlich in `GameData/PhysStageRecovery/PluginData/settings.cfg`: `maxSinkSpeed` und die übrigen Sicherheitsgrenzen, `heatImmune`, `cameraEnabled`, `cameraFps`, `cameraDistance`, `cameraHeading`, `cameraPitch`, `autoArm`, `maxBoosters`, `autoOpenWindow` und `showDiagnostics`. `showDiagnostics = true` blendet die ausführlichen Anzeigen und den Diagnoseknopf wieder ein. Dateiänderungen bei geschlossenem Spiel vornehmen.

**Die Bergung oberhalb des Bodens wurde entfernt.** Die früheren Schlüssel `recoveryHeight`, `requireTouchdown` und `touchdownSeconds` werden beim Laden bereinigt. Deine in 0.7.9 verwendete sofortige Bergung bei unbeschädigtem Bodenkontakt einschließlich der bestehenden Gelände-Erkennung bleibt Grundlage der Bergung. Andere bestehende Werte werden übernommen.

## Landung

Im Flug das Symbol in der rechten Sidebar öffnen und unter Einstellungen **Triebwerkslandung** einschalten. Der Landeautomat beginnt nach bestätigtem Sinkflug. Verwendet werden MechJebs ursprüngliche Zustände `UntargetedDeorbit`, `CoastToDeceleration`, `DecelerationBurn` und `FinalDescent`, die Gravity-Turn- und die Coast-Sinkplanung, der Geschwindigkeitsregler und der Standard-Lageregler `BetterController`. Ein Landeziel ist nicht erforderlich und wird nicht vorgegeben.

Nach dem Abtrennen richtet die Steuerung den Booster rückwärts aus. Der atmosphärische Anflug schätzt eine Bremsendehöhe aus Luftdichte, Widerstand und Masse. Zusätzlich begrenzt die Bremswegprüfung aus 0.8.1 die Zeit ohne Schub: Eine günstige Widerstandsschätzung darf den Endanflug nicht über den nötigen Bremsbeginn hinaus verzögern. Danach steuern Gravity-Turn-Regelung und die Sinkrampe der letzten 300 m den Schub. Bremsklappen werden bewusst nicht automatisch ausgefahren.

Im Log bezeichnet `ende=` die atmosphärisch geschätzte Bremsendehöhe, `atm=` die atmosphärische Bremsplanung und `cda=` die geschätzte Widerstandsfläche. `brakeGuard=True` zeigt an, dass die zusätzliche Bremswegprüfung den Endanflug eingeleitet hat; `thrustAcc=` nennt die verfügbare Triebwerksbeschleunigung. Die tatsächliche Zündhöhe hängt von Flugbahn, Schub und Ausrichtung ab.

Der Booster benötigt regelbare, wiederzündbare axiale Raketentriebwerke, ausreichend Treibstoff und echte Lageregelung durch Reaktionsräder, RCS, Schubvektorsteuerung oder Steuerflächen. Fehlende Steuerbarkeit wird nicht künstlich ersetzt. Zum Zünden muss der Booster innerhalb von 45 Grad zur befohlenen Richtung ausgerichtet sein; ein bereits laufender Bremsbrand wird erst ab 100 Grad Abweichung abgeschaltet, damit ein laufender Bremsvorgang nicht während des Drehens abbricht.

**Bergung erfolgt ausschließlich bei Bodenkontakt.** Sobald der Booster den Boden berührt und der Aufprall nicht als Absturz bewertet wird, gilt er als geborgen: ohne Wartezeit, ohne Timer. Der Schub wird beim Aufsetzen beendet. Ein harter Aufprall oder Teileverlust wird nicht als Erfolg gewertet.

Bei eingeschalteter Fallschirmautomatik werden die Schirme beim ersten sicheren Zustand im Sinkflug scharfgeschaltet, ohne zusätzliche Höhenverzögerung. KSP entscheidet anhand seiner Druck- und Sicherheitsgrenzen über die tatsächliche Öffnung. **Landebeine hängen nur an der Höhe und am Sinken: unter 1000 m über Grund und im Sinkflug, bei jeder Geschwindigkeit.** Keine Geschwindigkeitsgrenze und kein Automatenzustand — ein schwerer Booster ist bei 1000 m noch schnell und bremst spät, ein leichter schwebt dort schon. Das Sinken wird aus dem Höhenverlauf gelesen (die Höhe über Grund wird kleiner) statt aus der gemeldeten Sinkrate: Ein Fahrzeug, das das Spiel gerade erzeugt oder versetzt hat, kann kurz eine Sinkrate melden, die nicht zu seiner Bewegung passt. Ein bereits ausgefahrener Gruppenstatus verhindert nicht mehr das Ausfahren einzelner noch eingefahrener Beine. Fallschirme werden im Steigflug weiterhin geschützt.

## Nur landen, nicht bergen

Ist die Auto-Bergung ausgeschaltet, bleibt der Booster nach dem Aufsetzen stehen und kann später von Hand geborgen werden. Das funktioniert auch in großer Entfernung, wo KSP keinen Boden-Collider gebaut hat: PhysStageRecovery meldet den Bodenkontakt dann an KSP selbst weiter und packt das Fahrzeug sofort mit dem Zustand *gelandet*. Ohne diesen Schritt hält KSP den Booster für fliegend und löscht ihn, sobald er beim Freigeben der Physikreichweite auf Schienen gelegt wird — er wäre dann einfach verschwunden. Im Log steht dazu eine Zeile `Landed booster handed to the game … landedNow=True packed=True`.

## Hitzeschutz (optional)

Ein Booster, der den Wiedereintritt sauber rückwärts ausgerichtet durchfliegt, zeigt dem Luftstrom seine kleinste Querschnittsfläche. Er bremst dadurch langsamer als ein taumelnder Booster und heizt sich stärker auf. KSP zerlegt ein Bauteil in `Part._CheckPartTemp`, sobald seine Temperatur oder seine Hauttemperatur `maxTemp` beziehungsweise `skinMaxTemp` überschreitet; ein eigenes Wiedereintritts-Schadensmodul gibt es in 1.12 nicht mehr.

Die Einstellung `heatImmune` in der Einstellungsdatei hebt genau diese beiden Grenzen für die Bauteile verfolgter Booster an und stellt die Originalwerte wieder her, sobald der Booster losgelassen oder geborgen wird. Andere Fahrzeuge, insbesondere die aktive Rakete, werden nie verändert: es ist bewusst nicht KSPs globaler Schalter `CheatOptions.IgnoreMaxTemperature`. Die Option ist standardmäßig an. Wird sie während eines Wiedereintritts abgeschaltet, zerbrechen bereits zu heiße Bauteile sofort wieder. Jede `Landing check`-Zeile nennt die Anzahl geschützter Bauteile als `heat=`.

## Weitere Funktionen

* Autostaging: Auslösehöhe im Sinkflug und letzte Stufe einschließlich dieser Stufe einstellen; maximal eine Stufe je Spielsekunde. Die aktive Hauptrakete wird nicht gestaged.
* Physikreichweite: 5 bis 2000 km, ausschließlich für verfolgte Booster. **KSP baut Boden-Collider nur rund um das aktive Fahrzeug.** In großer Entfernung hat ein verfolgter Booster deshalb gar keinen Boden unter sich: er fällt durch das sichtbare Gelände, und ein Landeautomat, der ihn weiter mit seiner Aufsetzgeschwindigkeit nach unten fliegt, gräbt ihn meterweise ein. Wo die Messung keinen Welt-Collider unter dem Booster findet, gilt deshalb die exakte Geländehöhe als Bodenkontakt; sobald KSP einen Collider gebaut hat, entscheidet immer der physische Kontakt. Jede `Landing check`-Zeile markiert diesen Fall mit `KONTAKT=hoehe`, und die Statuszeile zeigt „Aufgesetzt (Bodenhoehe, kein Collider)".
* Physikwarp bleibt freigeschaltet. Rails-Timewarp bleibt während der Verfolgung gesperrt.
* Fenstergröße und Position werden gespeichert; Kamera-FPS und Zoom bleiben unabhängig vom Landeautomaten. Die Steuerung läuft in jedem Physikschritt.
* Kamerasteuerung für das Boosterbild wie im Hauptfenster: **rechte Maustaste ziehen dreht die Ansicht, das Mausrad zoomt**. Die Steuerung gehört dem Boosterbild, solange der Mauszeiger über dem Mod-Fenster steht — dort ist die Hauptkamera ohnehin gesperrt, damit sich beide nicht gleichzeitig drehen. Über dem Hauptbild steuert der Zeiger wie gewohnt die Hauptansicht. Die früheren Schieberegler entfallen; die Kamerawerte werden gespeichert.
* Bestehende Einstellungen für Reichweite, Kamera, Fenster und Autostaging bleiben erhalten. Der frühere MechJeb-Schalter und die Bodenwartezeit entfallen.
* Diagnose: Jede Sekunde im Endanflug schreibt `KSP.log` eine Zeile `Landing check` mit Höhe, Sink- und Seitengeschwindigkeit, `err` (Lageabweichung zur befohlenen Richtung), `cmd` (angeforderter Schub), `heat` (geschützte Bauteile), `tiefe` (berechneter Abstand der bauteilunterkante zum Ursprung) und `guidance` (ausgeführter Schub samt Automatenzustand). `cmd` größer als `guidance` bedeutet, dass die Schubsperre eingegriffen hat; `cmd = 0 %` bedeutet, dass der Automat selbst keinen Schub wollte. Alle fünf Sekunden folgt eine `Terrain probe`-Zeile mit allen Bodenquellen, der Zahl der gefundenen Collider in allen Layern und einem als unplausibel verworfenen Collider, falls es einen gab.

## Installation und Build

KSP schließen. Den Ordner `GameData/PhysStageRecovery` nach `KSP/GameData` kopieren oder im Projekt `./install.ps1` ausführen. Das Installationsskript sichert die bisherige DLL und erhält `PluginData/settings.cfg`. KSP anschließend neu starten.

`./build.ps1` baut gegen die Bibliotheken der lokalen KSP-Installation und Harmony, ohne MechJeb-Referenzen. `./test.ps1` prüft Bergung, Aufprall, Einstellungen, Autostaging, Warp, Fallschirmschutz, die übernommenen MechJeb-Zustände sowie das neue Landegesetz und die Bodensuche. Der Compiler stammt aus Visual Studio Build Tools.

Die Sinkflugtests verwenden idealisierte Dynamik. Komplette Anflüge ab 25 km prüfen geringe und zusätzliche Luftreibung, Physikwarp sowie 0,5 und 5 m/s Zielgeschwindigkeit. Ohne Bremswegprüfung reproduziert dieselbe Testbahn den verspäteten Zündbeginn und harten Aufprall. Sie prüfen senkrechte und seitliche Anflüge, schwächeren Schub sowie Physikwarp. Separate Lagereglertests prüfen die Korrektur einer 45-Grad-Abweichung und fehlendes Drehmoment. Sie ersetzen keinen KSP-Testflug mit der konkreten Rakete.

Für das neue Gesetz gilt dasselbe: `GuidanceTests` fliegt senkrechte Anflüge, Hoverslams, Seitendrift, schwachen Schub, Höhenrauschen und einen Eintritt aus 25 km gegen ein geschlossenes Punktmassenmodell, und prüft die Bodensuche gegen synthetisches Gelände (flach, ansteigend, abfallend, Klippe, Hang). Der Eintritt aus 25 km ist dort **nicht** aussagekräftig, weil das Testmodell eine reine Exponentialatmosphäre verwendet, die zwischen 10 und 25 km weit dünner ist als Kerbins echte — der Test prüft deshalb nur, dass das Gesetz den Abstieg durchfliegt und endlich bleibt. KSPs echte Atmosphäre, Triebwerksrampen und Gelände muss der Flugtest zeigen.

## Quellcode und Lizenz

GPL-3.0, siehe `LICENSE`. Der vollständige zu dieser DLL gehörende Quellcode einschließlich Buildskripten liegt in `PhysStageRecovery-Source.zip`. Herkunft, festgeschriebener MechJeb-Commit und die Anpassungen an KSP/PhysStageRecovery stehen in `THIRD_PARTY.md`.

Es handelt sich um eine eigenständige Integration der MechJeb-Algorithmen, keine unveränderte Kopie des gesamten MechJeb-Mods. Insbesondere sind die Physikwarp-Anpassung des Schubreglers, die Schutzregeln für abgetrennte Booster und die sofortige Bergung nach Bodenkontakt Ergänzungen von PhysStageRecovery.

## 0.8.15: flacher Endanflug

Der Endanflug begrenzt neben der Gravity-Turn-Planung die gesamte Oberflächengeschwindigkeit anhand des verbleibenden Bremswegs mit 300 m Übergangsreserve und 20 Prozent Schubreserve. Damit wird Seitwärtsfahrt vor der letzten Landephase abgebaut. Der Übergang aus dem schubfreien Flug bei Erreichen der unabhängigen Bremsgrenze führt sofort in diesen geregelten Endanflug; er darf nicht erneut in einer unbegrenzten atmosphärischen Geschwindigkeitsplanung warten.

Steigt der Booster nach einem Bremsstoß unterhalb 300 m wieder, verwendet er die aufrechte Sink- und Driftregelung. Er folgt dann nicht dem nach unten wandernden Retrograde-Vektor. Die Zünd- und Lagesperren bleiben bestehen; vorhandene Drehmomente begrenzen weiterhin die tatsächlich mögliche Ausrichtung.

Für diese Änderung wurden auf Wunsch keine Tests ausgeführt; nur die DLL gebaut.
## 0.8.16: kontinuierliche letzte Landephase

Der letzte Flug zeigte 14 m/s bei 318 m und danach wechselnde Schub- und Lagebefehle bis zu 81 Grad Zielneigung bei 54 m. Die letzten 300 m verwenden jetzt dauerhaft die vertikale Sink- und Driftregelung; der bisherige separate Vollschub-Retrograde-Zweig entfällt. Nach kurzem Wiederaufsteigen wird nicht zurückgeschaltet. Die Zielneigung dieser bodennahen Regelung ist auf 30 Grad begrenzt.

Die Anfluggeschwindigkeit läuft am Übergang zur letzten Landephase stetig weiter, statt in der Anflugplanung auf null zu fallen und mit der nächsten Phase wieder hochzuspringen. Die 20-Prozent-Schubreserve bleibt bestehen. Neue Logwerte nose, control und torque zeigen tatsächliche Neigung, Steuereingaben und verfügbares Drehmoment; tilt bezeichnet weiterhin die angeforderte Richtung. Damit lässt sich die noch nicht eindeutig erklärte Retrograde-Abweichung im höheren Anflug von den behobenen Moduswechseln unterscheiden.

Auf Wunsch keine Tests ausgeführt; Nur gebaut und installiert.
## 0.8.17: Übergang nach Seitwärtsgeschwindigkeit

Unter 300 m wird wie bei MechJeb bei mindestens 5 m/s Seitwärtsgeschwindigkeit weiter retrograde gebremst. Erst unter 5 m/s übernimmt die vertikale Sinkregelung mit Driftkorrektur. Beim Wiederaufsteigen bleibt die bestehende Ausnahme bestehen, damit der Booster bodennah nicht nach unten zielt. Fehlender Schubüberschuss verwendet die maximale vertikale Bremsregelung.

Die pauschale Neigungsbegrenzung der Driftregelung entfällt im Sinkflug; sie gilt nur noch für die Wiederaufstiegs-Ausnahme. Beim Wechsel des Schubmodus wird der PID-Regler wie in MechJeb zurückgesetzt. Die übrige Anflugplanung und die Zünd-/Lagesperren bleiben erhalten. Auf Wunsch nur gebaut, keine Tests ausgeführt.
## 0.8.18: Plausible Bodenmessung und rückkehrbarer Endanflug

Boden-Collider werden nur akzeptiert, wenn ihre Entfernung höchstens 250 m von der berechneten Geländehöhe abweicht. Ein naher Fehlkontakt in großer Höhe kann dadurch weder eine bodennahe Landung noch das Ausfahren der Beine auslösen. Die 300-m-Endphase wird bei jedem Physikschritt neu bestimmt und bleibt nach einem einzelnen falschen Höhenwert nicht dauerhaft aktiv. Ohne gültigen Welt-Collider bleibt die bestehende Erkennung an der exakten Geländehöhe erhalten.

Auf Wunsch keine Tests ausgeführt; nur gebaut und installiert. Ein Flugtest steht aus.


## 0.9.11: Zündplanung mit vollständigem Atmosphärenprofil

Das Dichteprofil reicht jetzt bis zur tatsächlichen Atmosphärengrenze des Himmelskörpers; zuvor endete es bei 9500 m und lieferte darüber null. Geländeabstand und Höhe über Meeresspiegel werden getrennt behandelt. Die Triebwerksleistung folgt der Druck-/Isp-Kurve, die Beschleunigung der aktuellen Masse und dem tatsächlich verfügbaren Treibstoff. Der Verbrauch berücksichtigt das Mischungsverhältnis und den zuerst aufgebrauchten Treibstoff.

Eine gemeinsame Vorausberechnung integriert zuerst den unmotorisierten Eintritt und anschließend mögliche Landeburns. Sie wählt den spätesten im Modell geeigneten Zündpunkt, einschließlich Zündverzögerung, Schubaufbau, Massenschwund, Schwerkraft und Luftwiderstand entlang des Weges. Eine 20-km-Sperre und die bisherigen konkurrierenden Vakuum-/Dichte-Auslöser entfallen. Die Vorhersage wird vor der Zündung regelmäßig aktualisiert, statt oberhalb 3 km auf einem alten Ergebnis stehen zu bleiben. Ungültige Modellwerte verwenden eine konservative Bremsweg-Rückfallebene, die im Log ausdrücklich als fallback erscheint.

Ziel ist ein langsamer Übergang bei 100 m über dem Boden: etwa 10 m/s und höchstens 2 m/s seitlich. Das steilere Bremsprofil oberhalb 100 m erhält einen Vorsteueranteil; darunter bleiben Driftberuhigung, Aufrichtung, sanftes Abfangen und die bestehenden Aufsetzparameter wirksam. Die Vorhersage verwendet denselben Regler wie der Flug. Sie simuliert auch das letzte Fallen zwischen Triebwerksabschaltung und Bodenkontakt. Falls kein Kandidat alle Ziele erreicht, wird die beste vollständig berechnete Flugbahn gewählt; das allein erzwingt keine sofortige Zündung in großer Höhe.

Neue Logwerte: planZuendung, vBei100m und querBei100m. Bestehende Einstellungen bleiben erhalten. Das Modell schreibt den aktuell gemessenen Widerstandsquerschnitt konservativ fort; spätere Änderungen durch Machzahl, Lage, abgeworfene Bauteile oder Fallschirme sind erst nach erneuter Messung bekannt. Getrennte Treibstoffnetze werden konservativ behandelt. Der Plan ist keine Landegarantie bei unzureichender Steuerkraft oder Treibstoffmenge.

Auf Wunsch keine Tests ausgeführt. Nur gegen die installierten KSP-Bibliotheken gebaut; die Bestätigung im Spiel steht aus.
## 0.9.12: Retrograde vor dem Burn und einstellbare Zielhöhe

Die CSV des Flugs vom 23.09.2026 zeigte bei 68246 m bereits die Phase Bremszündung, aber noch 0 % Schub. Der Richtungsregler interpretierte den Null-Beschleunigungsvektor als radial nach oben. Solange noch kein positiver Bremsbefehl ausgegeben wurde, bleibt die Ausrichtung jetzt retrograde; nach dem ersten Bremsbefehl bleiben Ausrichtung, Bremsprofil und Endlandung wie in 0.9.11.

In `GameData/PhysStageRecovery/PluginData/settings.cfg` ist die Zielhöhe mit `captureAltitude = 100` editierbar (Meter über dem Boden, 10 bis 5000 m). Dort soll der Hauptbremsvorgang die Geschwindigkeit auf ungefähr 10 m/s reduziert haben. Der Wert wird geladen, gespeichert und an Vorhersage sowie Bremsprofil weitergegeben. Er ist unabhängig von `terminalAltitude`, das die abschließende Lage-/Neigungsregelung steuert. Die Datei bei geschlossenem Spiel bearbeiten und KSP anschließend starten. Bestehende Konfigurationen erhalten den Standardwert 100, alle anderen Werte bleiben erhalten.

Die Logfelder heißen jetzt `zielHoehe`, `vBeiZielhoehe` und `querBeiZielhoehe`. Keine Tests ausgeführt; nur gebaut. Die Bestätigung der Ausrichtung im Spiel steht aus.
## 0.9.13: Aufgeräumte Flugübersicht mit Tankanzeige

Die vier Kacheln zeigen Bodenabstand, Surface Speed (Betrag der tatsächlichen Oberflächengeschwindigkeit), Entfernung zur aktiven Rakete und Rest-Δv. Die letzte Kachel enthält einen horizontalen Tankbalken für den massengewichteten Füllstand der Landetreibstofftanks. Bei weniger als 15 Prozent wird der Balken bernsteinfarben. Der Zahlenwert ist das verbleibende Vakuum-Δv für die geeigneten Landetriebwerke; das Tankvolumen und die nutzbare Reichweite sind unterschiedliche Größen. Fahrzeuge ohne solche Triebwerke zeigen einen Gedankenstrich. Schmale Fenster ordnen die Kacheln in zwei Reihen an.

Unter den Kacheln bleiben nur Zustand und Schub (angewandte Drosselstellung). Zielgeschwindigkeit, Vorhersage, Reserve und die übrigen Debugzeilen entfallen in der Flugübersicht; CSV und Logs bleiben erhalten. Die Anzeige misst Tankdaten zweimal pro Sekunde unabhängig vom aktivierten Landeautomaten und greift nicht in die Flugsteuerung ein. Bestehende Einstellungen bleiben erhalten. Nur gebaut, keine Tests oder Ingame-Sichtprüfung ausgeführt.
## 0.9.14: Gemeinsames Abbremsen von Sinkflug und Seitwärtsfahrt

Der Hauptburn plant beide Geschwindigkeitsanteile mit derselben verbleibenden Zeit bis zur eingestellten Zielhöhe. Seitwärtsbremsung wartet dadurch nicht mehr auf eine überschrittene Sinkgeschwindigkeitsgrenze. Die gemessene Luftbremsung wird je Richtung berücksichtigt. Reicht die reservierte Schubleistung nicht, darf der Regler bis zum verfügbaren Vollschub gehen; bei knappem Bremsweg hat das Abfangen der Sinkgeschwindigkeit Vorrang. Die bodennahe Neigungsbegrenzung bleibt für langsame Drift wirksam. Die bestehenden Lage- und Zündsperren bleiben erhalten.

Die Flugbahnvorhersage wird auch während des Burns aus dem aktuellen Zustand erneuert und übernimmt dafür den Reglerzustand und die vergangene Zeit seit dem ersten positiven Schubbefehl. Ein intern geplanter, aber noch schubfreier Burn darf neu geplant werden. Bis zum ersten Schubbefehl bleibt die Retrograde-Ausrichtung erhalten. Die Logs kennzeichnen Vektorbremsung und Notbremsung.

Bestehende Einstellungen bleiben erhalten. Keine Tests ausgeführt; die Bestätigung mit dem betroffenen Booster im Spiel steht aus.
## 0.9.15: Kein Leerlaufburn in großer Höhe und ruckelfreie Vorausberechnung

Die CSV des Flugs vom 24.09.2026 zeigte die Zündung bei 26213 m mit 2006 m/s und einem geplanten Burn von 196 s; tatsächlich genügten später rund 100 s. Der Grund lag in der Auswahl des Zündpunkts: Die Vorhersage verlangte am Rand des Auffangfensters die exakte Einhaltung des seitlichen Zieltempos. Lenkverluste ließen dadurch jeden Kandidaten knapp außerhalb liegen, kein Kandidat galt als geeignet, und die Ersatzwahl nahm den zeitlich frühesten Burn - sichtbar als `ersatzplan=ja` und `planZuendung` gleich der aktuellen Höhe. Jetzt zielt die Bremsung innerhalb des Fensters (halbe zulässige Seitwärtsgeschwindigkeit), die Prüfung erlaubt eine geringe Überschreitung (0,5 m/s), und bei mehreren gleich guten Ersatzplänen gewinnt der mit dem geringeren Brennstoffbedarf. Derselbe Flugzustand ergibt damit einen Plan bei 13,7 km mit 855 m/s statt einer Zündung bei 26,2 km mit 1929 m/s.

Die reine Vorausberechnung läuft nicht mehr im Physikthread. Eine einzelne Rechnung dauert auf diesem Rechner je nach Zustand 1 bis 40 ms; in jedem Physikschritt war das das Ruckeln zwischen Abtrennung und Zündung. Der Regler verwendet jetzt das zuletzt fertige Ergebnis und wartet nie auf das nächste. Das erste Ergebnis kommt wenige Schritte später; solange eine Rechnung läuft, unterbleibt die konservative Zündung aus der Bremsweg-Rückfallebene, außer der Boden ist weniger als 5 s entfernt. Ein Plan verfällt nach 2 s unter 1000 m und nach 5 s darüber.

Neue Logwerte: `planMs`, `kandidaten`, `ersatzplan` und `planAlter`. Im Flugfenster erscheint bei einem Fehler in der Rechnung die Ursache als `planFehler`. Bestehende Einstellungen bleiben erhalten.

Geprüft mit `.\test.ps1`: Die Regelungstests fliegen die Vorausberechnung deterministisch im selben Thread, weil eine Testuhr, die tausendfach schneller läuft als die Wanduhr, ein Hintergrundergebnis nie rechtzeitig sehen würde; der Hintergrundweg hat einen eigenen Test, der auch prüft, dass er nicht blockiert und dasselbe Ergebnis liefert. Dabei kam ein Fehler der Testumgebung heraus: Der Prüfstand setzte `AltitudeAsl` nie, sodass die Vorhersage für einen Booster in 40 km Höhe mit Luft in Meeresniveau rechnete. Offen bleibt ein Test: Die Aufsetzgeschwindigkeit bei künstlichem Rauschen von ±3 m auf den Bodenabstand liegt mit 9,5 m/s über der Grenze von 9 m/s dieses Stresstests. Das bestand schon vor 0.9.15 und ist eine Frage der Filterung des letzten Höhenwerts, nicht der Zündplanung.
## 0.9.16: Späte Zündung auch bei starker Seitwärtsfahrt (in 0.9.17 zurückgenommen)

Die Flüge vom 24.09.2026 um 08:06 und 08:39 zeigten den schweren Booster weiterhin mit Zündung in großer Höhe: 24048 m bzw. 16786 m, danach 6 bis 9 % Schub bis in Bodennähe. Der kleine Booster (4,8 t, 134 m/s Seitwärtsfahrt) zündete bei 1571 m und flog einwandfrei; der Unterschied ist die Seitwärtsfahrt von rund 1800 m/s beim schweren Booster.

Die Kandidatenliste des Planers für den Zustand bei 16786 m erklärt es vollständig. Bewertet wird jeder mögliche Zündpunkt entlang der antriebslosen Flugbahn:

```
i=15 h= 2967 m  Aufsetzen 6,0 m/s  quer bei 50 m 3,87 m/s
i=13 h= 4706 m  Aufsetzen 5,9 m/s  quer bei 50 m 4,14 m/s
i= 4 h=12875 m  Aufsetzen 6,0 m/s  quer bei 50 m 6,06 m/s
i= 0 h=16793 m  Aufsetzen 6,0 m/s  quer bei 50 m 2,49 m/s  <- der einzige "sichere"
```

Jeder spätere Zündpunkt scheiterte ausschließlich an der seitlichen Auffanggrenze von 2,5 m/s (2 m/s Ziel plus 0,5 m/s Toleranz), nicht am Aufsetzen: 5,9 bis 6,1 m/s in allen Zeilen. Damit blieb nur "sofort zünden" übrig. Die Grenze ist jetzt 4 m/s (also 6 m/s im Auffangpunkt). Sie ist eine Qualitätsgrenze für den Plan und keine Landegrenze: Ein Kandidat wird nur angenommen, wenn dieselbe Regelung ihn anschließend weich aufsetzt, und der Endanflug unterhalb der Auffanghöhe hat ein Vielfaches dieser Querdrift an Steuerautorität. Der aufgezeichnete Flug kam mit 9 m/s Querdrift bei 62 m an und setzte mit 1 m/s seitlich auf.

Derselbe Flugzustand ergibt damit Zündung bei 2683 m, 23 s Brenndauer und 626 m/s statt Zündung bei 16786 m, 152 s und 1365 m/s. Eine geschlossene Simulation mit den Triebwerks- und Atmosphärentabellen genau dieses Fluges bestätigt es unabhängig: Zündung bei rund 3 km, 4,5 t statt 8,5 t Treibstoff, Aufsetzen 5,9 m/s. Der kleine Booster und der steile Eintritt mit 963 m/s Sinkgeschwindigkeit bleiben unverändert; dort ist die frühe Zündung physikalisch nötig.

Neuer Test: Der aufgezeichnete Zustand (26,4 t, 16786 m, 338 m/s Sink, 1779 m/s Seitwärts, 4,7 m² Widerstandsfläche) muss unter 6000 m zünden und unter 6,5 t verbrauchen. Mit der alten Grenze zündet derselbe Testfall bei 10403 m und fällt durch.

**Nachtrag:** Diese Änderung wurde in 0.9.17 zurückgenommen. Die Begründung unten.
## 0.9.17: Zurückgenommene Auffanggrenze und die Ursache des Aufschlags vom 09:07

Die in 0.9.16 auf 4 m/s erweiterte seitliche Auffanggrenze ist wieder 0,5 m/s. Der Flug vom 24.09.2026 um 09:07 hat gezeigt, warum: Die Vorausberechnung nimmt an, dass der Schub dort wirkt, wohin sie ihn befiehlt. Dieses Fahrzeug kann diese Achse oberhalb etwa eines Kilometers nicht halten. Der Luftstrom stellt es rückwärts an, und die CSV zeigt 50 bis 70 Grad Lagefehler gegen einen nahezu senkrechten Befehl - der Triebwerksschub drückte also gegen die Flugbahn statt zu tragen. Mit der engen Grenze bleiben die Eintritte, die landen, bei ihrer frühen Zündung; mit der weiten rutschen sie auf genau den späten Burn, der hier aufgeprallt ist.

Der Flug vom 09:07 (27,6 t) im Einzelnen: Zündung bei 4274 m mit 320 m/s Sink und 793 m/s Seitwärts, danach 10 bis 18 % Schub bis 1 km. Dort noch 217 m/s Sink und 315 m/s seitlich. Ab etwa 500 m dann 100 % Schub, aber es fehlten Höhe und Zeit: Aufschlag mit 111 m/s bei 2,4 m. Der Plan hatte an der Zündstelle noch ein weiches Aufsetzen mit 6,0 m/s vorhergesagt; am Ende stand `predTd` bei 134 m/s und die Reserve bei -124 m/s. Das ist keine Fehlfunktion der Drossel, sondern eine zu optimistische Vorhersage.

Dieser Flug wäre mit 0.9.15 genauso aufgeschlagen: Sein Zustand ergibt bei enger wie bei weiter Grenze denselben Plan bei etwa 4,1 km (dort als Ersatzplan, weil kein Kandidat das Auffangfenster traf). Die weite Grenze war also nicht die Ursache dieses Aufschlags - sie hätte nur die beiden Eintritte, die landen, auf denselben ungeprüften späten Burn verschoben.

Was der Vorhersage fehlt, ist das Modell der Schubachse. Sie fliegt jeden Versuch entlang des befohlenen Vektors, weshalb sie für einen späten Burn ein weiches Aufsetzen berechnet, das das Fahrzeug nicht fliegen kann. Dasselbe gilt für die Sinkratenleiter, deren erreichbare Verzögerung ebenfalls senkrechten Schub annimmt. Solange beides fehlt, ist die enge Auffanggrenze die sichere Zahl. Der Testfall dazu steht als `TestHeavySidewaysEntryKeepsItsEarlyBurn` in der Testdatei und hält die frühe Zündung fest (gemessen 10403 m, Aufsetzen 5,64 m/s, 0,62 m/s seitlich, 5,7 t Treibstoff).

Dieser Stand ist als Baseline eingefroren: `build/backups/baseline-0.9.17-20260924-092207` mit Prüfsummen in `SHA256SUMS.txt`, beschrieben in `docs/BASELINE-0.9.17.md`. Nach dem Flug vom 24.09.2026 zündet der schwere Booster damit wieder früh und landet.
## 0.9.18: Vorhersage kennt die Schubachse

Die Vorausberechnung setzt den Triebwerksschub jetzt in die Richtung, in die das Fahrzeug ihn tatsächlich bringt. Oberhalb eines Staudrucks von 40 kPa gehört das Heck dem Luftstrom: Der Booster fliegt den relativen Wind, also bremst das Triebwerk entlang der Flugbahn, statt den Sinkflug zu tragen. Darunter wird der Befehl wieder bestimmend, sodass der langsame, tiefe Teil eines Versuchs - der Teil, der landet - unverändert bleibt.

Die Zahlen stammen aus den Flügen vom 24.09.2026. Der Lagefehler im Log entsprach genau dem Winkel zwischen Befehl und Flugbahn: 74 Grad bei 15 km, 63 Grad bei 3,8 km, und er fiel erst auf null, als die Luft ihren Impuls verloren hatte - 0,3 Grad bei 738 m und 21 kPa beim 26,4-t-Booster, 1,5 Grad bei 648 m und 46 kPa beim 27,6-t-Booster. Die 40 kPa liegen zwischen beiden Messungen, auf der vorsichtigen Seite: Eine zu düstere Vorhersage zündet nur früher, und früh zünden ist das, was diese Eintritte landet; eine zu optimistische fliegt sie in den Boden.

Rückrechnung der vier aufgezeichneten Flüge mit dem neuen Modell, geschlossen und mit den Atmosphären-, Schub- und Widerstandstabellen der jeweiligen CSV:

| Flug | echte Zündung | echtes Ergebnis | Modell: Zündung | Modell: Aufsetzen |
|---|---|---|---|---|
| 08:39, 26,4 t | 16786 m | 5,5 m/s, gelandet | 26137 m | 5,9 m/s |
| 09:07, 27,6 t | 4274 m | 111 m/s, **Aufschlag** | 29792 m | 5,9 m/s |
| 08:06, 27,6 t | 24048 m | 6,3 m/s, gelandet | 32270 m | 5,8 m/s |
| 08:11, 4,8 t | 1571 m | 7,4 m/s, gelandet | 1316 m | 5,9 m/s |

Der Aufschlagflug wird damit gerettet: Das Modell erkennt, dass kein Zündpunkt ab 4 km noch reicht, und zündet bei 29,8 km, wo die Luft die Seitwärtsfahrt noch rechtzeitig herausnimmt. Die drei anderen landen weiterhin, der kleine Booster praktisch unverändert.

Neuer Test `TestForecastKnowsTheThrustAxisIsNotItsOwn`: Für den Zustand des Aufschlags (27,6 t, 4280 m, 321 m/s Sink, 794 m/s seitlich) muss die Vorhersage ein hartes Aufsetzen melden. Sie sagt 139,7 m/s voraus, geflogen wurden 111 m/s - vorher stand dort "weich, 6,0 m/s".

Noch nicht geändert ist die Befehlsseite: Der Regler fordert weiterhin den kleinen senkrechten Schub an, der in Wahrheit rückwärts wirkt. Die Sinkratenleiter nimmt also nach wie vor senkrechten Schub als erreichbare Verzögerung an. Sie zu lehren, den Schub als das zu benutzen, was er ist - eine Bremse entlang der Flugbahn -, ist der nächste Schritt; er würde die frühe Zündung verkürzen statt sie nur ehrlich zu machen.
## 0.9.19: Volllastbremsung bis 50 m, dann Auslaufen auf die Aufsetzgeschwindigkeit

Der Bremsbefehl ist jetzt das, was er physikalisch ist. Bis zur Auffanghöhe (50 m) arbeitet der Hauptburn mit **voller Schubleistung** und zielt so, dass im 50-m-Punkt genau die Auffanggeschwindigkeit senkrecht und **keine Seitwärtsfahrt** mehr übrig ist. Unterhalb übernimmt die Endphase und läuft die Sinkrate vom Auffangwert auf die Aufsetzgeschwindigkeit aus. Die alte Sinkratenleiter mit Teilschub ist damit keine Führungsgröße mehr, sondern nur noch die Obergrenze, gegen die die Endphase nachgeführt wird.

Dazu gehört die Schubachse, und die war in 0.9.18 nur der Vorhersage bekannt, nicht dem Befehl. Solange der Luftstrom das Heck hält, geht ein senkrechter Befehl als Schub entlang der Flugbahn raus. Der Regler zielt deshalb im Mittel aus dem, was er will, und dem, was die Luft zulässt, und lässt das Triebwerk in dieser Phase mit voller Leistung laufen: In der Phase kann der Burn nur Fahrt herausnehmen, und jeder zurückgehaltene Newton wird mit Zündhöhe bezahlt. Beide Komponenten sind dabei begrenzt - der Bahnschub darf die Sinkrate nicht ganz wegnehmen (sonst hängt der Booster am eigenen Triebwerk, gemessen: 600 s Stillstand in 31 km mit 20 t Treibstoff über Bord) und die Drift nicht über null hinaus drücken.

Rückrechnung der fünf aufgezeichneten Flüge mit den Tabellen der jeweiligen CSV:

| Flug | Zündung | Drossel mittel/max | Sink bei 50 m | Aufsetzen sink/quer | Treibstoff |
|---|---|---|---|---|---|
| 08:39, 26,4 t | 1738 m | 37 % / 97 % | 5,3 m/s | 5,6 / 0,44 m/s | 3,5 t |
| 09:07, 27,6 t (**Absturz**) | 4769 m | 35 % / 100 % | 5,0 m/s | 5,6 / 0,08 m/s | 8,8 t |
| 08:06, 27,6 t | 2118 m | 41 % / 100 % | 5,3 m/s | 5,6 / 0,44 m/s | 1,8 t |
| 08:12, 20,3 t | 3804 m | 31 % / 100 % | 5,1 m/s | 5,6 / 0,11 m/s | 3,7 t |
| 08:11, 4,8 t | 1667 m | 32 % / 83 % | 5,3 m/s | 5,6 / 0,28 m/s | 0,5 t |

Alle fünf landen weich, auch der Zustand, der am 09:07 aufgeschlagen ist. Die Zündung liegt jetzt dort, wo der volle Schub gebraucht wird, statt 15 km darüber, und der 26,4-t-Fall braucht 3,5 t statt 4,5 t wie im echten Flug.

Zwei Tests halten das fest: `TestHeavySidewaysEntryBrakesLateAndHard` (26 t mit 1779 m/s Seitwärtsfahrt: Zündung unter 8000 m, weiches Aufsetzen) und `TestTheCrashedStateLandsWithTheAxisAwareBurn` (der Zustand des Aufschlags landet: Zündung bei 3315 m, Aufsetzen 5,56 m/s).

Was noch offen ist: Das Triebwerk schaltet weiterhin 1,5 m über dem Boden ab, der Booster fällt dieses Stück frei. Deshalb steht im Log als Aufsetzgeschwindigkeit rund 5,6 m/s, obwohl die Endphase auf 2 m/s ausläuft. Wer 2 m/s beim Kontakt will, muss die Abschalthöhe senken; das macht die Landung empfindlicher gegen die Höhenmessung.
## 0.9.20: Spätere Zündung bei Eintritten mit viel Seitwärtsfahrt

Die Flüge vom 24.09.2026 um 09:51 und 09:56 zeigten beide Booster mit einer Zündung, die etwas zu hoch lag, gefolgt von einem langen, flachen Sinkflug bei 30 bis 50 % Schub. Beim schweren Booster lief die Volllastphase nur von 4,2 km bis 2,2 km, danach modulierte der Regler 48 s lang herunter - 7,9 t Treibstoff für eine Landung.

Die Ursache stand in der Kandidatenliste des Planers für den Zustand bei 2,5 km. Jeder Zündpunkt wurde nur wegen der seitlichen Auffanggrenze verworfen, nicht wegen des Aufsetzens:

```
i= 2 h=2202 m  Aufsetzen 6,0 m/s  quer bei 50 m 3,58 m/s
i= 1 h=2348 m  Aufsetzen 5,9 m/s  quer bei 50 m 2,67 m/s
i= 0 h=2497 m  Aufsetzen 5,8 m/s  quer bei 50 m 2,05 m/s   <- allesamt über der Grenze von 1,0 m/s
```

Mit 1,0 m/s als Grenze bleibt kein Kandidat unter 4,4 km übrig, also zündet der Planer dort. Die Grenze ist auf 4,5 m/s erweitert - und das ist kein Zufallsschritt, denn der Burn zielt ohnehin immer auf null Seitwärtsfahrt; die Grenze entscheidet nur, welchen Zündpunkt der Planer akzeptiert. Genau das war 0.9.16 schon einmal versucht worden und musste zurückgenommen werden, weil die Vorhersage damals die Schubachse nicht kannte und einen Burn plant, den das Fahrzeug nicht fliegen konnte. Seit 0.9.18 kennt sie die Achse, seit 0.9.19 benutzt der Befehl sie - damit ist die Grenze wieder eine reine Qualitätsfrage.

Rückrechnung der drei jüngsten Flüge, Übergabe an die Regelung in 60 km Höhe (also das, was im Spiel passiert), mit den Tabellen der jeweiligen CSV:

| Flug | Zündung vorher | Treibstoff vorher | Zündung jetzt | Treibstoff jetzt |
|---|---|---|---|---|
| 09:51, 27,6 t | 4397 m | 5,10 t | **3161 m** | **4,02 t** |
| 09:56, 4,8 t | 1664 m | 0,76 t | **1314 m** | **0,67 t** |
| 09:07, 27,6 t (Absturzzustand) | 4769 m | 9,15 t | **2555 m** | **5,84 t** |

Alle drei landen weiterhin weich (5,6 m/s, 0,2 bis 0,5 m/s Restdrift), auch der Zustand des Aufschlags. Mehr als 4 m/s Grenze ändert nichts mehr, ab dort entscheiden Aufsetzgeschwindigkeit und Endphase über den Zündpunkt.

Im Spiel bestätigt: sechs Flüge mit 0.9.20. Die mittleren Booster zünden bei 1,1 bis 1,6 km statt bei 4,3 km, der schwere Fall (48,4 t, 1883 m/s Seitwärtsfahrt) hält von 16 km bis 2 km durchgehend 100 % Schub und moduliert erst im letzten Kilometer herunter. Aufgesetzt wird mit 5,4 bis 6,9 m/s und 0,3 bis 2,0 m/s Restdrift.

Dieser Stand ist als Baseline eingefroren: `build/backups/baseline-0.9.20-20260924-103455` mit Prüfsummen in `SHA256SUMS.txt`, beschrieben in `docs/BASELINE-0.9.20.md`. Die Baseline 0.9.17 von 09:22 liegt unverändert daneben.
