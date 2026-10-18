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
  bosshp.py, probe.py, view.py (side-by-side capture), tt.py (Twilight Thorn approach), spam.py SECONDS [TARGET]
  (Cross/Triangle spam). device_bash calls stop at 45 s; split long steps.
- Boss hits and boss HP changes have their own log budget ([hit]/[hp] lines with B_ names).

## Done and verified (2026-10-18, laptop solo test)
- Test tab: warp (quick spots incl. Larxene, Lexaeus, Twilight Thorn, Axel I), god mode, infinite MP, refill, screenshots, mark, switches, save lock, revive tools, reconnect.
- Revive: downed instead of game over, hold-Triangle revive both ways, Revive now.
- Enemy sync: targeting both players, copied enemies hit Sora 2, kills sync. Shadows/Neoshadows/Driller Moles
  are copied with their underground state (actor+0x120 bits 0x0A) and are hittable by the friend.
- Bosses: friend's hits count (boss claims skip the full-match hold), a boss body that vanishes with HP left isn't
  a death, the host's boss defeat ends the boss on the friend's game. Larxene AS (04/21 map/btl/evt 128) end to end;
  Lexaeus AS (04/21 programs 122) HP sync.
- Large bosses (objentry type 12, also type 21 large enemies) get the same HP, claim and defeat sync. Twilight Thorn
  (02/22 from save slot 3, LV1 Roxas; `boot.py load 1`) verified 2026-10-18: Game 2's hits became boss claims, the host
  applied them (HP 2 -> 0 from Game 2's last hit) and both games went to the after-fight cutscene together.
- Fixed: crash dump on every game close (render worker std::thread destructor at exit).
- Dev builds: Test tab > Co-op > Dev build; 20261018.13 on the PC. The zip carries its own build id.

## Open
1. Boss copying (Test switch "Copy the host's boss", KH2COOP_MIRROR_BOSSES=1): boss by boss, list in
   inject/src/EnemyMirrorPose.hpp kMirroredBosses. Lexaeus done (position, motion, hits, HP). Twilight Thorn
   (candidate, 20261018.9): position, motion, hits, HP and defeat match; while a boss holds the local player
   (player+0x2B0 = the boss, a grab) the friend's own AI keeps it until it lets go, then the copy resumes. Not yet
   seen: Key Counter/Reversal on the friend's game during those grabs.
   Fixed 20261018.11: a boss's defeat cutscene starts on the frame its HP hits 0, and the host only sent the death
   from its census (open gameplay only), so the friend ended the fight ~9.7 s late. The host now sends the death
   from the HP-change hook ("host death ... (at the hit)"); measured 0.2 s. (20261018.10 shipped old mod files.)
   Axel I (B_EX110_SKIRMISH, objectId 2229, Sandlot 02/05 programs 87; 86 = the Dusks before him) verified
   2026-10-18 with and without copying: HP, claims, the friend's last hit, both in the next cutscene together.
   Copy: brief hand-backs around the friend's hits, then back in step. Program numbers come from the GoA ROM mod's
   Events(m,b,e) checks (KH2FM-Mods-Num/GoA-ROM-Edition) and khbr's locations.yaml (spawn groups per room).
   With the copy on, the friend's player is rarely grabbed (the copy aims at the host's player); Key Counter and
   Lunarsault prompts were seen on the friend's game, their damage not yet checked.
   Larxene's duplicates
   are made by her own AI on each game, so she stays uncopied. Struggle gauges (Lexaeus power) and reaction
   commands on Game 2 are still per game.
2. A multi-part boss is untested; enemy projectiles and rejoin mid-fight are untested.
   Crash seen twice on Game 2 (10-17, 10-18, both Twilight Town): first-chance 0xC00000FD at clr.dll+0x5F8AE0,
   a fresh .NET thread probing ~1 MB of stack. Not our thread; cause unknown. The crash dump comes out empty.
   Co-op link dropped once while idle (10-18 17:43, host runtime "Input mailbox closed"); cause unknown.
3. Drive Forms in co-op; Sora 2 shows the friend's keyblade; keyblade swap only visible to the switcher.
4. Test tab: bring together, freeze, items, debug view, auto tests.
5. Release 0.1.4 (go-ahead needed); website with the installer link; trim debug logs.
