# Booster-Landung von Grund auf — Entwurf

Dieses Dokument beschreibt einen Neuentwurf der Triebwerkslandung, **unabhängig vom
bisherigen Stand**. Es erklärt zuerst, warum die aktuelle Kette aus MechJeb-Zuständen
(`UntargetedDeorbit` → `CoastToDeceleration` → `DecelerationBurn` → `FinalDescent`) für
einen abgetrennten Booster strukturell nicht tragen kann, und leitet daraus eine
Architektur ab, die aus **einer** Regelgleichung und **einer** Phasenachse besteht.

Die Phasenkette bleibt als Rückfallebene erhalten, wird aber nicht mehr zum Landen benutzt.

---

## 1. Missionsprofil

Ausgangslage: Abtrennung in ~500 km Höhe, keine Besatzung, kein Landeziel, kein MechJeb an
Bord. Der Booster hat axiale, drosselbare, wiederzündbare Triebwerke, begrenzten Treibstoff,
begrenztes Drehmoment und einen Hitzeschutz (`heatImmune`), der ihn den Wiedereintritt
überleben lässt.

```
  Abtrennung 500 km
        │
        ▼
  ① AUSRICHTEN        retrograde drehen, Triebwerke aus, Systeme bereit
        │             (Dauer ~10-30 s, nur Drehmoment nötig)
        ▼
  ② EINTRITT          Atmosphäre, Triebwerke aus, Lage retrograde halten
        │             Höhe 70 km → 5 km, Geschwindigkeit 2300 → 300 m/s durch Luftreibung
        ▼
  ③ BREMSZÜNDUNG      ein Brennstoß, dessen Länge aus einer numerischen Vorhersage folgt
        │             Höhe ~2 km → ~50 m, Geschwindigkeit 300 → 5 m/s senkrecht
        ▼
  ④ LETZTE 50 m       5 m/s senkrecht, Drift gegen null, Neigung gegen null
        │
        ▼
  ⑤ AUFSETZEN        Schub aus, Landebeine unten, Bodenkontakt, Übergabe/Bergung
```

Kern der Sache: **③ und ④ sind kein Umschalten zwischen zwei Flugreglern**, sondern eine
einzige Sollwertvorgabe für die Sinkrate, die mit fallender Höhe stetig von „was die
Physik hergibt" auf „5 m/s" übergeht. Deshalb gibt es dort nichts zu oszillieren.

---

## 2. Warum die MechJeb-Kette auf einem Booster nicht funktioniert

MechJebs Landeautomat ist für ein Landefahrzeug mit Ziel, Landebeinen und einem
Triebwerk gebaut, das über den ganzen Anflug Reserve hat. Sieben Eigenschaften übertragen
sich nicht auf einen Booster, und jede davon ist in den Flugprotokollen des Mods sichtbar
geworden.

### 2.1 Geschwindigkeit und Flugrichtung sind dort gekoppelt

