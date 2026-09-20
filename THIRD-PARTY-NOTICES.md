# Third-party notices — MarkUp

Copyright (C) 2026 Eselchen Labs

## Emoji graphics — Twemoji (CC BY 4.0)

The stamps under `stamps/emoji/` (23 PNG files, 72 × 72) come from **Twemoji**
and are licensed under **CC BY 4.0**.

**Provenance verified** (not assumed): on 2026-09-06 the files were compared by
SHA-256 against the official Twemoji release. Several files are **bit-identical**
to Twemoji 14.0.2 (`assets/72x72/`), among them:

| File | Unicode | Result |
|---|---|---|
| `fire.png` | U+1F525 | identical to twemoji 14.0.2 |
| `rocket.png` | U+1F680 | identical to twemoji 14.0.2 |
| `unicorn.png` | U+1F984 | identical to twemoji 14.0.2 |
| `poop.png` | U+1F4A9 | identical to twemoji 14.0.2 |
| `heart.png` | U+2764 | identical to twemoji 14.0.2 |

The files no longer carry any metadata; when they were adopted they were renamed
from their Unicode code point names to readable ones (`donkey.png` instead of
`1f434.png`). Renaming is not an adaptation of the work — the image content is
unchanged (bit-identical), so attribution alone is required.

**Twemoji** is CC BY 4.0. For distribution, attribution is sufficient (name,
author, license, link to the license text). There is **no** ShareAlike
obligation (unlike OpenMoji), and using it does not place MarkUp itself under a
CC license.

The notice shipped with the program and reachable from its user interface:

    Emoji graphics: Twemoji
    Copyright 2020 Twitter, Inc and other contributors
    (continued by the Twemoji community, github.com/jdecked/twemoji)
    License: CC BY 4.0 — https://creativecommons.org/licenses/by/4.0/

The emoji from the Windows font Segoe UI Emoji are deliberately not used: they
belong to Microsoft and may not be redistributed as image files.

## Company logos — NOT part of the release

`stamps/peakon_logo.png` and `stamps/workday_logo.png` are third-party company
marks (Peakon/Workday). They are **not** shipped: the public build
(`-p:PublicBuild=true`, constant `PUBLIC_BUILD`) strips them from `Program.cs`,
and the installer only picks up `stamps/emoji/`, never `stamps/*.png`. They
remain exclusive to the private edition.

## Runtime — .NET 8 (MIT)

MarkUp is built **self-contained** against **.NET 8 (Windows Desktop)**: the
runtime (MIT license) is embedded in the EXE, so nothing has to be installed
separately. There are no other third-party components — `MarkUp.csproj` does not
contain a single `PackageReference`.
