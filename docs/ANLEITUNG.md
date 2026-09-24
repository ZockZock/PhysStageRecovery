# Anleitungstext im Hauptfenster

**Eingebaut in 0.9.34** — der Text steht im Fenster, solange **kein Booster erfasst** ist, an der
Stelle der früheren zwei Zeilen. Ausgewählt sind die Punkte 1, 2, 4, 5, 6, 8, 9 und 10; jeder auf das
Wesentliche gekürzt und **ohne jeden Bezug auf MechJeb**.

Nicht enthalten (bewusst): die Flugübersicht (die Zahlen stehen ohnehin darunter), der Lande-Vorhalt,
die Diagnosewege und die Einstellungsdatei.

**Im Fenster** (`FlightWindow.DrawGuide`): Überschrift, darunter die eine Zeile, die den Zustand
erklärt (Fehler, „Nicht erfasst: …", oder die Rakete, deren Abtrennungen beobachtet werden), darunter
die acht Absätze in einem **Scrollbereich** — mit dem Mausrad zu scrollen, wie im Einstellungen-Reiter.
Die zwei Werte aus den Einstellungen werden beim Anzeigen eingesetzt (`<<1>>`), der Text kann ihnen
also nicht widersprechen.

---

## Überschrift (bleibt wie sie ist)

`Bereit für die nächste Stufe`

## Die acht Absätze

### 1 · Was der Mod macht

> Abgetrennte, unbemannte Stufen landen von selbst: Fallschirme, Landetriebwerk, Landebeine,
> Aufsetzen, Bergung. Deine Rakete fliegt weiter wie immer.

### 2 · Welche Stufen übernommen werden

> Jede abgetrennte, unbemannte Stufe in Reichweite, die Fallschirme oder ein Landetriebwerk mit
> Treibstoff hat — höchstens <<1>> gleichzeitig. Sonst nennt das Fenster den Grund.

### 4 · Fallschirme

> Schirme werden im Sinkflug scharfgeschaltet, ihre Öffnungshöhe stellst du unter Einstellungen ein
> (<<1>> über Grund). Wann sie aufgehen, entscheidet KSP.

### 5 · Triebwerkslandung

> Der Landeautomat richtet den Booster rückwärts aus, bremst ihn mit dem Triebwerk und sinkt die
> letzten Meter sanft ab. Er braucht ein Kontrollmodul am Booster (z. B. eine Sonde), wiederzündbare
> Triebwerke und Lageregelung; Bremsklappen bleiben eingefahren.

### 6 · Landebeine und Bremsen

> Unter 1000 m über Grund fahren im Sinkflug die Landebeine aus, unter 10 m die Radbremsen.

### 8 · Autostaging

> Autostaging trennt im Sinkflug ab der eingestellten Höhe bis zur eingestellten letzten Stufe —
> höchstens eine Stufe pro Sekunde. Deine Rakete wird dabei nie gestagt.

### 9 · Bergung

> Geborgen wird nur bei Bodenkontakt, ohne Wartezeit; ein harter Aufprall zählt nicht als Landung.
> Ohne Auto-Bergung bleibt die Stufe stehen und lässt sich später von Hand bergen.

### 10 · Während du fliegst

> Physikwarp bleibt frei, der normale Zeitraffer wartet auf die Booster. Der Hitzeschutz
> (Standard: an) bewahrt verfolgte Booster beim Wiedereintritt vor dem Zerbrechen — deine Rakete
> bleibt unverändert.

---

## Das Kontrollmodul ist keine Empfehlung, sondern eine Bedingung

In der installierten 1.12.5 nachgesehen — ohne Kontrollmodul kommt der befohlene Schub **gar nicht
beim Triebwerk an**:

* `Vessel.CheckControllable()` (gerufen aus `Vessel.LateUpdate`) setzt
  `isControllable = GetControlLevel() > 0`. `GetControlLevel()` liefert ohne Besatzung und ohne
  Steuermodul 0 — es zählt die Steuermodule der Bauteile und die CommNet-Verbindung.
* `ModuleEngines.UpdateThrottle()` fragt über `Part.get_isControllable()` genau diesen Wert ab und
  **springt vor dem Setzen von `requestedThrottle` heraus**, wenn er falsch ist.

Der Autopilot kann also rechnen, was er will: Das Triebwerk bleibt dunkel. Im Flugschreiber sieht man
das als hohen Wert in `drossel` bei `engThr = 0` und `istAcc = 0` — im Fenster steht aber nichts davon.
Deshalb gehört die Bedingung in den Text **und** in die Prüfung:

* **Text** (erledigt, 0.9.33): Absatz 5 nennt das Kontrollmodul.
* **Prüfung im Mod** (erledigt, 0.9.33): Beim Erfassen einer Stufe wird mitgeprüft, ob sie ein
  Steuermodul hat (`ModuleCommand`). Ist keines da und hat die Stufe auch keine Fallschirme, wird sie
  mit dem Grund **„kein Kontrollmodul am Booster"** übersprungen statt in den Absturz geschickt; hat
  sie Fallschirme, wird sie weiter verfolgt, der Landeautomat bleibt aus und die Zustandszeile sagt
  **„Booster nicht steuerbar – keine Triebwerkslandung"** (`tests/TrackingAcceptanceTests.cs`).

## Werte, die aus den Einstellungen kommen

Zwei Zahlen dürfen **nicht** fest im Text stehen, sonst widerspricht er dem, was eingestellt ist:

| Platzhalter | Woher | Standard |
|---|---|---|
| `<<1>>` in Absatz 2 | `maxBoosters` | 8 |
| `<<1>>` in Absatz 4 | Schirmhöhe (`chuteHeight`) | 1000 m |

Die 1000 m für die Landebeine und die 10 m für die Bremsen sind dagegen fest im Mod verankert
(`TrackedBooster`) und bleiben als Zahl im Text.

## Platz im Fenster

Acht Absätze mit Zwischenüberschrift sind zusammen rund 24 Zeilen. In der leeren Fläche stehen bei der
**kleinsten** Fenstergröße (480 × 540 px) nur etwa sieben Zeilen zur Verfügung, bei einem großen
Fenster (zum Beispiel 1265 × 1205 px) dagegen reichlich Platz. Der Bereich braucht deshalb einen
**Scrollbereich** wie der Einstellungen-Reiter (`GUI.BeginScrollView`), sonst wird der Text bei
kleinen Fenstern abgeschnitten. Das ist beim Einbau zu machen — der Text selbst ändert sich dadurch
nicht.

## Vorschlag für die Tags beim Einbau

Je Absatz eine Überschrift und ein Text, also 16 Tags: `#PSR_Help_IntroTitle` / `#PSR_Help_Intro`,
`#PSR_Help_TrackedTitle` / `#PSR_Help_Tracked`, `#PSR_Help_ChutesTitle` / `#PSR_Help_Chutes`,
`#PSR_Help_PoweredTitle` / `#PSR_Help_Powered`, `#PSR_Help_GearTitle` / `#PSR_Help_Gear`,
`#PSR_Help_AutoStageTitle` / `#PSR_Help_AutoStage`, `#PSR_Help_RecoveryTitle` / `#PSR_Help_Recovery`,
`#PSR_Help_WarpTitle` / `#PSR_Help_Warp`.

Die englische Fassung kommt beim Einbau dazu; `tests/LocalizationTests.cs` verlangt für jeden Tag
beide Sprachen mit denselben `<<n>>`-Platzhaltern.

## Folgen der Auswahl

* Der **Lande-Vorhalt** kommt im Fenster nicht mehr vor. Damit entfällt auch der frühere Hinweis, dass
  ein Schiff ohne Abtrennung den Knopf „Vorhalt freigeben" braucht — wer die Anleitung liest, erfährt
  vom Vorhalt nur am Triebwerk selbst. Wenn das nicht gewollt ist, wäre Absatz 7 der ersten Fassung
  in zwei Zeilen die Ergänzung.
* **MechJeb** wird nirgends genannt. Die Autostaging-Zeile sagt deshalb nur, dass die Rakete nie
  gestagt wird.
