# Connected Hearts: dev status

Read this first in a new chat. Updated with every dev build.

## Machines
- PC (user adminstrators1): `C:\Users\adminstrators1\Documents\Connected Hearts`, game on `D:\SteamLibrary\...`.
- Laptop (user ulyss, device "sen"): `C:\Users\ulyss\Documents\Connected Hearts`. `Documents\KH2 Co-op` is an old copy; don't use it.
- Install a dev build from the launcher: Test (top bar) > Co-op > Dev build > Get latest dev build. The games must be closed.

## Channels
- Live (friends): `main` branch, version 0.1.3. Push there only with Sento's go-ahead.
- Dev (Sento's machines, Test tab only): `dev` branch, `dev/build.txt` + `dev/connected-hearts-dev.zip`, built by
  `tools/make_dev.py`. A dev build is protocol 20; it doesn't connect to 0.1.3.
- Mod source: JayKazma/connected-hearts-mod (`git push chmod HEAD:main`, only with go-ahead); local commits ahead of it.

## Remote testing (while linked to a machine)
- Launcher inbox: JSON files in `build\test\inbox` run like Test tab clicks; `build\test\activity.log` is the log.
- `{"cmd":"chtest","args":"...","target":"1|2|both"}` runs bin\chtest.exe; answers land in `build\test\results`.
- Helper scripts in `build\test`: q.py (send and read), boot.py start|load|connect, cycle.py, skip.py, hunt.py,
  bosshp.py, probe.py, view.py (side-by-side capture). device_bash calls stop at 45 s; split long steps.

## Done and verified (2026-10-18, laptop solo test)
- Test tab: warp, god mode, infinite MP, refill, screenshots, mark, switches, save lock, revive tools, reconnect.
- Revive: downed instead of game over, hold-Triangle revive both ways, Revive now.
- Enemy sync: targeting both players, copied enemies hit Sora 2, kills sync. Shadows/Neoshadows/Driller Moles
  are copied with their underground state (actor+0x120 bits 0x0A) and are hittable by the friend.
- Bosses: friend's hits count (boss claims skip the full-match hold), a boss body that vanishes with HP left isn't
  a death, the host's boss defeat ends the boss on the friend's game. Larxene AS (04/21 map/btl/evt 128) end to end;
  Lexaeus AS (04/21 programs 122) HP sync.
- Fixed: crash dump on every game close (render worker std::thread destructor at exit).
- Dev builds: Test tab > Co-op > Dev build; 20261018.4 on the PC.

## Open
1. Boss copying (Test switch "Copy the host's boss", KH2COOP_MIRROR_BOSSES=1): boss by boss, list in
   inject/src/EnemyMirrorPose.hpp kMirroredBosses. Lexaeus done (position, motion, hits, HP). Larxene's duplicates
   are made by her own AI on each game, so she stays uncopied. Struggle gauges (Lexaeus power) and reaction
   commands on Game 2 are still per game.
2. A multi-part boss is untested; enemy projectiles and rejoin mid-fight are untested.
3. Drive Forms in co-op; Sora 2 shows the friend's keyblade; keyblade swap only visible to the switcher.
4. Test tab: bring together, freeze, items, debug view, auto tests.
5. Release 0.1.4 (go-ahead needed); website with the installer link; trim debug logs.
