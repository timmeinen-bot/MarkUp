# MarkUp

Ein transparentes Annotationswerkzeug für den Windows-Bildschirm. MarkUp legt
sich über alles, was gerade zu sehen ist — Präsentation, Tabelle, Video — und
lässt darauf zeichnen, ohne die darunterliegende Anwendung zu berühren.

![Lizenz](https://img.shields.io/badge/Lizenz-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Plattform](https://img.shields.io/badge/Plattform-Windows-0078D4)

## Was es kann

| Werkzeug | Beschreibung |
|---|---|
| Freihand | zeichnen mit der Maus |
| Pfeil, Rechteck, Ellipse | die üblichen Formen |
| Text | Beschriftung, Enter beendet die Eingabe |
| Highlighter | halbtransparent, zum Hervorheben |
| Zensur | deckt ab, was nicht auf den Bildschirm soll |
| Stempel | Symbole und Textstempel (Approved, Rejected, Draft, Confidential) |
| Nummerierung | fortlaufende Ziffernkreise für Schrittanleitungen |

Dazu: **Markup ein/aus** lässt Klicks wieder zur darunterliegenden Anwendung
durch, ohne das Gezeichnete zu verlieren. Auf mehreren Bildschirmen ist die
Werkzeugleiste überall gleichzeitig aktiv — gezeichnet wird dort, wo der Zeiger
ist.

Alle Tasten sind frei belegbar; die Hilfe (Standard: `F1`) zeigt die aktuelle
Belegung.

## Bauen

```
dotnet build -c Release
```

Ergebnis: `bin/Release/net8.0-windows/win-x64/MarkUp.dll` bzw. die EXE.
Voraussetzung ist das .NET-8-SDK für Windows. MarkUp hat **keine**
NuGet-Abhängigkeiten.

## Eigene Stempel

Jede Datei `stamps/<name>_logo.png` wird beim Start als zusätzlicher Stempel
angeboten. Der Name in der Auswahl ergibt sich aus dem Dateinamen.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

Die mitgelieferten Emoji-Grafiken stammen aus **Twemoji** und stehen unter
**CC BY 4.0**. Einzelheiten und die vollständige Namensnennung:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

**Eselchen Labs**
