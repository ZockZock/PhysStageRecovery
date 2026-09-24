# Entwickeln mit git

Der Quelltext liegt seit 24.09.2026 in einem git-Repository. Die eingefrorenen Stände sind
Tags, die Ordner unter `build/backups/` bleiben als physische Kopie daneben bestehen.

## Was versioniert wird und was nicht

Versioniert sind `src/`, `tests/`, `tools/`, `packaging/`, `docs/` und die Skripte
`build.ps1`, `test.ps1`, `install.ps1` samt `BoosterWatch.csproj`, `README.md`,
`THIRD_PARTY.md` und `LICENSE` — 75 Dateien.

Nicht versioniert (siehe `.gitignore`): `build/` (Prüfstands-Binaries, Schnappschüsse),
`dist/` (Bauausgabe), die ausgelieferten `*.zip`-Archive und die `SHA256SUMS.txt` der
Schnappschüsse. Das installierte Binary in KSP ist ohnehin kein Quelltext.

## Die Baselines

| Tag | Stand | Beleg |
|---|---|---|
| `v0.9.17` | Anflug mit Sinkratenleiter, seitliche Auffanggrenze 0,5 m/s | `docs/BASELINE-0.9.17.md` |
| `v0.9.20` | Volllastbremsung bis zur Auffanghöhe, schubachsenbewusste Vorhersage, späte Zündung | `docs/BASELINE-0.9.20.md` |
| `v0.9.31` | Lande-Vorhalt am Triebwerk (Bauteilmodul, MechJeb trennt darauf), Anzeige schraffiert in der Stock-FT-Box | `docs/BASELINE-0.9.31.md` |

```powershell
git log --oneline --decorate          # Historie mit Tags
git show v0.9.20 --stat               # was ein Stand enthält
git diff v0.9.17 v0.9.20 -- src/      # was sich zwischen zwei Baselines geändert hat
```

Einen Stand ansehen, ohne den Arbeitsordner anzufassen:

```powershell
git worktree add ..\BoosterWatch-v0.9.20 v0.9.20   # zweiter Ordner mit diesem Stand
```

## Der übliche Ablauf

```powershell
.\test.ps1        # muss durchlaufen (ein bekannter Rauschtest ist offen, siehe README)
.\build.ps1
.\install.ps1     # KSP muss geschlossen sein

git status        # was ist geändert?
git diff          # was genau?
git add -A
git commit -m "0.9.21: <was geändert wurde und warum>"
```

Ist ein Stand im Spiel bestätigt, wird er als Baseline markiert:

```powershell
git tag -a v0.9.21 -m "Baseline 0.9.21 - <wofür sie steht>"
```

Und die zugehörige Beschreibung als `docs/BASELINE-0.9.21.md` dazu — mit den Flugdaten,
die den Stand belegen. Dieser Text ist später mehr wert als der Code selbst: Er sagt, womit
eine Zahl gemessen wurde.

## Wenn etwas schiefgeht

```powershell
git restore src/DescentGuidance.cs        # eine Datei auf den letzten Commit zurück
git restore .                             # alles nicht Committete verwerfen
git checkout v0.9.20 -- src/              # den Quelltext einer Baseline zurückholen
git revert <commit>                        # einen Commit rückgängig machen, ohne Historie zu verlieren
```

Der gesamte Stand einer Baseline liegt zusätzlich als Ordner unter
`build/backups/baseline-<version>-<zeitstempel>/` mit `SHA256SUMS.txt`. Prüfen, ob so ein
Ordner noch unverändert ist:

```powershell
Get-Content SHA256SUMS.txt | ForEach-Object {
    $hash, $file = $_ -split '  ', 2
    if ($hash -ne (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) { "geaendert: $file" }
}
```

## Einstellungen des Repositorys

`user.name` und `user.email` sind nur lokal gesetzt (`ermel`, `ermel@localhost`) — das
Repository hat keine Verbindung zu einem Server. `core.autocrlf` steht auf `false`, damit git
die Zeilenenden der vorhandenen Dateien nicht anfasst.