In MechJebs Vakuumphase bedeutet „maximal erlaubte Geschwindigkeit" zugleich die
Flugrichtung: das Triebwerk zeigt retrograde, der gesamte Schub bremst. Der Regler
(*„Bremsung auf X m/s"*, `DecelerationBurn.Drive`) ist ein reiner Geschwindigkeitsregler mit
Zeitkonstante 0,3 s, der Schub zwischen 0 und 100 % schiebt.

Das funktioniert nur, solange Drehmoment und Schubüberschuss großzügig sind. Ein Booster
hat typisch **TWR 1,3–2,5** und in der Atmosphäre ein Massen-/Trägheitsverhältnis, das
Trägheitsmomente im Bereich 10⁵–10⁶ kg·m² erzeugt. Der Regler kommandiert dann Neigungen,
die das Fahrzeug nicht fliegen kann; die tatsächliche Neigung weicht ab, der Schub wirkt
teilweise seitwärts, die Höhe bricht ein. Genau das steht in den Logzeilen als
`err=…deg` neben `cmd=…%` und `guidance=…`.

**Konsequenz im Entwurf:** Richtung und Betrag des Schubs werden getrennt geplant. Der
Betrag kommt aus der Sinkratenvorgabe, die Richtung aus einem Beschleunigungsvektor, dessen
Längsanteil nach oben durch das Triebwerk gedeckt ist.

### 2.2 Der Zündzeitpunkt entsteht aus drei unabhängigen Schätzungen

Aktuell zündet der Bremsbrand, wenn eine von drei Bedingungen zuerst greift:

| Quelle | Formel | Schwäche |
|---|---|---|
| `DecelerationEndAltitude` | `1,1 · DragLength + terrain` | reine Atmosphärencharakteristik, kennt Schub und Masse nur über die Drag-Länge |
| `BrakingEnvelope` | Gravitationsumkehr-Policy bei `terrain + 200 m` | `terrainRadius + 200` ist die Endhöhe, nicht die Zündhöhe — die 200 m Reserve sind in Wirklichkeit der Nullpunkt der Simulation |
| `DragFreeAllowedSpeed` | Gravitationsumkehr ohne Luft | nur als Notbremse nutzbar, nicht als Plan |

Drei Schätzungen, drei Fehlerquellen, drei verschiedene Antworten auf „wann zünden?".
Dass die tatsächliche Zündung dann noch von Flugbahn, Schub **und Ausrichtung** abhängt,
steht im README bereits als Einschränkung — das ist der Kern des Problems.

**Konsequenz im Entwurf:** Es gibt genau **eine** Vorhersage, und sie ist eine
Zeitschritt-Integration des tatsächlichen Abstiegs mit Masse, Schub, Luftdichte und
Widerstand. Der Zündzeitpunkt ist der Zeitpunkt, an dem diese Integration nicht mehr
aufgeht.

### 2.3 Phasenumschaltungen mit unstetigen Sollwerten

`FinalDescent` springt bei 300 m zwischen `KEEP_SURFACE` (retrograd, Geschwindigkeitsregler),
`KEEP_VERTICAL` (vertikale Sinkregelung) und `OFF` (Vollschub retrograd) um. Die
Zielneigung springt dabei zwischen ~0° und 88°. Die Flugprotokolle zeigen genau das:
14 m/s bei 318 m, danach wechselnde Befehle, 81° Zielneigung bei 54 m. Die späteren
Versionen (0.8.15–0.8.18) haben diese Sprünge einzeln abgefedert — die Ursache, ein
diskontinuierlicher Sollwert, blieb.

**Konsequenz im Entwurf:** Der Sollwert ist eine **stetige Funktion der Höhe**, und der
Regler ist in allen Höhen derselbe.

### 2.4 Gier-/Nickachse wird singulär, wenn die Geschwindigkeit klein wird

Retrograd ist als Richtung definiert durch `-v̂`. Bei 2 m/s Restgeschwindigkeit wandert
diese Richtung mit jeder Störung um große Winkel. Ein Lageregler, der ihr folgt, dreht den
Booster kurz vor dem Boden. Das ist der im README beschriebene „88 Grad bei 103 m"-Effekt.

**Konsequenz im Entwurf:** Unterhalb einer Schwelle wird **nicht mehr retrograd** geregelt,
sondern in einem lokalen Horizontsystem (Hoch/East/North). Die Regelrichtung ist dort
per Definition stetig.

### 2.5 Der Schubstoß bekommt keinen Vorhalt für Zündverzug und Lagefehler

Bis der Schub anliegt, vergehen in KSP:

* **Zündverzug**: `ModuleEngines` baut Schub über mehrere Physikschritte auf (`ignitionDelay`,
  Drosselrampe).
* **Lagefehler**: Beträgt die Abweichung zur befohlenen Richtung θ, verliert man während der
  gesamten Brenndauer `g·(1-cos θ)` an Wirkung und `g·sin θ` als Seitwärtsdrift.
* **Treibstoffsetzen**: Ein Booster nach dem Wiedereintritt hat kein beruhigtes Treibmittel;
  KSP modelliert das über den Schubaufbau, nicht über einen expliziten Settling-Schritt.

Bei 200 m/s Restgeschwindigkeit kostet jede Sekunde Verzug 200 m Höhe, die man nicht mehr
zurückbekommt.

**Konsequenz im Entwurf:** Die Vorhersage kennt einen Schubaufbau
(`spool = 0,5 s` bzw. aus der Drosselrampe geschätzt) und eine Zündreserve, die aus dem
**aktuellen** Lagefehler berechnet wird, nicht aus einer festen Zahl.

### 2.6 Die Schubgrenzen der Triebwerke werden nicht als Regelgrenze behandelt

`throttleMin`/`minThrust` werden beim Auswählen der Triebwerke nur als Filter benutzt
(`e.minThrust <= 0 && e.throttleMin <= 0`). Ein Triebwerk mit Mindestdrossel kann also
garnicht ausgewählt werden — obwohl es das einzige an Bord sein kann. Umgekehrt gilt: kann
das Triebwerk bei der Zielmasse nicht unter den Schub für 5 m/s Sinken herunter, dann ist
„5 m/s" physikalisch unerreichbar, und der Booster steigt statt zu sinken.

**Konsequenz im Entwurf:** Der Drosselbereich `[throttleMin, 1]` ist Teil der Regelung, und
die Machbarkeit wird **vor** der Landung geprüft, nicht während.

### 2.7 Die Endphase hat kein Abbruchkriterium

Wenn der Booster nicht mehr aufzuhalten ist (Treibstoff alle, Lage nicht haltbar, Schub zu
klein), läuft der Automat weiter und schreibt Statuszeilen, bis der Aufprall kommt. Es gibt
keinen Zustand, in dem er das erkennt, den Schub abschaltet und das Ergebnis als Absturz
meldet.

**Konsequenz im Entwurf:** Ein eigener Fehlerzustand mit klarer Logzeile und einem letzten
Versuch („weichster möglicher Aufprall"): Neigung aufrecht, Vollschub, Landebeine unten.

---

## 3. Der Entwurf

### 3.1 Ein Zustandsautomat mit einer Phasenachse

```
  0  INACTIVE          vor Abtrennung, oder Automatik aus
  1  ALIGN             ab Abtrennung: retrograde ausrichten, Triebwerke gesperrt
  2  ENTRY             in der Atmosphäre: retrograd halten, Triebwerke gesperrt
  3  PREDICT           (Teil von 2) Vorhersage läuft mit, Zündung, sobald sie nicht mehr aufgeht
  4  BURN              ein Brennstoß, Neigung begrenzt, Sinkrate nach Rampe
  5  TERMINAL          unter der Endphase-Grenze: senkrecht, 5 m/s, Drift → 0
  6  TOUCHDOWN         unter Ausschaltgrenze: Schub aus, Kontakt abwarten
  7  DONE / ABORT      übergeben bzw. Aufprall als Absturz gemeldet
```

`ALTITUDE_BOTTOM` — die Höhe der **Boosterunterkante über Grund** — ist die einzige
Fortschrittsvariable für 4 → 5 → 6. Alle anderen Größen (Geschwindigkeit, Masse, Schub,
Lagefehler) beeinflussen nur die Sollwerte innerhalb einer Phase, nie den Phasenwechsel.
Damit kann kein einzelner verrauschter Messwert die Phase umschalten (die Ursache der
0.8.18-Korrektur „300-m-Endphase wird bei jedem Physikschritt neu bestimmt").

### 3.2 Die eine Regelgleichung

Jeden Physikschritt wird ein **Beschleunigungsvektor im lokalen Horizontsystem** kommandiert:

```
û  = radial nach oben           (Einheitsvektor, geodätisch)
v  = Oberflächengeschwindigkeit (KSP: vessel.srf_velocity)
v_v = v·û                       (positiv = steigend)
v_h = v - v_v·û                 (Seitwärtsanteil)

Höhe über Grund      h   = Clearance der Boosterunterkante
Zielsinkrate         v*  = Profil(h)                       (siehe 3.3)

freies_a = 0.8·(T_max/m) − g    (Verzögerungsbudget über der Schwerkraft)
Resthöhe = h − h_aus           (h_aus = Triebwerksausschalthöhe)

a_vert   = g + min( freies_a , 0.5·(v_sink² − v*²)/Resthöhe )
a_lat    = (v_lat − v_lat_soll)/τ, begrenzt auf freies_a·tan(θ_max)
a_cmd    = a_vert·û + a_lat
```

Und daraus mechanisch Lage und Drossel:

```
Drossel  = |a_cmd| / (T_max/m)        → 0…1
Richtung = a_cmd / |a_cmd|            → Zielachse des Triebwerks
```

Diese eine Umrechnung ersetzt die komplette heutige Zustandslogik. Sie hat vier
Eigenschaften, die die alte Kette nicht hatte:

1. **Weit oben sättigt die Verzögerungsforderung am Schubbudget**, also Vollschub — genau das,
   was ein schneller Eintritt braucht. Nach unten läuft dieselbe Formel von selbst gegen null aus,
   weil die Resthöhe im Nenner steht und die Sinkrate fällt.
2. **Die Neigung ist automatisch selbstbegrenzend.** `a_lat_max = freies_a·tan θ_max` mit
   `θ_max = 20°` oberhalb und gegen 0° in der Endphase. Der Booster kann sich gar nicht
   so weit neigen, dass ihm die Höhenregelung entgleitet.
3. **Kein PID-Regler auf den Schub.** `g` wird als **Vorhalt** direkt mitkommandiert. Deshalb
   funktioniert dieselbe Gleichung bei TWR 1,3 und bei TWR 3,4.
4. **Richtung ist stetig.** `û` und die Horizontalkomponenten sind bei `|v| → 0` definiert,
   weil nie durch `|v_h|` geteilt wird.

**Wichtig für die Umsetzung:** `a_cmd` wird als **Vektor mit Betrag** zurückgegeben, nicht nur als
Richtung. Die Drossel ist `|a_cmd|/(T/m)`, also darf der Adapter die Einheitsrichtung nicht
nochmals mit der Drossel skalieren — sonst wird jeder Querbefehl nur zum Bruchteil ausgeführt.
Genau dieser Fehler steckte im ersten Entwurf und ist der Grund, warum die Seitendrift zunächst
nicht abgebaut wurde.

### 3.2.1 Warum genau diese Form und keine andere

Drei naheliegende Alternativen wurden im Testlauf durchgemessen und verworfen:

| Ansatz | Warum nicht |
|---|---|
| Proportionalterm auf den Geschwindigkeitsfehler mit fester Verstärkung | Entweder oben zu schwach (der Booster fällt durch das Profil) oder unten zu stark (harter Pull-up). Es gibt keine Verstärkung, die beides kann. |
| Nur das Profil als Vorhalt (`a = Profil-Verzögerung`, sobald `v > v*`) | Fällt der Booster einmal über das Profil, bleibt er für den Rest des Abstiegs darüber und kommt zu schnell an. |
| Zeit-bis-Aufprall-Regler (`a = Fehler/Zeit`) | Überschwingt ins Steigen, weil der reale Abstieg immer etwas stärker bremst als der geplante — der Luftwiderstand allein reicht dafür. Danach steigt der Booster hunderte Meter. |

Die gewählte Form ist die einzige, die ohne Verstärkung sowohl sättigt als auch ausläuft.

### 3.3 Die Sinkratenrampe (ersetzt „Suicide Burn vs. Hover")

Vorgabe: In der Endphase 5 m/s, ab Bremsbeginn eine Sinkrate, die die Physik hergibt, und
dazwischen **stetig**.

```
h_aus   = 1,5 m                       (Triebwerksausschaltung)
v_td    = 5 m/s                       (Zielsinkrate, einstellbar)
a_frei  = 0,8·(T_max/m) − g           (Verzögerungsbudget mit 20 % Schubreserve)

v*(h) = sqrt( v_td² + 2·k·a_frei·(h − h_aus) )        k = 0,05 (Standard)
```

**Der Koeffizient `k` ist der entscheidende Punkt.** Wird er auf 1 gesetzt, beschreibt die
Wurzel genau die Kurve, die ein Vollschubbremsstoß fliegt — das klingt richtig, ist aber falsch:
der Regler bremst dann mit derselben Verzögerung, die das Profil vorgibt, und die Sinkrate
bleibt für den ganzen Abstieg über dem Profil hängen, ohne es je zu erreichen. Gemessen:

| k | Ergebnis im Testlauf |
|---|---|
| 0,00 | saubere Landung mit 1,8 m/s, Booster bremst spät |
| 0,05 | Landung mit 1,8 m/s, hält das Profil etwas fester |
| 0,10 | Landung mit 3,9 m/s |
| 0,20 | 24 m/s — der Booster bremst weit oben und verliert die Höhe, die er braucht |

Der Standard steht deshalb auf **0,05**, mit 0,10 als oberer sinnvoller Grenze.

Das ist **kein Hover und kein Hoverslam im Sinne eines umgeschalteten Modus**, sondern eine
einzige Formel:

* Ein Booster mit hohem TWR hat ein großes `a_frei`, das Profil ist steil, er fällt schnell, zündet
  spät und bremst hart. Das ist der Treibstoff-optimale Hoverslam.
* Ein Booster mit TWR 1,2 hat ein kleines `a_frei`, das Profil ist flach, er sinkt gemächlich und
  zündet früh. Das ist der Hover-Anflug.

Beide entstehen aus derselben Zeile. Kein Umschalten, keine Zündhöhe als Einstellwert.

Zusätzlich die **Vorhersage als Zündauslöser** (Abschnitt 3.4): Sie ist der einzige Zündauslöser.
Der Profilvergleich bleibt als Sicherheitsnetz, falls der Booster schneller fällt als die
Vorhersage annahm.

### 3.4 Die Vorhersage: eine Integration statt drei Schätzungen

Einmal pro Sekunde (und im letzten Kilometer zweimal pro Sekunde) wird der restliche
Abstieg ab der aktuellen Höhe **rückwärts** integriert, um die Höhe zu finden, in der der
Brennstoß spätestens beginnen muss.

```
Modell (1D entlang der Flugbahn, Schrittweite 0,05 s, max. 2000 Schritte):
   ρ(h)  = Atmosphärendichte aus KSPs Druckprofil bei der Zielhöhe   (exakt, keine e-Funktion nötig)
   D     = 0,5·ρ·v²·Cd·A / m            (Cd·A aus den echten DragCubes, Geschwindigkeitsrichtung)
   a_thr = T_max(h, ρ)·(1 − Reserve)/m(t)   mit m(t) = m0 − ṁ·t
   a     = a_thr − D/m − g(h)

Abbruchbedingungen der Integration:
   v ≤ v_td  in Höhe h_end   →  „geht auf"       →  Zündung noch nicht nötig
   h ≤ h_end und v > v_td    →  „geht nicht auf" →  Zündung sofort
```

Gesucht wird per Bisektion über 8 Kandidaten die **Zündhöhe**. Ergebnis pro Lauf:

| Ausgabe | Verwendung |
|---|---|
| `igniteAlt` | Schwellwert für Phase 2 → 4 (mit Zündreserve) |
| `burnTime` | Logzeile, Treibstoffbedarf = `ṁ·burnTime` |
| `fuelNeeded` | Machbarkeitsprüfung: reicht der Treibstoff? |
| `touchdownSpeed` | Vorhersage des realen Aufsetzens (Log + Absturzerkennung) |
| `margin` | verbleibende Reserve in m/s — die wichtigste Zahl im Log |

Wichtig: Die Integration rechnet **nicht** mit einem festen Schub, sondern mit
`T_max(h, ρ)` aus KSPs eigener Triebwerkskurve (`MaxThrustOutputAtm` mit dem Druck in der
jeweiligen Höhe) und der Massenabnahme während des Brennstoßes. Damit ist sie auch für ein
Triebwerk mit starkem ISP-Abfall in Bodennähe gültig.

### 3.5 Lage und Drehmoment

Der Beschleunigungsvektor aus 3.2 wird zur Zielachse; die Zuordnung erfolgt
`Achse = −û_thrust`. `BetterController` bleibt darunter erhalten (bewährt, kein
Neuschreiben von Reglermath), bekommt aber zwei Ergänzungen:

1. **Rollreferenz.** `attitudeTo` übergibt derzeit `up = -ReferenceTransform.forward`. Die
   Rollage ist damit an die vorherige Lage gebunden. Für die Endphase wird die Rollreferenz
   auf „Flugrichtung über Grund" gelegt, damit die Neigung tatsächlich in Richtung des
   Driftfehlers zeigt und nicht 90° daneben.
2. **Drehmomentbudget als Regelgrenze.** Aus `Vessel.MOI` und `TorqueAvailable` wird die
   erreichbare Winkelbeschleunigung bestimmt und in die Sollneigung eingespeist:
   `θ_zulässig = θ_max·min(1, ω_zulässig/ω_nötig)`. Ist der Booster kaum drehfähig, plant der
   Regler von Anfang an flacher — statt am Ende eine Neigung zu verlangen, die nicht mehr
   erreichbar ist.

**Machbarkeitsprüfung vor dem Wiedereintritt** (einmal, direkt nach dem Ausrichten):

```
benötigt:  T_max(Boden)/m_trocken  ≥ 1,15      (Schubreserve für die Endphase)
           Drehmoment für 45° Nickschwenk in ≤ 20 s     (Lagekontrolle vorhanden)
           Treibstoff ≥ 1,25 · fuelNeeded (aus Vorhersage mit aktueller Bahn)
           Treibwerk: axial, drosselbar, wiederzündbar, throttleMin < benötigte Enddrossel
```

Fällt eine Prüfung durch, wird die Triebwerkslandung für diesen Booster nicht versucht,
sondern die Fallschirmlandung fortgesetzt — mit **einer** Logzeile, die den Grund nennt.
Heute erfährt man den Grund erst aus dem Aufprall.

### 3.6 Bodenerkennung

Die vorhandene `GroundSurface`/`Bottom`-Logik ist die richtige Grundlage und bleibt: Unterkante
aus echten Teilkollidern, Geländehöhe als Referenz, Welt-Collider nur bei Plausibilität
(≤ 250 m Abweichung), Layer 10 ausgeschlossen. Drei Ergänzungen:

1. **Bodenfläche statt Bodenpunkt — in Flugrichtung.** Die Höhe wird dort abgetastet, wo der
   Booster tatsächlich aufsetzen wird (Ballistikschätzung ~2 s voraus, nicht senkrecht unter
   ihm). Beim Abstieg über eine Steigung gewinnt man damit die entscheidenden Sekunden.
2. **Untergrenze ist eine Wand, kein Ziel.** Ein Booster, der ohne Boden-Collider auf
   Geländehöhe sinkt, darf nicht darunter kommandiert werden. Die Regelung klemmt `h ≥ 0`
   und die Sinkrate auf 0; der Kontakt wird wie bisher synthetisch gemeldet
   (`KONTAKT=hoehe`) und über `LandingSystems.HandOverLanded` an KSP übergeben.
3. **Geländeneigung.** Aus drei Abtastungen (Mitte, ±30 m in Fallrichtung) wird die Neigung
   geschätzt und als Zuschlag zur Ausschaltgrenze addiert: `h_cut = 1,5 m + 0,5·tan(Neigung)·Rumpfradius`.
   Ohne diesen Zuschlag setzt ein Booster auf 15° Hang mit dem Rand zuerst auf.

### 3.7 Hitzeschutz

`HeatGuard` (maxTemp/skinMaxTemp auf 100000 K) tut genau, was gefordert ist, und bleibt.
Ergänzt wird eine **Messtufe** ohne Regelwirkung: pro Sekunde die höchste
Bauteilhauttemperatur, das heißeste Bauteil, die dynamische Druckbelastung `q` und die
Integralhitze. Damit ist nach einem Flug mit einer Zeile beantwortbar, ob `heatImmune`
überhaupt nötig ist und welcher Teil der Grenzfall wäre.

Eine aktive Wärmeregelung (Anstellwinkel zur Spitzenreduktion) ist bewusst **nicht** Teil
dieses Entwurfs: Sie erfordert aerodynamische Stabilität, die ein Booster rückwärts nicht
besitzt.

### 3.8 Treibstoff- und Massenrechnung

```
m(t)   = GetTotalMass() − ṁ·t          (aktuell, kein Planungswert über den ganzen Flug)
T_max  = Σ MaxThrustOutputAtm(atm = staticPressure/101.325, T_external, atmDensity)
         nur Triebwerke, deren Schubachse innerhalb 25° der Zielachse liegt
a_avail= T_max/m
a_disp = 0,80·a_avail − g              (Verzögerungsreserve der Rampe)
```

`0,80` ist die Schubreserve: 20 % bleiben für Lageregelung, Zündverzug, Widerstandsfehler
und für den Fall, dass die Vorhersage 1 s daneben liegt. Diese eine Zahl ersetzt die
heutigen Grenzen `BrakingEnvelope`/`DragFreeAllowedSpeed`.

---

## 4. Was der Booster zwingend mitbringen muss

Keine Regelung ersetzt fehlende Hardware. Die Prüfung aus 3.5 läuft **vor** dem Versuch, und
die Landung wird nicht begonnen, wenn eines davon fehlt:

| Bedingung | Warum | Fehlerbild heute |
|---|---|---|
| Axiales Raketentriebwerk, wiederzündbar, drosselbar | Schub muss betrags- und richtungsregelbar sein | „kein nutzbarer Treibstoff/Schub" nach dem Aufprall |
| TWR ≥ 1,15 bei Landemasse | unter 1,0 ist kein Abfangen möglich | Durchschlagen mit Vollschub |
| Lagekontrolle: RCS, Reaktionsräder, Gimbal oder Flügel | retrograde Ausrichtung über 100 s | Taumeln, `err` bleibt bei 100° |
| Treibstoff für `1,25·fuelNeeded` | Reserve für Fehlerkorrektur | Schub bricht in der Endphase ab |
| `heatImmune` oder echte Hitzeschutzkacheln | retrograde Lage = kleinste Querschnittsfläche = maximale Aufheizung | Bauteilzerlegung in `Part._CheckPartTemp` |

---

## 5. Abgrenzung: was aus der heutigen Version bleibt

| Baustein | Entscheidung |
|---|---|
| `GroundSurface`, `Bottom`, `TouchdownPolicy`, `TerrainProbe` | **bleibt** — die Bodenerkennung ist die gründlichste Fehlerquelle, die bereits gelöst ist |
| `HeatGuard` | **bleibt** unverändert |
| `LandingSystems`, `ParachuteDeployment`, `ParachuteGuard` | **bleibt** — Fallschirmlandung ist nicht Gegenstand dieser Änderung |
| `RecoveryPolicy`, `RecoveryJournal`, `VesselRangeTransition`, `RailWarpGuard` | **bleibt** |
| `BetterController`, `MJPIDController`, `MJPortMath` | **bleibt** als Lageregler unter der neuen Zielachse |
| `LandingController` (MJ-Zustandskette) | **wird ersetzt**, bleibt aber als Rückfallebene im Code verfügbar |
| `BrakingEnvelope`, `MJUntargetedDeorbit`, `MJCoastToDeceleration`, `MJDecelerationBurn`, `MJFinalDescent`, `MJGravityTurn`, `MJKillHorizontalVelocity` | **werden von der neuen Führung nicht mehr aufgerufen** (kein Löschen — GPL-Herkunft dokumentiert) |

---

## 6. Umsetzungsplan

Jeder Block ist für sich prüfbar; nach jedem Block ist die DLL flugfähig.

**Stand: Blöcke A, B, D und E sind gebaut, die DLL ist kompiliert und im Testlauf grün.** Der
Flugtest in KSP steht aus. Die Konstanten sind gegen ein Punktmassenmodell eingemessen, nicht
gegen KSPs echte Atmosphäre und Triebwerksrampen.

### Block A — Reine Führung, ohne KSP ✔
`src/DescentTypes.cs`, `src/GuidanceConfig.cs`, `src/DescentPrediction.cs`,
`src/DescentGuidance.cs`. Reine Mathematik, keine Unity-Abhängigkeit, im bestehenden Testlauf
prüfbar (`tests/GuidanceTests.cs`, in `test.ps1` eingebunden).

Getestet und bestanden: Sinkratenprofil und Neigungsgrenze, senkrechter Anflug (1,8 m/s),
Hoverslam (1,8 m/s), starker Schub (1,8 m/s), Seitendrift (0,9 m/s Restdrift), Höhenrauschen
±3 m (2,6 m/s), Stetigkeit, nicht landbarer Booster (wird als Absturz gemeldet), Zündentscheidung
der Vorhersage.

### Block B — KSP-Anbindung ✔
`src/DescentAdapter.cs` sammelt Höhe, lokales Horizontsystem, Geschwindigkeit, Triebwerksschub,
Luftdichteprofil aus KSPs eigenem Druckverlauf, Drag-Cubes und Treibstoffvorrat. `PoweredLanding`
ruft das Gesetz jeden Physikschritt auf und schreibt Drossel und Zielachse zurück.
Schalter `guidanceMode = predictive | legacy` in `settings.cfg`.

### Block C — Bodenerkennung ✔
`src/GroundScan.cs` tastet die Geländehöhe entlang der Flugrichtung ab (zwei Sekunden Vorhalt,
höchstens 400 m, acht Stichproben) und meldet den **niedrigsten** Boden auf diesem Weg, nicht den
Mittelwert. Der kleinere der beiden Werte — senkrecht oder voraus — geht in den Abstieg ein.
Dazu die Neigung unter dem voraussichtlichen Aufsetzpunkt; die Ausschalthöhe der Triebwerke wird
um `Rumpfradius · (tan Neigung + tan Restlage)` angehoben, damit auf einem Hang nicht die
talseitige Kante zuerst aufsetzt.

Die Abtastung nutzt die prozedurale Geländehöhe, weil KSP Boden-Collider nur rund um das aktive
Fahrzeug baut — dieselbe Quelle, die schon die senkrechte Ersatzmessung verwendet. Die
Plausibilitätsregeln aus `GroundSurface` (Layer 10 ausgeschlossen, höchstens 250 m Abweichung)
bleiben für die senkrechte Messung unverändert zuständig.

Getestet: flaches Gelände, ansteigendes Gelände (senkt den gemeldeten Abstand, Neigung wird
erkannt), abfallendes Gelände (macht die Messung **nicht** optimistisch), senkrechter Abstieg
(eine Klippe 50 m voraus beeinflusst ihn nicht) und der Zuschlag zur Ausschalthöhe.

### Block D — Diagnose ✔
Neue Werte im Log und in der Statuszeile. Eigene `Landing predict`-Zeile mit allen Werten in einer
Reihe: Phase, Höhe, Neigung, Vorausmessung, Sinken, Seitwärts, Zielsinkrate, freies
Beschleunigungsbudget, Befehl, verfügbare Beschleunigung, Ausschalthöhe, Neigung und Lagefehler,
Drehmoment, Masse, vorhergesagte Aufsetzgeschwindigkeit, Reserve, Zündentscheidung, Brenndauer,
benötigtes und verfügbares Δv. Dazu `NICHT-ABFANGBAR` und `ABBRUCH` als Klartext.

### Block E — Fenster ✔
Die Statuszeile nennt Phase, Zielsinkrate, voraussichtliche Aufsetzgeschwindigkeit, Reserve und
Schub sowie den Absturzzustand. Die Titelzeile zeigt die Mod-Version.

### Erster Flugtest (bewusst klein)

1. Booster mit **TWR ≈ 2,5** und RCS, Abtrennung bei 80 km statt 500 km. Das Gesetz und die
   Logzeilen werden geprüft, ohne dass der Wiedereintritt dazwischenfunkt.
2. Auswertung: Zielsinkrate gegen tatsächliche Sinkrate, tatsächliche Neigung gegen die
   Neigungsgrenze, Aufsetzgeschwindigkeit gegen `touchdownSpeed`.
3. Erst danach die volle Abtrennung bei 500 km mit Eintrittsphase.
4. Fällt die Landung zu hart aus, zuerst `touchdownSpeed` senken, dann `terminalAltitude`
   erhöhen. Fällt sie zu weich aus (Booster schwebt und verbraucht Treibstoff), umgekehrt.
5. Geht etwas schief: `guidanceMode = legacy` setzt die alte Kette wieder ein, ohne Neubau.

## 7. Offene Fragen an den Flugtest

* Wie stark weicht die Vorhersage ab, wenn die KSP-Luftdichte in Bodennähe nicht der
  Skalenhöhe entspricht? (`ρ` wird aus KSPs Profil gelesen, nicht gerechnet.)
* Reicht das Drehmoment eines typischen Boosters für retrograde Ausrichtung im dichten
  Luftstrom? Die 20-%-Schubreserve ist die Antwort darauf; ob sie reicht, zeigt `err`.
* Trifft die Zündentscheidung der Vorhersage in KSPs echter Atmosphäre? Im Testmodell ist die
  Atmosphäre zwischen 10 und 25 km zu dünn, um das zu beantworten.
* Ist eine Anstellwinkelsteuerung im oberen Wiedereintritt nötig, um die Bodenspur zu
  treffen? Aktuell wird keine Landeposition angestrebt — nur eine sichere Landung dort, wo
  der Booster ankommt.

---

## 8. Nachtrag: die Eintrittsphase

Beim Durchmessen des echten Missionsprofils (Abtrennung hoch oben, ~2200 m/s quer, Abstieg
durch die Atmosphäre) hat sich gezeigt, dass das Gesetz ohne eigene Eintrittsphase am falschen
Ende bremst. Zwei Fehlgriffe, beide im Testlauf gemessen:

1. **Der Querregler bremst die Bahngeschwindigkeit mit dem Triebwerk.** Er sieht 2200 m/s
   Drift und arbeitet dagegen — oben in dünner Luft, wo der Luftwiderstand das umsonst
   erledigt hätte. Ergebnis: 15 t Treibstoff verbraucht, bevor die Landung überhaupt beginnt.
2. **Die Sinkregelung hält den Abstieg auf Profilhöhe**, statt den Booster durch die
   Atmosphäre fallen zu lassen. Er zündet bei 39 km, schwebt 140 s mit ~9 m/s² und kommt mit
   **492 m/s** an.

### Die Regel

```
Budget = freies_a · (Resthöhe / max(CoastTimeReferenceSpeed, Sinkrate)) · CoastMargin
Coast, solange  Gesamtgeschwindigkeit <= Budget
```

`CoastMargin` (Standard 0,5) ist bewusst großzügig: Wer zu früh bremst, verbraucht den
Treibstoff, der die Landung trägt; wer zu spät bremst, verbraucht den Booster.

Während des Coasts gilt:

* **Triebwerke aus.** Der Luftwiderstand ist die Bremse.
* **Lage retrograd**, komponentenweise aus dem Geschwindigkeitsvektor. Ein Booster, der quer
  steht, zeigt dem Luftstrom die breite Seite — und ein früherer Fehler ließ ihn hier
  *entlang* seiner eigenen Geschwindigkeit zeigen, statt gegen sie.
* **Querregler gesperrt.** Seitwärtsfahrt ist vor dem Bremsbrand kein Fehler, sondern das,
  was der Widerstand abbaut. Sie mit dem Triebwerk zu bekämpfen kostet genau den Treibstoff,
  der die Landung trägt.
* **Die Vorhersage darf die Zündung nicht anordnen.** Ihre Aussage „ohne Triebwerke nicht
  landbar" ist immer wahr und beantwortet nicht die Frage *wann*.

`CoastMargin = 0` schaltet die Phase ab und stellt das Verhalten der vertikalen Landungen
unverändert wieder her.

### Was weiterhin fehlt

Die Eintrittsphase ist der Anfang, nicht die Lösung. Offen bleibt eine **Zündentscheidung auf
Höhe *und* Gesamtgeschwindigkeit** und eine Landeprofilplanung, die die horizontale
Geschwindigkeit einbezieht — das heutige Profil ist eine Funktion der Höhe allein. Solange
kommt ein Booster mit 2200 m/s quer im Modell noch zu hart an, und für diesen Flug bleibt
`guidanceMode = legacy` die sichere Wahl.

Der Grund dafür ist auch der Grund, warum hier Schluss ist: Die eigentliche Arbeit leistet der
Überschallwiderstand in einer dichten Atmosphäre, und den liefert nur KSP mit seinen echten
Drag-Cubes. Ein Punktmassenmodell mit einer handgebauten Dichteformel kann diese Phase nicht
ausmessen, und eine daran eingemessene Konstante wäre schlechter als keine.

## 9. Was der Testlauf an echten Fehlern gefunden hat

Zur Einordnung, wie viel der Testlauf wert war: Er hat vier Fehler gefunden, die im Flug
nicht als Rechenfehler, sondern als schleichendes Fehlverhalten aufgetreten wären.

| Fehler | Wirkung im Flug |
|---|---|
| `a_cmd` wurde als Einheitsvektor zurückgegeben und vom Adapter mit der Drossel skaliert | Jeder Querbefehl wurde nur zum Bruchteil ausgeführt; Drift wuchs statt zu fallen (30 → 88 m/s) |
| Vorzeichen der Querrichtung in `AimBurn` | Der Querregler beschleunigte die Drift, statt sie abzubauen |
| `GroundScan` lieferte eine absolute Höhe statt eines Abstands | Flacher Boden meldete −1002 m statt +998 m |
| Profilkoeffizient nicht auf 1 begrenzt | Oberhalb 1 kann das Profil eine Sinkrate verlangen, die steiler ist als der freie Fall — der Booster steigt dann, statt zu landen |
| Coast-Lage aus der Quer-**Geschwindigkeit** statt aus dem Queranteil des Geschwindigkeits**vektors** | Ein flach eintretender Booster zielte entlang seiner eigenen Geschwindigkeit statt gegen sie — breite Seite in den Luftstrom, genau das Gegenteil von dem, wofür die Ausrichtung da ist |
| „Steigt" schon bei `sink < 0` | Bei einem flachen Eintritt (wenige m/s Sinken, hunderte m/s quer) galt der Booster als steigend und wurde aufrecht gestellt |

Dazu die Erkenntnis zur Eintrittsphase in Abschnitt 8, die ebenfalls aus dem Testlauf kommt
und sich nur dort zeigen ließ. Nicht jeder dieser Fehler wäre im Flug als Absturz sichtbar
geworden; mehrere hätten sich als „der Automat macht irgendwas" gezeigt.

## 10. Der schwerste Fehler: die Vorhersage rechnete verkehrt herum (0.9.4)

`DescentPrediction.Traverse` integriert den Rest des Abstiegs mit gezündeten Triebwerken. In
dieser Integration standen **Schwerkraft und Schub mit dem falschen Vorzeichen**:

```csharp
// bis 0.9.3 – falsch
double aSink = thrust - drag * (sink / speed) - gravity;
```

`sink` ist positiv **nach unten**. Schwerkraft muss die Sinkrate also *vergrößern*, der Schub
sie *verkleinern*. Die Zeile tat beides umgekehrt: Sie ließ den Schub den Sturz
beschleunigen. Damit war jede Vorhersage falsch, und zwar immer in dieselbe Richtung —
pessimistisch:

| Fall (Testlauf) | Vorhersage 0.9.3 | Vorhersage 0.9.4 |
|---|---|---|
| 500 m Fall, 0,5 m/s² Schub | Aufsetzen mit 3727 m/s | 96,5 m/s (= freier Fall, korrekt) |
| 500 m Fall, 25 m/s² Schub | Aufsetzen mit 122 m/s | 5,0 m/s |
| 4000 m, 200 m/s Sink, 25 m/s² | „nicht abfangbar" | 4,5 m/s |

**Warum das kein Test bemerkt hat.** Geprüft war nur die *Entscheidung* („will sie zünden?"),
nie die Rechnung. Eine Vorhersage, die immer „nicht abfangbar" ruft, besteht jede Prüfung der
Form „zu schnell → zünden". Der Fehler war damit unsichtbar, obwohl er in jeder einzelnen
Logzeile stand.

**Was er im Flug bewirkt hat.** `Unstoppable` war praktisch immer wahr, und `Unstoppable` ist
genau das, was die Eintrittsphase wieder abschaltet:

```csharp
if (coasting && prediction.Unstoppable) coasting = false;
```

Der Coast wurde also in jedem Fall verworfen, in dem er gegriffen hätte — die Booster zündeten
oben und verbrannten den Treibstoff, der die Landung trägt. Gemessen: derselbe Testfall
(8000 m, 100 m/s, Triebwerk mit 25 m/s²) ergibt mit der alten Vorhersage `Coasting = False`,
mit der neuen `True`.

**Warum die vertikalen Landungen trotzdem stimmten.** Bei ihnen entscheidet nicht die
Vorhersage, sondern das Sicherheitsnetz (`sinkAtIgnition > zielSink − IgnitionSpeedMargin`) in
Verbindung mit dem Coast-Budget. Verglichen mit der ausgelieferten 0.9.3 liefern alle
geschlossenen Testflüge **dieselben Zahlen** (Aufsetzen 1,77 m/s, Masse 27039 kg usw.), und
auch die Zündhöhen stimmen überein. Der Fehler saß nicht in der Landung, sondern in der
Eintrittsphase und in allen Diagnosewerten (`tdPred`, `reserve`), die aus der Vorhersage
stammen und im Log standen.

### Was jetzt zusätzlich geprüft wird

`TestForecastPhysics` prüft die Integration selbst, nicht ihr Urteil:

* ohne nennenswerten Schub kommt der freie Fall heraus (96,4 m/s gegen 96,5 m/s gerechnet),
* mit 25 m/s² aus 500 m kommt sie sanft an (5,0 m/s),
* 200 m/s aus 4000 m sind zu schaffen, aus 300 m nicht (`IgnitionNeeded`),

und die Integration fliegt den Brand jetzt so, wie das Gesetz ihn fliegt: **retrograd**, also
gegen die geflogene Geschwindigkeit statt nur gegen die Sinkrate, und mit **Drosselrücknahme**
auf die Zielsinkrate — vorher lief die Integration mit Vollschub bis zum Boden, was einen
Booster mit TWR > 1 nach oben fliegen lässt und die Steiggeschwindigkeit als „Aufsetzen"
meldet.

### Was auch das nicht löst

Der Wiedereintritt mit ~2200 m/s quer bleibt in diesem Modell nicht landbar: Die
Punktmassen-Simulation hält eine einzelne Exponentialatmosphäre mit 5,6 km Skalenhöhe, und
Kerbin ist oberhalb von 15 km mehrere Male dichter als das; KSPs Drag-Cubes kommen mit
Mach-Abhängigkeit dazu. Beides wirkt in Richtung *mehr* Bremsung. Solange gibt die Vorhersage
hier „nicht abfangbar" und verwirft den Coast — mit den Zahlen *dieses* Modells zu Recht.
Entschieden wird das im Flug, nicht hier.

