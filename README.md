# MarkUp

<img src="assets/eselchen-labs.png" align="right" width="110" alt="Eselchen Labs">


A transparent annotation tool for the Windows desktop. MarkUp sits on top of
whatever is currently on screen — a presentation, a spreadsheet, a video — and
lets you draw over it without touching the application underneath.

![License](https://img.shields.io/badge/License-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D4)

## What it does

| Tool | Description |
|---|---|
| Freehand | draw with the mouse |
| Arrow, rectangle, ellipse | the usual shapes |
| Text | captions; Enter finishes the entry |
| Highlighter | semi-transparent, for emphasis |
| Redaction | covers up anything that should not be on screen |
| Stamps | symbols and text stamps (Approved, Rejected, Draft, Confidential) |
| Numbering | consecutive numbered circles for step-by-step instructions |

On top of that: **markup on/off** lets clicks through to the application
underneath again without losing what you have drawn. Across several monitors
the toolbar is active on all of them at once — you draw wherever the pointer
is.

Every key is freely assignable; the help screen (default: `F1`) shows the
current bindings.

## Building

```
dotnet build -c Release
```

Result: `bin/Release/net8.0-windows/win-x64/MarkUp.dll` and the matching EXE.
You need the .NET 8 SDK for Windows. MarkUp has **no** NuGet dependencies.

## Custom stamps

Every file named `stamps/<name>_logo.png` is offered as an additional stamp at
startup. The name shown in the picker is derived from the file name.

## License

MIT — see [LICENSE](LICENSE).

The bundled emoji graphics come from **Twemoji** and are licensed under
**CC BY 4.0**. Details and the full attribution are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

**Eselchen Labs**
