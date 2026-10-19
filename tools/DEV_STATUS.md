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
  bosshp.py, probe.py, ard.py, cmp.py (boss side by side), death.py (timed defeat), kc.py/kt.py (grab watch), view.py (side-by-side capture), tt.py (Twilight Thorn approach), spam.py SECONDS [TARGET]
  (Cross/Triangle spam). device_bash calls stop at 45 s; split long steps.
- Boss hits and boss HP changes have their own log budget ([hit]/[hp] lines with B_ names).

- Helper scripts are also kept in tools/test-helpers (copy into <install>\build\test); the laptop got them 10-19.

## Done and verified (2026-10-18, laptop solo test)
- Test tab: warp (quick spots incl. Larxene, Lexaeus, Twilight Thorn, Axel I, Axel II, Shan Yu, Barbossa, Grim Reaper I and II), god mode, infinite MP, refill, screenshots, mark, switches, save lock, revive tools, reconnect.
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
- Dev builds: Test tab > Co-op > Dev build; 20261019.2 on the laptop, 20261018.18 on the PC. The zip carries its own build id.

## Build note
- The first build after a change can sit on EntityHook.cpp for 30+ minutes at full CPU; killing it and rerunning
  the build compiles it in seconds. If EntityHook.cpp is still compiling after ~5 minutes, kill and rerun.

## Open
1. Boss copying: ON by default since 20261019.2 for every boss (objentry types 3/12) except
   kUncopiedBosses in inject/src/EnemyMirrorPose.hpp (Larxene). KH2COOP_MIRROR_BOSSES=0 / Test switch "Keep each
   game's own boss" turns it off. Checked by default on the laptop: Barbossa follows the host exactly (he
   didn't before). Spot-test only unusual bosses (several bodies, clones, phases, grabs); add misbehaving ones to
   the uncopied list. History: boss by boss, the old list was Lexaeus done (position, motion, hits, HP). Twilight Thorn
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
   Axel II (B_EX110, objectId 81, Basement Hall 02/14 programs 137; Data Axel = 213) verified 2026-10-18 with and
   without copying, same checks as Axel I.
   Shan Yu (Land of Dragons Palace Gate 08/09, btl 75, map/evt default; B_MU100 objectId 21 + falcon B_MU110
   objectId 22, both type 3), tested with the Roxas save: both parts' HP and the friend's claims sync. Not finished:
   after Shan Yu fell to 221 and the falcon to 10 the game held their HP on the host too (even a direct kill), so
   the defeat wasn't reached; likely a scripted phase, maybe needing a Sora save.
   With the Port Royal save (Sora): Shan Yu's half-HP phase (221/442) holds HP for a while, then the friend's hits
   count again (199 -> 182); the host-side defeat reached both games, but with btl 75 only both sat on a white
   screen (the fight's own end needs more than map 117; second try stayed in the phase). Quick spot: 08/09 map 117 btl 75.
   Barbossa (B_CA010, objectId 1015, Treasure Heap 10/0A programs 60, the save's next story fight) verified
   2026-10-18: the friend's game first saw him 35 units from the host's spawn point, so he never paired (HP 612 vs
   600). Fix: a lone boss pairs by object id when no spawn point matches (one host entry, one living local, same
   id; kept through death). After it: HP sync, the host's kill applied on the friend's game by its netId
   (established-death), both in the after-fight cutscene together. The friend's hits weren't landed (he hovers).
   Grim Reaper I (B_CA050, objectId 1543, Black Pearl 10/12 map/btl 85) verified 2026-10-19 on the laptop with the
   Port Royal save: HP sync, the friend's claims, the friend's last hit (4 -> 0), death on both. Both then sat on
   a white screen (the warp has no end event, like Shan Yu btl-only). Grim Reaper II (same object, Harbor 10/01
   map/btl 54): HP sync confirmed (host HP poked 510 -> 300, friend followed); he takes no damage outside his
   medallion phase, so claims and defeat weren't reached.
   build/test/ard.py [TARGET] lists the current room's map/btl/evt programs and their spawn groups (area data at
   rva 0x2A0F828, Steam); khbr's locations.yaml says which group is the boss.
   Larxene's duplicates
   are made by her own AI on each game, so she stays uncopied. Struggle gauges (Lexaeus power) and reaction
   commands on Game 2 are still per game.
2. A multi-part boss is untested; enemy projectiles and rejoin mid-fight are untested.
   Crash seen three times, always on Game 2 (10-17 and 10-18 Twilight Town, 10-18 Land of Dragons): first-chance
   0xC00000FD at clr.dll+0x5F8AE0, a fresh .NET thread probing ~1 MB of stack. Not our thread; cause unknown.
   Since 20261018.15 the crash handler writes connected_hearts_overflow_<pid>.dmp from a helper thread (the
   old dump was empty) and logs the loaded modules and the module rcx points into.
   Co-op link dropped once while idle (10-18 17:43, host runtime "Input mailbox closed"); cause unknown.
3. Drive Forms in co-op; Sora 2 shows the friend's keyblade; keyblade swap only visible to the switcher.
4. Test tab: bring together, freeze, items, debug view, auto tests.
5. Release 0.1.4 (go-ahead needed); website with the installer link; trim debug logs.
