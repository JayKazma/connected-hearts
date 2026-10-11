# Connected Hearts

Two-player online co-op for **Kingdom Hearts II Final Mix** (Steam, Global build) over Steam.
The other player joins your party as Sora 2.

Steam must be running and signed in. Open `ConnectedHearts.exe`; the launcher keeps itself up to date.

## Play

1. Both players open the launcher. It finds the game folder (use **Browse** if it doesn't).
2. Pick **Host** or **Join**, enter the other player's SteamID (yours shows in the launcher once the game is up; click it
   to copy), press **Start**.
3. Load your save. When the launcher says the save is loaded, press **Connect**.

**Solo test**: two windows on one PC, tiled side by side.

## What's in the folder

| | |
| --- | --- |
| `ConnectedHearts.exe` | The launcher |
| `bin\connected_hearts.dll` | The mod; it runs inside KH2 |
| `bin\winmm.dll` | The loader, copied into the game folder only during a session (see below) |
| `bin\connected_hearts_runtime.exe` | The co-op connection over Steam |
| `bin\connected_hearts_relay.exe` | The solo test's local connection |
| `lib\` | Microsoft WebView2 (the launcher's window) |
| `save-backups\` | Copies of your KH2 saves, made before each start (newest 10) |
| `build\rig\logs\` | Logs; **Collect logs** in the launcher zips them for a bug report |

## Safety

- **How the mod gets into the game.** For a session the launcher copies `bin\winmm.dll` into the game folder. KH2
  loads that file at startup; it passes every call on to Windows' own `winmm.dll` and loads the mod only when this
  launcher started the game. Started from Steam, the game runs untouched. The launcher deletes the file again once its
  games have closed, and stops instead if another mod already has a `winmm.dll` there.
- **Internet.** The launcher downloads its own updates and reads public Steam profile pages for player names. Co-op
  traffic goes through Steam to the one SteamID you enter. Nothing is uploaded.
- **Files.** Its own folder, the save backups above, and the loader in the game folder during a session. The clipboard
  is read only when you press Paste ID.

Licensed under the GPL-3.0; see `licenses\`.
