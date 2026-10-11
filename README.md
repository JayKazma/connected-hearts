# Connected Hearts

Two-player online co-op for **Kingdom Hearts II Final Mix** (Steam, Global build) over Steam.
The other player appears in your party as Sora 2 — same room, same enemies, their own keyblade.

## Download

**[ConnectedHeartsSetup.exe](https://github.com/JayKazma/connected-hearts/raw/main/dist/ConnectedHeartsSetup.exe)** — run it, pick a
folder, and it installs the launcher with shortcuts. Prefer no installer? [ConnectedHearts.zip](https://github.com/JayKazma/connected-hearts/raw/main/dist/ConnectedHearts.zip)
unpacks anywhere; run `ConnectedHearts.exe`. The launcher keeps itself up to date. Current version: see [`version.txt`](version.txt).

Steam must be running and signed in.

The files are not code-signed yet, so Windows may show "Windows protected your PC" the first time. Don't turn off your
antivirus for this. Check the files first (see **Safety** below), and only then choose More info, Run anyway.

## Play

1. Both players open the launcher. It finds the game folder (use **Browse** if it doesn't).
2. Pick **Host** or **Join**, enter the other player's SteamID (yours shows in the launcher once the game is up; click it to copy), press **Start**.
3. The game starts; when Steam is ready the button becomes **Start hosting** / **Join host**. Press it.
4. The joiner's Sora appears in the host's party at the next room change.

**Solo test**: two windows on one PC through a local relay, tiled side by side.

## What's in the box

| | |
| --- | --- |
| `ConnectedHearts.exe` | The launcher (C#, .NET Framework 4.x, WebView2). Source in `src\`, page in `ui\` |
| `bin\` | The mod: `connected_hearts.dll` (runs inside KH2), `winmm.dll` (the loader, see below), `connected_hearts_runtime.exe` (Steam session), `connected_hearts_relay.exe` (local relay) |
| `lib\` | Microsoft WebView2 wrappers |
| `settings.json` | Game folder, mode and SteamIDs |
| `build\rig\logs\` | Game, Steam and co-op logs — **Collect logs** in the launcher zips them for a bug report |

The mod's source is in [connected-hearts-mod](https://github.com/JayKazma/connected-hearts-mod) (GPL-3.0).

## Safety

What it does:

- **How the mod gets into the game.** For a session, the launcher copies `bin\winmm.dll` into the game folder. KH2 loads
  that file by name at startup; it passes every call on to Windows' own `winmm.dll`, and loads `connected_hearts.dll`
  only when this launcher started the game. Started from Steam, the game runs untouched. The launcher deletes the file
  again once its games have closed. If another mod already has a `winmm.dll` there, the launcher stops and tells you
  instead of replacing it.
- **Internet.** The launcher downloads its updates from this repository and reads public Steam profile pages for player
  names. Co-op traffic goes through Steam to the one SteamID you enter. Nothing is uploaded anywhere.
- **Files.** Its own folder (settings, logs, crash dumps), copies of your KH2 saves in `save-backups\` before each start,
  and the loader in the game folder during a session. The clipboard is read only when you press Paste ID.

Some antivirus engines flag unsigned, rarely-downloaded game mods by machine-learning guesses (names ending in `!ml`,
"Heuristic", "Static AI", "confidence"). The source for every file is public in this repository and the mod's.

## Releasing a version

`version.txt` is the current version; `dist/connected-hearts-update.zip` the matching package and `dist/notes.txt` the
change notes. Launchers read all three from `main`, so pushing `main` is the release.

    python tools/make_release.py --bin <folder with the mod binaries> --version 0.4.0 --notes "what changed"
    sh tools/build_exe.sh        # rebuilds ConnectedHearts.exe and dist/ConnectedHeartsSetup.exe (file details from version.txt)

Commit `version.txt`, `dist/` and `ConnectedHearts.exe`, push.
