# Connected Hearts

Two-player online co-op for **Kingdom Hearts II Final Mix** (Steam, Global build) over Steam.
The other player appears in your party as Sora 2 — same room, same enemies, their own keyblade.

## Download

**[KH2CoopSetup.exe](https://github.com/JayKazma/kh2-coop/raw/main/dist/KH2CoopSetup.exe)** — run it, pick a folder, and it installs the launcher with shortcuts.
The launcher keeps itself up to date. Current version: see [`version.txt`](version.txt).

Prefer a zip? [`dist/kh2coop-update.zip`](dist/kh2coop-update.zip) unpacks anywhere; run `KH2Coop.exe`.

Steam must be running and signed in. Windows Defender may ask once about the mod's DLL; allow it.

## Play

1. Both players open the launcher. It finds the game folder (use **Browse** if it doesn't).
2. Pick **Host** or **Join**, enter the other player's SteamID (yours shows in the launcher once the game is up; click it to copy), press **Start**.
3. The game starts; when Steam is ready the button becomes **Start hosting** / **Join host**. Press it.
4. The joiner's Sora appears in the host's party at the next room change.

**Solo test**: two windows on one PC through a local relay, tiled side by side.

## What's in the box

| | |
| --- | --- |
| `KH2Coop.exe` | The launcher (C#, .NET Framework 4.x, WebView2). Source in `src\`, page in `ui\` |
| `bin\` | The mod: `kh2coop_inject.dll` (runs inside KH2), `kh2coop_runtime_scaffold.exe` (Steam session), `kh2coop_server.exe` (local relay), `kh2ctl.exe` (launches the game) |
| `lib\` | Microsoft WebView2 wrappers |
| `settings.json` | Game folder, mode and SteamIDs |
| `build\rig\logs\` | Game, Steam and co-op logs — **Collect logs** in the launcher zips them for a bug report |

The mod's source is in [kh2-coop-mod](https://github.com/JayKazma/kh2-coop-mod) (GPL-3.0).

## Releasing a version

`version.txt` is the current version; `dist/kh2coop-update.zip` the matching package and `dist/notes.txt` the
change notes. Launchers read all three from `main`, so pushing `main` is the release.

    python tools/make_release.py --bin <folder with the mod binaries> --version 0.4.0 --notes "what changed"
    sh tools/build_exe.sh        # rebuilds KH2Coop.exe and dist/KH2CoopSetup.exe

Commit `version.txt`, `dist/` and `KH2Coop.exe`, push.
