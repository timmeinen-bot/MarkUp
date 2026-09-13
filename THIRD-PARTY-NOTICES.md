# Fremdlizenzen — MarkUp

Copyright (C) 2026 Eselchen Labs

## Emoji-Grafiken — Twemoji (CC BY 4.0)

Die Stempel unter `stamps/emoji/` (23 PNG-Dateien, 72 × 72) stammen aus
**Twemoji** und sind unter **CC BY 4.0** lizenziert.

**Herkunft belegt** (nicht geraten): Am 06.09.2026 wurden die Dateien per
SHA-256 gegen die offizielle Twemoji-Ausgabe abgeglichen. Mehrere Dateien sind
**bit-identisch** mit Twemoji 14.0.2 (`assets/72x72/`), u. a.:

| Datei | Unicode | Ergebnis |
|---|---|---|
| `fire.png` | U+1F525 | identisch mit twemoji 14.0.2 |
| `rocket.png` | U+1F680 | identisch mit twemoji 14.0.2 |
| `unicorn.png` | U+1F984 | identisch mit twemoji 14.0.2 |
| `poop.png` | U+1F4A9 | identisch mit twemoji 14.0.2 |
| `heart.png` | U+2764 | identisch mit twemoji 14.0.2 |

Die Dateien tragen keine Metadaten mehr; sie wurden beim Übernehmen von den
Unicode-Codepunkt-Namen auf sprechende Namen umbenannt (`donkey.png` statt
`1f434.png`). Umbenennen ist keine Bearbeitung des Werks — der Bildinhalt ist
unverändert (bit-identisch), es ist also nur die Namensnennung erforderlich.

**Twemoji** ist CC BY 4.0. Für die Auslieferung genügt die Namensnennung
(Name, Urheber, Lizenz, Verweis auf den Lizenztext). Es besteht **keine**
ShareAlike-Pflicht (anders als bei OpenMoji), und MarkUp selbst wird durch die
Verwendung nicht CC-lizenziert.

Mitgelieferter und in der Oberfläche erreichbarer Hinweis:

    Emoji-Grafiken: Twemoji
    Copyright 2020 Twitter, Inc und weitere Mitwirkende
    (fortgeführt von der Twemoji-Community, github.com/jdecked/twemoji)
    Lizenz: CC BY 4.0 — https://creativecommons.org/licenses/by/4.0/

Nicht verwendet werden die Emoji der Windows-Schriftart Segoe UI Emoji: sie
gehören Microsoft und dürfen nicht als Bilddateien weitergegeben werden.

## Firmenlogos — NICHT in der Veröffentlichung

`stamps/peakon_logo.png` und `stamps/workday_logo.png` sind fremde Firmenmarken
(Peakon/Workday). Sie werden **nicht** ausgeliefert: der öffentliche Build
(`-p:PublicBuild=true`, Konstante `PUBLIC_BUILD`) entfernt sie aus `Program.cs`,
und der Installer nimmt nur `stamps/emoji/` auf, nie `stamps/*.png`. Sie bleiben
allein der privaten Fassung vorbehalten.

## Laufzeitumgebung — .NET 8 (MIT)

MarkUp ist **self-contained** gegen **.NET 8 (Windows Desktop)** gebaut: die
Laufzeitumgebung (MIT-Lizenz) ist in die EXE eingebettet, es muss nichts
gesondert installiert werden. Weitere Fremdbestandteile gibt es nicht —
`MarkUp.csproj` führt kein einziges `PackageReference`.
