// Connected Hearts launcher. Single-file C# (C# 5, .NET Framework 4.x) so it builds with the csc.exe that ships
// with Windows: see build.bat. Hosts ui\index.html in a borderless WebView2 window and runs the
// game/runtime/update logic. Layout next to the exe: bin\, ui\, lib\ (WebView2 wrappers), version.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ConnectedHearts
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

        // The main top-level window of a process (KH2 creates its window a while after launch).
        public static IntPtr MainWindowOf(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wp; GetWindowThreadProcessId(h, out wp);
                if ((int)wp != pid || !IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true; // GW_OWNER: skip owned popups
                found = h; return false;
            }, IntPtr.Zero);
            return found;
        }
        // Side by side on the primary screen's work area. Borderless: the frame is stripped so the two
        // halves meet edge to edge (a window the game made borderless itself is handled the same way).
        // With borders: a borderless (fullscreen-window) game gets its frame back first. A game in
        // exclusive fullscreen cannot be moved from outside.
        // Returns 0 = windows not found yet, 1 = moved, 2 = already in place.
        public static int Tile(int pidLeft, int pidRight, bool borderless)
        {
            IntPtr a = MainWindowOf(pidLeft), b = MainWindowOf(pidRight);
            if (a == IntPtr.Zero || b == IntPtr.Zero) return 0;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = wa.Width / 2, h = wa.Height, moved = 0;
            foreach (var pair in new[] { new { H = a, X = wa.Left }, new { H = b, X = wa.Left + w } })
            {
                RECT r; GetWindowRect(pair.H, out r);
                const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_POPUP = unchecked((int)0x80000000);
                int style = GetWindowLong(pair.H, GWL_STYLE);
                bool hasFrame = (style & WS_CAPTION) == WS_CAPTION;
                bool inPlace = !IsIconic(pair.H) && r.L == pair.X && r.T == wa.Top && r.R - r.L == w && r.B - r.T == h;
                if (inPlace && hasFrame != borderless) continue;
                int want = borderless ? (style & ~(WS_CAPTION | WS_THICKFRAME)) | WS_POPUP : (style & ~WS_POPUP) | WS_OVERLAPPEDWINDOW;
                if (want != style) SetWindowLong(pair.H, GWL_STYLE, want);
                if (IsIconic(pair.H)) ShowWindow(pair.H, 9);
                SetWindowPos(pair.H, IntPtr.Zero, pair.X, wa.Top, w, h, 0x0004 | 0x0040 | 0x0020); // SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED
                moved++;
            }
            return moved > 0 ? 1 : 2;
        }
    }

    static class Program
    {
        public static string Root;

        [STAThread]
        static int Main(string[] args)
        {
            Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            try { if (!Native.SetProcessDpiAwarenessContext((IntPtr)(-4))) Native.SetProcessDPIAware(); } catch { try { Native.SetProcessDPIAware(); } catch { } }
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name + ".dll";
                string p = Path.Combine(Path.Combine(Root, "lib"), name);
                return File.Exists(p) ? Assembly.UnsafeLoadFrom(p) : null;
            };
            CleanOld(Root); CleanOld(Path.Combine(Root, "lib")); CleanOld(Path.Combine(Root, "bin"));
            try { File.Delete(Path.Combine(Root, "bin", "chctl.exe")); } catch { } // replaced by the game-folder loader (0.1.1)
            try { File.Delete(Path.Combine(Root, "build.bat")); if (Directory.Exists(Path.Combine(Root, "src"))) Directory.Delete(Path.Combine(Root, "src"), true); } catch { } // no longer shipped
            bool created;
            using (var mutex = new Mutex(true, "ConnectedHeartsLauncher", out created))
            {
                if (!created)
                {
                    // Discord's Join button starts us again: the open launcher gets the join over Discord's pipe.
                    if (!args.Contains("--discord")) MessageBox.Show("Connected Hearts is already open.", "Connected Hearts");
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return Run(args);
            }
        }

        static void CleanOld(string dir)
        {
            try { if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.old*")) { try { File.Delete(f); } catch { } } } catch { }
        }

        static int Run(string[] args)
        {
            var core = new Core(Root);
            bool noUpdate = args.Any(a => a == "-NoUpdate" || a == "--no-update");
            var form = new MainForm(core, noUpdate);
            Application.Run(form);
            core.Shutdown();
            if (core.RestartAfterExit)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(Root, "ConnectedHearts.exe"), "-NoUpdate") { WorkingDirectory = Root, UseShellExecute = true }); } catch { }
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ launcher logic
    class Core
    {
        public readonly string Root, Bin, Loader, Runtime, Dll, Relay, Logs, SettingsFile, VersionFile;
        const string UpdateBase = "https://raw.githubusercontent.com/JayKazma/connected-hearts/main";
        const string GameExe = "KINGDOM HEARTS II FINAL MIX.exe";
        const int RelayPort = 7782;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly object gate = new object();

        // settings
        public string GameDir = "", Mode = "host", HostId = "", FriendId = "";
        public List<string> Recent = new List<string>();
        // Steam persona names and avatars by SteamID, from the public community profile
        // (steamcommunity.com/profiles/<id>?xml=1; no key needed, public profiles only). Cached in settings.json.
        public Dictionary<string, Dictionary<string, object>> Names = new Dictionary<string, Dictionary<string, object>>();
        // Test switches for the mod (settings.json "devEnv": {"KH2COOP_...": "1"}), applied after the defaults.
        public Dictionary<string, string> DevEnv = new Dictionary<string, string>();
        readonly HashSet<string> resolving = new HashSet<string>();
        public string World = "", Room = ""; // from the runtime's room-state lines
        int WorldId = -1;
        Dictionary<string, object> PartyMe, PartyPeer; // from the runtime's [Party] lines (player cards)
        bool tiled; int tileTries;
        public bool TileBorderless = true; // solo test: the two game windows meet edge to edge (settings.json "tileBorderless")
        public bool CastReplay = true;     // replay the other player's magic/items on their clone (settings.json "castReplay")
        public bool DiscordStatus = true;  // show the session in Discord (settings.json "discordStatus")
        // Developer Test tab: shown only when bin\chtest.exe is present (never in the release package).
        public readonly string TestTool;
        public bool TestKit { get { return File.Exists(TestTool); } }
        public bool SaveLock = true, TestGod, TestMp; // settings.json "saveLock", "testGod", "testMp"
        readonly string testDir;
        int testBusy; DateTime reconnectAt = DateTime.MinValue;
        readonly DiscordPresence discord = new DiscordPresence();
        long sessionStart;                 // unix seconds when the current game started (Discord timer)
        string pendingJoin;                // host SteamID from a Discord Join, joined once the save is loaded

        // state
        public int GamePid, GamePid2;
        int fieldPid, fieldPid2; // game PIDs whose DLL logged "[field] save loaded"
        public bool SaveLoaded { get { return GamePid != 0 && fieldPid == GamePid && (Mode != "local" || (GamePid2 != 0 && fieldPid2 == GamePid2)); } }
        public string MyId = "", Problem = "", Update = "", UpdateText = "", Notes = "";
        public bool Updating, Launching, Connected;
        public int? Ping; public string Loss;
        public bool RestartAfterExit;
        Process runtimeA, runtimeB, relay;
        readonly List<string> lines = new List<string>();

        // background work
        Task<string[]> launchTask; Task<string[]> updateTask; Task<string> downloadTask;

        public Core(string root)
        {
            Root = root; Bin = Path.Combine(root, "bin");
            Loader = Path.Combine(Bin, "winmm.dll"); Runtime = Path.Combine(Bin, "connected_hearts_runtime.exe");
            Dll = Path.Combine(Bin, "connected_hearts.dll"); Relay = Path.Combine(Bin, "connected_hearts_relay.exe");
            TestTool = Path.Combine(Bin, "chtest.exe"); testDir = Path.Combine(root, @"build\test");
            Logs = Path.Combine(root, @"build\rig\logs"); SettingsFile = Path.Combine(root, "settings.json"); VersionFile = Path.Combine(root, "version.txt");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            LoadSettings();
            ResolveKnownNames();
            if (string.IsNullOrEmpty(GameDir)) GameDir = FindGameDir();
            Log("Connected Hearts " + LocalVersion() + " ready.");
            if (string.IsNullOrEmpty(GameDir)) Log("Game folder not found automatically. Use Browse.");
            if (DiscordStatus) { DiscordPresence.Register(Path.Combine(root, "ConnectedHearts.exe")); discord.Start(); }
        }

        // ---- helpers
        public void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            lock (gate)
            {
                lines.Add(line); if (lines.Count > 300) lines.RemoveRange(0, lines.Count - 300);
                // The Test tab's activity, readable from outside the launcher (remote testing).
                if (testDir != null && TestKit) { try { Directory.CreateDirectory(testDir); File.AppendAllText(Path.Combine(testDir, "activity.log"), DateTime.Now.ToString("yyyy-MM-dd ") + line + "\r\n"); } catch { } }
            }
        }
        public string LocalVersion() { try { return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "0"; } catch { return "0"; } }
        static bool Alive(int pid) { if (pid == 0) return false; try { var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; } }
        static bool Alive(Process p) { try { return p != null && !p.HasExited; } catch { return false; } }
        public bool GameAlive { get { return Alive(GamePid); } }
        public bool Game2Alive { get { return Alive(GamePid2); } }
        public bool RuntimeAlive { get { return Alive(runtimeA); } }
        static string ReadShared(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(s)) return r.ReadToEnd();
        }
        static bool Newer(string a, string b)
        {
            try { return new Version(a.TrimStart(new[] { 'v' })) > new Version(b.TrimStart(new[] { 'v' })); } catch { return a != b && string.CompareOrdinal(a, b) > 0; }
        }

        // ---- settings
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var d = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile));
                object v;
                if (d.TryGetValue("gameDir", out v) && v != null) GameDir = v.ToString();
                if (d.TryGetValue("tileBorderless", out v) && v is bool) TileBorderless = (bool)v;
                if (d.TryGetValue("castReplay", out v) && v is bool) CastReplay = (bool)v;
                if (d.TryGetValue("discordStatus", out v) && v is bool) DiscordStatus = (bool)v;
                if (d.TryGetValue("saveLock", out v) && v is bool) SaveLock = (bool)v;
                if (d.TryGetValue("testGod", out v) && v is bool) TestGod = (bool)v;
                if (d.TryGetValue("testMp", out v) && v is bool) TestMp = (bool)v;
                if (d.TryGetValue("mode", out v) && v != null) Mode = v.ToString();
                if (d.TryGetValue("hostId", out v) && v != null) HostId = v.ToString();
                if (d.TryGetValue("friendId", out v) && v != null) FriendId = v.ToString();
                if (d.TryGetValue("recent", out v) && v is System.Collections.ArrayList)
                    foreach (var r in (System.Collections.ArrayList)v) if (r != null && Regex.IsMatch(r.ToString(), @"^\d{17}$")) Recent.Add(r.ToString());
                if (d.TryGetValue("devEnv", out v) && v is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)v)
                        if (kv.Key.StartsWith("KH2COOP_") && kv.Value != null) DevEnv[kv.Key] = kv.Value.ToString();
                if (d.TryGetValue("names", out v) && v is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)v)
                        if (Regex.IsMatch(kv.Key, @"^\d{17}$") && kv.Value is Dictionary<string, object>) Names[kv.Key] = (Dictionary<string, object>)kv.Value;
            }
            catch { }
        }
        public void SaveSettings()
        {
            try
            {
                var d = new Dictionary<string, object> { { "gameDir", GameDir }, { "tileBorderless", TileBorderless }, { "castReplay", CastReplay }, { "discordStatus", DiscordStatus }, { "mode", Mode }, { "hostId", HostId }, { "friendId", FriendId }, { "recent", Recent }, { "names", Names } };
                if (DevEnv.Count > 0) d["devEnv"] = DevEnv;
                if (TestKit) { d["saveLock"] = SaveLock; d["testGod"] = TestGod; d["testMp"] = TestMp; }
                File.WriteAllText(SettingsFile, json.Serialize(d), Encoding.UTF8);
            }
            catch { }
        }
        string FindGameDir()
        {
            try
            {
                var c = new List<string> { @"C:\Program Files (x86)\Steam\steamapps\common\KINGDOM HEARTS -HD 1.5+2.5 ReMIX-" };
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                    c.Add(Path.Combine(drive.RootDirectory.FullName, @"SteamLibrary\steamapps\common\KINGDOM HEARTS -HD 1.5+2.5 ReMIX-"));
                    c.Add(Path.Combine(drive.RootDirectory.FullName, @"Steam\steamapps\common\KINGDOM HEARTS -HD 1.5+2.5 ReMIX-"));
                }
                foreach (var dir in c) if (File.Exists(Path.Combine(dir, GameExe))) return dir;
            }
            catch { }
            return "";
        }

        // ---- state for the page
        public string StateJson()
        {
            string peer = Mode == "join" ? HostId : Mode == "local" ? "" : FriendId;
            List<string> l; lock (gate) l = new List<string>(lines);
            var d = new Dictionary<string, object>
            {
                { "version", LocalVersion() }, { "update", Update }, { "updateText", UpdateText }, { "updating", Updating },
                { "gameDir", GameDir }, { "mode", Mode }, { "peerId", peer },
                { "gameRunning", GameAlive }, { "gamePid", GamePid }, { "myId", MyId }, { "launching", Launching },
                { "game2Running", Game2Alive }, { "gamePid2", GamePid2 }, { "relayAvailable", File.Exists(Relay) },
                { "runtimeRunning", RuntimeAlive }, { "connected", Connected }, { "ping", Ping }, { "loss", Loss }, { "problem", Problem },
                { "native", true }, { "saveLoaded", SaveLoaded }, { "lines", l }, { "recent", new List<string>(Recent) }, { "names", NamesSnapshot() },
                { "world", World }, { "room", Room }, { "tiled", tiled }, { "partyMe", PartyMe }, { "partyPeer", PartyPeer }
            };
            if (TestKit)
            {
                var sw = new Dictionary<string, object>();
                foreach (var t in TestSwitches) sw[t.Id] = SwitchOn(t);
                sw["saveLock"] = SaveLock;
                d["test"] = new Dictionary<string, object> { { "switches", sw }, { "god", TestGod }, { "mp", TestMp }, { "busy", testBusy > 0 } };
            }
            return json.Serialize(d);
        }

        // ---- Steam names
        Dictionary<string, object> NamesSnapshot()
        {
            var d = new Dictionary<string, object>();
            lock (gate) foreach (var kv in Names) d[kv.Key] = new Dictionary<string, object>(kv.Value);
            return d;
        }
        // Looks a SteamID up once a day (or when unknown); the page re-renders when the name arrives.
        public void ResolveName(string id)
        {
            if (!Regex.IsMatch(id ?? "", @"^\d{17}$")) return;
            lock (gate)
            {
                Dictionary<string, object> have;
                object at;
                if (Names.TryGetValue(id, out have) && have.TryGetValue("at", out at))
                {
                    long t; if (long.TryParse(at.ToString(), out t) && DateTime.UtcNow.Ticks - t < TimeSpan.TicksPerDay) return;
                }
                if (!resolving.Add(id)) return;
            }
            Task.Factory.StartNew(() =>
            {
                string name = null, avatar = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "ConnectedHearts launcher";
                        string xml = wc.DownloadString("https://steamcommunity.com/profiles/" + id + "?xml=1");
                        var m = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.Singleline);
                        if (m.Success) name = m.Groups[1].Value.Trim();
                        m = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.Singleline);
                        if (m.Success) avatar = m.Groups[1].Value.Trim();
                    }
                }
                catch { }
                lock (gate)
                {
                    resolving.Remove(id);
                    Dictionary<string, object> e;
                    if (!Names.TryGetValue(id, out e)) Names[id] = e = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(name)) e["name"] = name;
                    if (!string.IsNullOrEmpty(avatar)) e["avatar"] = avatar;
                    e["at"] = DateTime.UtcNow.Ticks;
                }
                SaveSettings();
            });
        }
        public void ResolveKnownNames()
        {
            foreach (var id in new List<string>(Recent)) ResolveName(id);
            if (MyId.Length == 17) ResolveName(MyId);
            if (HostId.Length == 17) ResolveName(HostId);
            if (FriendId.Length == 17) ResolveName(FriendId);
        }

        // ---- update
        public void CheckUpdate()
        {
            if (updateTask != null) return;
            UpdateText = "checking";
            updateTask = Task.Factory.StartNew(() =>
            {
                using (var wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "ConnectedHearts-Launcher"; wc.Headers["Cache-Control"] = "no-cache";
                    string bust = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string remote = wc.DownloadString(UpdateBase + "/version.txt" + bust).Trim();
                    string notes = ""; try { notes = wc.DownloadString(UpdateBase + "/dist/notes.txt" + bust).Trim(); } catch { }
                    return new[] { remote, notes };
                }
            });
        }
        void CompleteCheckUpdate(Task<string[]> t)
        {
            if (t.IsFaulted) { UpdateText = "update check failed"; Log("Update check failed: " + Inner(t.Exception)); return; }
            string remote = t.Result[0], notes = t.Result[1];
            if (!Regex.IsMatch(remote, @"^v?\d+(\.\d+)+$")) { UpdateText = "update check failed"; Log("Update check: unexpected version '" + remote + "'."); return; }
            string local = LocalVersion();
            if (!Newer(remote, local)) { Update = ""; UpdateText = "up to date"; Log("Up to date (" + local + ")."); return; }
            Update = remote.TrimStart(new[] { 'v' }); UpdateText = "update available"; Notes = notes;
            Log("Update " + remote + " available (you have " + local + ")."); if (notes.Length > 0) Log(notes);
        }
        public void InstallUpdate()
        {
            if (Update.Length == 0 || GameAlive || downloadTask != null) return;
            Updating = true; Log("Downloading " + Update + "...");
            downloadTask = Task.Factory.StartNew(() =>
            {
                using (var wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "ConnectedHearts-Launcher"; wc.Headers["Cache-Control"] = "no-cache";
                    string bust = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string tmp = Path.Combine(Path.GetTempPath(), "connected-hearts-update-" + Guid.NewGuid().ToString("N") + ".zip");
                    wc.DownloadFile(UpdateBase + "/dist/connected-hearts-update.zip" + bust, tmp);
                    string stage = Path.Combine(Path.GetTempPath(), "connected-hearts-stage-" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(tmp, stage);
                    try { File.Delete(tmp); } catch { }
                    return stage;
                }
            });
        }
        void CompleteInstallUpdate(Task<string> t)
        {
            Updating = false;
            if (t.IsFaulted) { Log("Update failed: " + Inner(t.Exception)); return; }
            string stage = t.Result;
            try
            {
                bool restart = false;
                foreach (var d in new[] { "bin", "ui", "lib", "licenses" })
                {
                    string src = Path.Combine(stage, d); if (!Directory.Exists(src)) continue;
                    bool changed = CopyDir(src, Path.Combine(Root, d));
                    if (changed && (d == "lib" || d == "ui")) restart = true;   // wrappers are loaded and the page is already shown; both need a restart
                }
                foreach (var f in new[] { "version.txt", "CONNECTED-HEARTS-PACKAGE", "package.json", "README.md", "ConnectedHearts.exe.config" })
                {
                    string src = Path.Combine(stage, f); if (File.Exists(src)) ReplaceFile(src, Path.Combine(Root, f));
                }
                string newExe = Path.Combine(stage, "ConnectedHearts.exe"), me = Path.Combine(Root, "ConnectedHearts.exe");
                if (File.Exists(newExe) && !SameFile(newExe, me)) { ReplaceFile(newExe, me); restart = true; }
                try { Directory.Delete(stage, true); } catch { }
                Update = ""; UpdateText = "up to date";
                Log("Updated to " + LocalVersion() + ".");
                if (restart) { Log("Launcher updated, restarting..."); RestartAfterExit = true; if (Exit != null) Exit(); }
            }
            catch (Exception ex) { Log("Update failed: " + ex.Message); }
        }
        public Action Exit;
        // Copies a tree; returns true if any file actually changed.
        static bool CopyDir(string src, string dst)
        {
            bool changed = false;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(f));
                if (SameFile(f, target)) continue;
                ReplaceFile(f, target); changed = true;
            }
            foreach (var d in Directory.GetDirectories(src)) if (CopyDir(d, Path.Combine(dst, Path.GetFileName(d)))) changed = true;
            return changed;
        }
        // Overwrites dst with src. A file that is in use (a loaded DLL, the running exe) cannot be overwritten or
        // deleted, but it can be renamed: move it aside as *.old and delete that on the next start (CleanOld).
        static void ReplaceFile(string src, string dst)
        {
            try { File.Copy(src, dst, true); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string aside = dst + ".old";
            for (int i = 1; File.Exists(aside); i++) { try { File.Delete(aside); break; } catch { aside = dst + ".old" + i; } }
            File.Move(dst, aside);
            File.Copy(src, dst, true);
        }
        static bool SameFile(string a, string b)
        {
            try { if (!File.Exists(a) || !File.Exists(b)) return false; var x = File.ReadAllBytes(a); var y = File.ReadAllBytes(b); return x.SequenceEqual(y); } catch { return false; }
        }
        static string Inner(AggregateException e) { Exception x = e; while (x.InnerException != null) x = x.InnerException; return x.Message; }

        // ---- game
        public void StartGame()
        {
            string dir = (GameDir ?? "").Trim(new[] { '"' }).TrimEnd(new[] { '\\' });
            if (!File.Exists(Path.Combine(dir, GameExe))) { Problem = "Game folder is wrong"; Log("Game folder is wrong: " + GameExe + " not found in '" + dir + "'."); return; }
            foreach (var f in new[] { Loader, Runtime, Dll }) if (!File.Exists(f)) { Problem = "Files missing"; Log("Missing " + f + ". Check for updates."); return; }
            bool local = Mode == "local";
            if (local && !File.Exists(Relay)) { Problem = "Relay missing"; Log(@"Solo test needs bin\connected_hearts_relay.exe (the local relay). Check for updates."); return; }
            if (launchTask != null) return;
            Problem = ""; GameDir = dir; SaveSettings();
            var env = new Dictionary<string, string> { { "SteamAppId", "2552430" }, { "SteamGameId", "2552430" }, { "KH2COOP_PUPPET_TRACE", "1" }, { "KH2COOP_AVATAR_DIAG", "1" } };
            if (!local) env["KH2COOP_STEAM_BROKER"] = "1";
            env["KH2COOP_CAST_REPLAY"] = CastReplay ? "1" : "0";
            // The other player is always a second Sora (one-clone party).
            env["KH2COOP_PARTY_NATIVE"] = "1"; env["KH2COOP_NATIVE_SORA_PRIVATE_STATUS"] = "1"; env["KH2COOP_CLONE_NEUTRAL_INPUT"] = "1"; env["KH2COOP_ALLY_HIT"] = "1";
            // Sora 2 also joins rooms that have no friend seat (the prologue); enemies follow the host (phase 1 list).
            env["KH2COOP_FORCED_SEAT"] = "1"; env["KH2COOP_ENEMY_MIRROR"] = "1"; env["KH2COOP_SPAWN_PICK"] = "1";
            foreach (var kv in DevEnv) env[kv.Key] = kv.Value;
            bool saveLock = TestKit && SaveLock;
            if (TestKit)
            {
                env["KH2COOP_TEST_KIT"] = "1";
                string ds; if (DevEnv.TryGetValue("KH2COOP_DOWNED_SPIKE", out ds) && ds == "1") env["KH2COOP_DOWNED_SPIKE_FIXTURE"] = "1"; // Knock out / Revive now
                if (saveLock) Log("Save lock is on: in-game saves are not kept this session.");
            }
            if (DevEnv.Count > 0) Log("Test switches: " + string.Join(", ", DevEnv.Select(kv => kv.Key + "=" + kv.Value)));
            int count = local ? 2 : 1;
            if (!InstallLoader(dir)) return;
            BackupSaves();
            Log(local ? "Starting two copies of KH2 (solo test)..." : "Starting KH2...");
            Launching = true;
            launchTask = Task.Factory.StartNew(() =>
            {
                var all = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    // Saves are real. In the solo test both windows share one Steam save folder, so only
                    // window 1 (the host) writes it; window 2's saves go to a throwaway copy.
                    var envI = new Dictionary<string, string>(env);
                    if (i == 0 && !saveLock) envI["KH2COOP_SAVEGUARD"] = "0";
                    all.Add(LaunchOne(dir, envI));
                    if (i < count - 1) Thread.Sleep(3000);
                }
                return all.ToArray();
            });
        }
        // Copies the game's save folder aside before each start (newest 10 kept), in case a session goes wrong.
        void BackupSaves()
        {
            try
            {
                string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "KINGDOM HEARTS HD 1.5+2.5 ReMIX");
                if (!Directory.Exists(saves)) { Log("No save folder found to back up (" + saves + ")."); return; }
                string root = Path.Combine(Root, "save-backups");
                string dst = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                CopyTree(saves, dst);
                var old = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).Skip(10).ToList();
                foreach (var d in old) { try { d.Delete(true); } catch { } }
                Log("Saves backed up to save-backups\\" + Path.GetFileName(dst) + ".");
            }
            catch (Exception ex) { Log("Save backup failed: " + ex.Message); }
        }
        // ---- the game-folder loader
        // bin\winmm.dll is copied next to the game for a session. Windows loads it with the game; it passes
        // every winmm call to the system winmm.dll and loads the mod only when this launcher started KH2
        // (KH2COOP_MOD_DLL). It is removed again once no game from this launcher is running.
        const string LoaderTag = "connected-hearts-loader-v1";
        string loaderDir; int coreTicks;
        static bool IsOurLoader(string path)
        {
            try { var b = File.ReadAllBytes(path); var tag = System.Text.Encoding.ASCII.GetBytes(LoaderTag); for (int i = 0; i + tag.Length <= b.Length; i++) { int k = 0; while (k < tag.Length && b[i + k] == tag[k]) k++; if (k == tag.Length) return true; } } catch { }
            return false;
        }
        bool InstallLoader(string dir)
        {
            string dst = Path.Combine(dir, "winmm.dll");
            try
            {
                if (File.Exists(dst) && !IsOurLoader(dst))
                {
                    Problem = "Another mod uses winmm.dll";
                    Log("The game folder already has a winmm.dll from another mod. Connected Hearts won't replace it. Remove it to play co-op (" + dst + ").");
                    return false;
                }
                if (!SameFile(Loader, dst)) File.Copy(Loader, dst, true);
                loaderDir = dir;
                return true;
            }
            catch (Exception ex) { Problem = "Loader not installed"; Log("Could not put the loader in the game folder: " + ex.Message); return false; }
        }
        // Called on every tick and at start/exit: removes our winmm.dll once no game of ours runs.
        public void RemoveLoaderIfIdle()
        {
            string dir = loaderDir ?? (GameDir ?? "").Trim(new[] { '"' }).TrimEnd(new[] { '\\' });
            if (string.IsNullOrEmpty(dir) || Launching || GameAlive || Game2Alive) return;
            string dst = Path.Combine(dir, "winmm.dll");
            try { if (File.Exists(dst) && IsOurLoader(dst)) { File.Delete(dst); Log("Loader removed from the game folder."); } loaderDir = null; } catch { }
        }
        // Starts one KH2 with the session settings; the loader loads the mod at the game's entry point.
        string LaunchOne(string dir, Dictionary<string, string> env)
        {
            try
            {
                string stage = Path.Combine(Root, @"build\rig\dll");
                Directory.CreateDirectory(stage); Directory.CreateDirectory(Logs);
                foreach (var old in Directory.GetFiles(stage, "connected_hearts_*.dll")) { try { File.Delete(old); } catch { } } // in use while a game runs
                string dll = Path.Combine(stage, "connected_hearts_" + DateTime.Now.Ticks + ".dll"); // a copy, so updates can replace bin\
                File.Copy(Dll, dll, true);
                var psi = new ProcessStartInfo(Path.Combine(dir, GameExe)) { WorkingDirectory = dir, UseShellExecute = false };
                foreach (var k in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("KH2COOP_")).ToList()) psi.EnvironmentVariables.Remove(k);
                foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                psi.EnvironmentVariables["KH2COOP_MOD_DLL"] = dll;
                psi.EnvironmentVariables["KH2COOP_LOG_DIR"] = Logs;
                var p = Process.Start(psi);
                string log = Path.Combine(Logs, "connected_hearts_" + p.Id + ".log");
                var until = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < until)
                {
                    if (p.HasExited) return "{\"ok\":false,\"error\":\"KH2 exited during startup (code " + p.ExitCode + ")\"}";
                    string text = ""; try { if (File.Exists(log)) text = ReadShared(log); } catch { }
                    if (text.Contains("InputCollector hook installed")) return "{\"ok\":true,\"processId\":" + p.Id + "}";
                    if (text.Contains("ERROR")) return "{\"ok\":false,\"error\":\"the mod reported an error at startup; see the log\",\"processId\":" + p.Id + "}";
                    Thread.Sleep(250);
                }
                return "{\"ok\":false,\"error\":\"the mod did not start within 60 s (is the loader in the game folder?)\",\"processId\":" + p.Id + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + json.Serialize(ex.Message) + "}"; }
        }
        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }
        string[] RunCapture(string exe, string args, Dictionary<string, string> env)
        {
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var k in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("KH2COOP_")).ToList()) psi.EnvironmentVariables.Remove(k);
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd(); p.WaitForExit();
                return (o + "\n" + e).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        void CompleteStartGame(Task<string[]> t)
        {
            Launching = false;
            if (t.IsFaulted) { Problem = "Launch failed"; Log("Launch failed: " + Inner(t.Exception)); return; }
            var pids = new List<int>();
            foreach (var line in t.Result.Where(l => l.StartsWith("{")))
            {
                try { var j = json.Deserialize<Dictionary<string, object>>(line); object ok, pid; if (j.TryGetValue("ok", out ok) && ok is bool && (bool)ok && j.TryGetValue("processId", out pid)) pids.Add(Convert.ToInt32(pid)); } catch { }
            }
            if (pids.Count == 0) { Problem = "Launch failed"; Log("Launch failed: " + string.Join(" ", t.Result)); return; }
            GamePid = pids[0]; MyId = "";
            if (TestKit) RegisterOwned(pids);
            if (Mode == "local")
            {
                if (pids.Count < 2) { Problem = "Second game failed"; Log("Second KH2 did not start: " + string.Join(" ", t.Result)); return; }
                GamePid2 = pids[1]; MyId = "local";
                Log("Two games started (PIDs " + GamePid + ", " + GamePid2 + "). Load a save in both, then press Connect both.");
            }
            else Log("KH2 started (PID " + GamePid + "). Waiting for the Steam session (load to the title screen)...");
        }
        // The DLL logs "[field] save loaded" once its player is in a field; Connect waits for it.
        void PollField()
        {
            Func<int, bool> loaded = pid =>
            {
                string f = Path.Combine(Logs, "connected_hearts_" + pid + ".log");
                if (!File.Exists(f)) return false;
                try { return ReadShared(f).Contains("[field] save loaded"); } catch { return false; }
            };
            bool before = SaveLoaded;
            if (GamePid != 0 && fieldPid != GamePid && loaded(GamePid)) fieldPid = GamePid;
            if (GamePid2 != 0 && fieldPid2 != GamePid2 && loaded(GamePid2)) fieldPid2 = GamePid2;
            if (!before && SaveLoaded) { Log(Mode == "local" ? "Both saves loaded. Press Connect both." : "Save loaded. You can connect now."); if (TestKit && (TestGod || TestMp)) ApplyTestFlags(); }
        }
        void PollSteamId()
        {
            if (Mode == "local" || !GameAlive || MyId.Length > 0) return;
            string bl = Path.Combine(Logs, "steam-broker_" + GamePid + ".log");
            if (!File.Exists(bl)) return;
            string t; try { t = ReadShared(bl); } catch { return; }
            var m = Regex.Match(t, @"ready appId=2552430 identity=(\d{17})");
            if (m.Success) { MyId = m.Groups[1].Value; Log("Steam session ready. Your SteamID: " + MyId); ResolveName(MyId); return; }
            var f = Regex.Match(t, @"\[steam-broker\] (unavailable|refused|exception)[^\r\n]*");
            if (f.Success && Problem != "Steam session failed") { Problem = "Steam session failed"; Log("Steam session failed: " + f.Value); }
        }

        // ---- co-op runtime
        string WriteIni()
        {
            string ini = Path.Combine(Root, @"build\rig\private-join\runtime.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(ini));
            File.WriteAllText(ini, "game_build=1.0.0.10-steam-global\r\ncontent_hash=none\r\nmod_hash=none", Encoding.ASCII);
            Directory.CreateDirectory(Logs);
            return ini;
        }
        Process StartHidden(string exe, string args, string logPath, bool parse)
        {
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.EnvironmentVariables["KH2COOP_AVATAR_DIAG"] = "1";
            var w = new StreamWriter(logPath, false) { AutoFlush = true };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler h = delegate(object s, DataReceivedEventArgs e) { if (e.Data == null) return; lock (w) { try { w.WriteLine(e.Data); } catch { } } if (parse) ParseRuntimeLine(e.Data); };
            p.OutputDataReceived += h; p.ErrorDataReceived += h;
            p.Exited += delegate { lock (w) { try { w.Dispose(); } catch { } } };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
            return p;
        }
        static readonly Dictionary<int, string> WorldNames = new Dictionary<int, string>
        {
            { 0x01, "End of Sea" }, { 0x02, "Twilight Town" }, { 0x03, "Destiny Islands" }, { 0x04, "Hollow Bastion" }, { 0x05, "Beast's Castle" },
            { 0x06, "Olympus Coliseum" }, { 0x07, "Agrabah" }, { 0x08, "Land of Dragons" }, { 0x09, "100 Acre Wood" }, { 0x0A, "Pride Lands" },
            { 0x0B, "Atlantica" }, { 0x0C, "Disney Castle" }, { 0x0D, "Timeless River" }, { 0x0E, "Halloween Town" }, { 0x0F, "World map" },
            { 0x10, "Port Royal" }, { 0x11, "Space Paranoids" }, { 0x12, "The World That Never Was" }
        };
        static readonly Dictionary<int, string> RoomNames = new Dictionary<int, string>
        {
            { 0x0015, "DIVETOHEART" }, { 0x0019, "GBA1" }, { 0x001A, "GBA2" }, { 0x001F, "GBA3" },
            { 0x0028, "FM(1)" }, { 0x0029, "FM(2)" }, { 0x002A, "FM(3)" }, { 0x002B, "FM(4)" },
            { 0x002C, "FM(5)" }, { 0x002D, "FM(6)" }, { 0x002F, "FM(7)" }, { 0x0030, "FM(8)" },
            { 0x0031, "FM(9)" }, { 0x0032, "COM(1)" }, { 0x0033, "COM(2)" }, { 0x0034, "COM(3)" },
            { 0x0035, "COM(4)" }, { 0x0036, "COM(5)" }, { 0x0037, "COM(6)" }, { 0x0038, "COM(7)" },
            { 0x0039, "COM(8)" }, { 0x003A, "COM(9)" }, { 0x003B, "COM(10)" }, { 0x003C, "COM(11)" },
            { 0x003D, "COM(12)" }, { 0x003E, "COM(13)" }, { 0x003F, "COM(14)" }, { 0x0100, "The Dark Margin" },
            { 0x0101, "loop demo" }, { 0x0200, "The Empty Realm" }, { 0x0201, "Roxas's Room" }, { 0x0202, "The Usual Spot" },
            { 0x0203, "Back Alley" }, { 0x0204, "Sandlot" }, { 0x0205, "Sandlot" }, { 0x0206, "Market Street: Station Heights" },
            { 0x0207, "Market Street: Tram Common" }, { 0x0208, "Station Plaza" }, { 0x0209, "Central Station" }, { 0x020A, "Sunset Terrace" },
            { 0x020B, "Sunset Station" }, { 0x020C, "Sunset Hill" }, { 0x020D, "The Woods" }, { 0x020E, "The Old Mansion" },
            { 0x020F, "Mansion: Foyer" }, { 0x0210, "Mansion: Dining Room" }, { 0x0211, "Mansion: Library" }, { 0x0212, "Mansion: The White Room" },
            { 0x0213, "Mansion: Basement Hall" }, { 0x0214, "Mansion: Basement Hall" }, { 0x0215, "Mansion: Computer Room" }, { 0x0216, "Mansion: Basement Corridor" },
            { 0x0217, "Mansion: Pod Room" }, { 0x0218, "On the Train" }, { 0x0219, "The Tower" }, { 0x021A, "Tower: Entryway" },
            { 0x021B, "Tower: Sorcerer's Loft" }, { 0x021C, "Tower: Wardrobe" }, { 0x021D, "Tower: Star Chamber" }, { 0x021E, "Tower: Moon Chamber" },
            { 0x021F, "Tower: Wayward Stairs" }, { 0x0220, "Station of Serenity" }, { 0x0221, "Station of Calling" }, { 0x0222, "Station of Awakening" },
            { 0x0223, "The Mysterial Train" }, { 0x0224, "Tunnelway" }, { 0x0225, "Underground Concourse" }, { 0x0226, "Tower: Wayward Stairs" },
            { 0x0227, "Tower: Wayward Stairs" }, { 0x0228, "Betwixt and Between" }, { 0x0229, "The Old Mansion" }, { 0x0300, "Beach" },
            { 0x0301, "Main Island: Ocean's Road" }, { 0x0302, "Main Island: Shore" }, { 0x0400, "Villain's Vale" }, { 0x0401, "The Dark Depths" },
            { 0x0402, "The Great Maw" }, { 0x0403, "Crystal Fissure" }, { 0x0404, "Castle Gate" }, { 0x0405, "Ansem's Study" },
            { 0x0406, "Postern" }, { 0x0407, "Restoration Site" }, { 0x0408, "Bailey" }, { 0x0409, "Borough" },
            { 0x040A, "Marketplace" }, { 0x040B, "Corridors" }, { 0x040C, "Heartless Manufactory" }, { 0x040D, "Merlin's House" },
            { 0x040E, "Castle Oblivion" }, { 0x040F, "Ansem's Study" }, { 0x0410, "Ravine Trail" }, { 0x0411, "The Great Maw" },
            { 0x0412, "Restoration Site" }, { 0x0413, "Bailey" }, { 0x0414, "Corridors" }, { 0x0415, "Cavern of Remembrance: Depths" },
            { 0x0416, "Cavern of Remembrance: Mining Area" }, { 0x0417, "Cavern of Remembrance: Engine Chamber" }, { 0x0418, "Cavern of Remembrance: Mineshaft" }, { 0x0419, "Transport to Remembrance" },
            { 0x041A, "Garden of Assemblage" }, { 0x041B, "Underground Facility" }, { 0x041C, "Memory's Contortion" }, { 0x041D, "The World of Nothing" },
            { 0x041E, "Hall of Empty Melodies" }, { 0x041F, "Bridge" }, { 0x0420, "The Old Mansion" }, { 0x0421, "Station of Remembrance" },
            { 0x0422, "Destiny Island" }, { 0x0423, "Addled Impasse" }, { 0x0424, "Mansion: Basement Hall" }, { 0x0425, "Havoc's Divide" },
            { 0x0426, "Station of Oblivion" }, { 0x0427, "Station of Awakening" }, { 0x0428, "Gathering Place" }, { 0x0500, "Entrance Hall" },
            { 0x0501, "Parlor" }, { 0x0502, "Belle's Room" }, { 0x0503, "The Beast's Room" }, { 0x0504, "Ballroom" },
            { 0x0505, "Ballroom" }, { 0x0506, "Courtyard" }, { 0x0507, "The East Wing" }, { 0x0508, "The West Hall" },
            { 0x0509, "The West Wing" }, { 0x050A, "Dungeon" }, { 0x050B, "Undercroft" }, { 0x050C, "Secret Passage" },
            { 0x050D, "Bridge" }, { 0x050E, "Ballroom" }, { 0x050F, "Bridge" }, { 0x0600, "The Coliseum" },
            { 0x0601, "Coliseum Gates" }, { 0x0602, "Coliseum Gates" }, { 0x0603, "Underworld Entrance" }, { 0x0604, "Coliseum Foyer" },
            { 0x0605, "Valley of the Dead" }, { 0x0606, "Hades' Chamber" }, { 0x0607, "Cave of the Dead: Entrance" }, { 0x0608, "Well of Captivity" },
            { 0x0609, "The Underdrome" }, { 0x060A, "Cave of the Dead: Inner Chamber" }, { 0x060B, "Underworld Caverns: Entrance" }, { 0x060C, "The Lock" },
            { 0x060D, "The Underdrome" }, { 0x060E, "Coliseum Gates" }, { 0x060F, "Cave of the Dead: Passage" }, { 0x0610, "Underworld Caverns: The Lost Road" },
            { 0x0611, "Underworld Caverns: Atrium" }, { 0x0612, "Coliseum Gates" }, { 0x0613, "The Underdrome" }, { 0x0700, "Agrabah" },
            { 0x0701, "Bazaar" }, { 0x0702, "The Peddler's Shop" }, { 0x0703, "The Palace" }, { 0x0704, "Vault" },
            { 0x0705, "Above Agrabah" }, { 0x0706, "Palace Walls" }, { 0x0707, "The Cave of Wonders: Entrance" }, { 0x0709, "The Cave of Wonders: Stone Guardians" },
            { 0x070A, "The Cave of Wonders: Treasure Room" }, { 0x070B, "Ruined Chamber" }, { 0x070C, "The Cave of Wonders: Valley of Stone" }, { 0x070D, "The Cave of Wonders: Chasm of Challenges" },
            { 0x070E, "Sandswept Ruins" }, { 0x070F, "The Peddler's Shop" }, { 0x0800, "Bamboo Grove" }, { 0x0801, "Encampment" },
            { 0x0802, "Checkpoint" }, { 0x0803, "Mountain Trail" }, { 0x0804, "Village" }, { 0x0805, "Village Cave" },
            { 0x0806, "Ridge" }, { 0x0807, "Summit" }, { 0x0808, "Imperial Square" }, { 0x0809, "Palace Gate" },
            { 0x080A, "Antechamber" }, { 0x080B, "Throne Room" }, { 0x080C, "Village" }, { 0x080D, "Palace Gate" },
            { 0x0900, "The Hundred Acre Wood" }, { 0x0901, "Starry Hill" }, { 0x0902, "Pooh Bear's House" }, { 0x0903, "Rabbit's House" },
            { 0x0904, "Piglet's House" }, { 0x0905, "Kanga's House" }, { 0x0906, "A Windsday Tale" }, { 0x0907, "The Honey Hunt" },
            { 0x0908, "Blossom Valley" }, { 0x0909, "The Spooky Cave" }, { 0x0A00, "Pride Rock" }, { 0x0A01, "Stone Hollow" },
            { 0x0A02, "The King's Den" }, { 0x0A03, "Wildebeest Valley" }, { 0x0A04, "The Savannah" }, { 0x0A05, "Elephant Graveyard" },
            { 0x0A06, "Gorge" }, { 0x0A07, "Wastelands" }, { 0x0A08, "Jungle" }, { 0x0A09, "Oasis" },
            { 0x0A0A, "Pride Rock" }, { 0x0A0B, "Oasis" }, { 0x0A0C, "Overlook" }, { 0x0A0D, "Peak" },
            { 0x0A0E, "Scar's Darkness" }, { 0x0A0F, "The Savannah" }, { 0x0A10, "Wildebeest Valley" }, { 0x0B00, "Triton's Throne" },
            { 0x0B01, "Ariel's Grotto" }, { 0x0B02, "Undersea Courtyard" }, { 0x0B03, "Undersea Courtyard" }, { 0x0B04, "The Palace: Performance Hall" },
            { 0x0B05, "Sunken Ship" }, { 0x0B06, "The Shore" }, { 0x0B07, "The Shore" }, { 0x0B08, "The Shore" },
            { 0x0B09, "Wrath of the Sea" }, { 0x0B0A, "Wedding Ship" }, { 0x0C00, "Audience Chamber" }, { 0x0C01, "Library" },
            { 0x0C02, "Colonnade" }, { 0x0C03, "Courtyard" }, { 0x0C04, "The Hall of the Cornerstone" }, { 0x0C05, "The Hall of the Cornerstone" },
            { 0x0C06, "Gummi Hangar" }, { 0x0C07, "The Wilderness" }, { 0x0D00, "Cornerstone Hill" }, { 0x0D01, "Pier" },
            { 0x0D02, "Waterway" }, { 0x0D03, "Wharf" }, { 0x0D04, "Lilliput" }, { 0x0D05, "Building Site" },
            { 0x0D06, "Scene of the Fire" }, { 0x0D07, "Mickey's House" }, { 0x0D08, "Villain's Vale" }, { 0x0E00, "Halloween Town Square" },
            { 0x0E01, "Dr. Finklestein's Lab" }, { 0x0E02, "Graveyard" }, { 0x0E03, "Curly Hill" }, { 0x0E04, "Hinterlands" },
            { 0x0E05, "Yuletide Hill" }, { 0x0E06, "Candy Cane Lane" }, { 0x0E07, "Christmas Tree Plaza" }, { 0x0E08, "Santa's House" },
            { 0x0E09, "Toy Factory: Shipping and Receiving" }, { 0x0E0A, "Toy Factory: The Wrapping Room" }, { 0x0F00, "World Map" }, { 0x1000, "Rampart" },
            { 0x1001, "Harbor" }, { 0x1002, "Town" }, { 0x1003, "The Interceptor" }, { 0x1004, "The Interceptor: Ship's Hold" },
            { 0x1005, "The Black Pearl" }, { 0x1006, "The Black Pearl: Captain's Stateroom" }, { 0x1007, "The Interceptor" }, { 0x1008, "Isla de Muerta: Rock Face" },
            { 0x1009, "Isla de Muerta: Cave Mouth" }, { 0x100A, "Isla de Muerta: Treasure Heap" }, { 0x100B, "Ship Graveyard: The Interceptor's Hold" }, { 0x100C, "Isla de Muerta: Powder Store" },
            { 0x100D, "Isla de Muerta: Moonlight Nook" }, { 0x100E, "Ship Graveyard: Seadrift Keep" }, { 0x100F, "Ship Graveyard: Seadrift Row" }, { 0x1010, "Isla de Muerta: Rock Face" },
            { 0x1011, "Isla de Muerta: Treasure Heap" }, { 0x1012, "The Black Pearl" }, { 0x1013, "The Black Pearl" }, { 0x1014, "The Black Pearl" },
            { 0x1015, "The Interceptor" }, { 0x1016, "The Interceptor" }, { 0x1017, "The Black Pearl: Captain's Stateroom" }, { 0x1018, "Harbor" },
            { 0x1019, "Isla de Muerta: Rock Face" }, { 0x1100, "Pit Cell" }, { 0x1101, "Canyon" }, { 0x1102, "Game Grid" },
            { 0x1103, "Dataspace" }, { 0x1104, "I/O Tower: Hallway" }, { 0x1105, "I/O Tower: Communications Room" }, { 0x1106, "Simulation Hangar" },
            { 0x1107, "Solar Sailer Simulation" }, { 0x1108, "Central Computer Mesa" }, { 0x1109, "Central Computer Core" }, { 0x110A, "Solar Sailer Simulation" },
            { 0x110B, "Solar Sailer Simulation" }, { 0x1200, "Where Nothing Gathers" }, { 0x1201, "Alley to Between" }, { 0x1202, "Fragment Crossing" },
            { 0x1203, "Memory's Skyscraper" }, { 0x1204, "The Brink of Despair" }, { 0x1205, "The Soundless Prison" }, { 0x1206, "Nothing's Call" },
            { 0x1207, "Crooked Ascension" }, { 0x1208, "Crooked Ascension" }, { 0x1209, "Twilight's View" }, { 0x120A, "Hall of Empty Melodies" },
            { 0x120B, "Hall of Empty Melodies" }, { 0x120C, "Naught's Skyway" }, { 0x120D, "Proof of Existence" }, { 0x120E, "Havoc's Divide" },
            { 0x120F, "Addled Impasse" }, { 0x1210, "Naught's Approach" }, { 0x1211, "Ruin and Creation's Passage" }, { 0x1212, "The Altar of Naught" },
            { 0x1213, "Memory's Contortion" }, { 0x1214, "The World of Nothing" }, { 0x1215, "Station of Awakening" }, { 0x1216, "The World of Nothing" },
            { 0x1217, "The World of Nothing" }, { 0x1218, "The World of Nothing" }, { 0x1219, "The World of Nothing" }, { 0x121A, "The World of Nothing" },
            { 0x121B, "The World of Nothing" }, { 0x121C, "The World of Nothing" }, { 0x121D, "The World of Nothing" },
        };
        static readonly Dictionary<int, string> Keyblades = new Dictionary<int, string>
        {
            { 41, "Kingdom Key" }, { 42, "Oathkeeper" }, { 43, "Oblivion" }, { 480, "Star Seeker" }, { 481, "Hidden Dragon" },
            { 484, "Hero's Crest" }, { 485, "Monochrome" }, { 486, "Follow the Wind" }, { 487, "Circle of Life" },
            { 488, "Photon Debugger" }, { 489, "Gull Wing" }, { 490, "Rumbling Rose" }, { 491, "Guardian Soul" },
            { 492, "Wishing Lamp" }, { 493, "Decisive Pumpkin" }, { 494, "Sleeping Lion" }, { 495, "Sweet Memories" },
            { 496, "Mysterious Abyss" }, { 497, "Fatal Crest" }, { 498, "Bond of Flame" }, { 499, "Fenrir" },
            { 500, "Ultima Weapon" }, { 543, "Two Become One" }, { 544, "Winner's Proof" },
        };
        // "lv=12 hp=40/40 mp=100/100 kb=41 world=2 room=5 paused=0" -> card fields; null for "-".
        static Dictionary<string, object> PartyStats(string part)
        {
            var m = Regex.Match(part, @"lv=(\d+) hp=(-?\d+)/(-?\d+) mp=(-?\d+)/(-?\d+) kb=(\d+) world=(\d+) room=(\d+) paused=(\d)");
            if (!m.Success) return null;
            Func<int, int> g = i => int.Parse(m.Groups[i].Value);
            int w = g(7), r = g(8), kb = g(6);
            string name;
            var d = new Dictionary<string, object>();
            if (g(1) > 0) d["level"] = g(1);
            if (g(3) > 0) { d["hp"] = g(2); d["maxHp"] = g(3); }
            if (g(5) > 0) { d["mp"] = g(4); d["maxMp"] = g(5); }
            if (kb != 0) d["keyblade"] = Keyblades.TryGetValue(kb, out name) ? name : "Keyblade";
            if (w != 0 || r != 0)
            {
                d["world"] = WorldNames.TryGetValue(w, out name) ? name : "World " + w.ToString("X2");
                d["room"] = RoomNames.TryGetValue((w << 8) | r, out name) ? name : "Room " + r.ToString("X2");
            }
            d["paused"] = g(9) == 1;
            d["worldId"] = w;
            return d;
        }
        void ParseRuntimeLine(string line)
        {
            var party = Regex.Match(line, @"^\[Party\] me (.*) \| peer (.*)$");
            if (party.Success) { PartyMe = PartyStats(party.Groups[1].Value); PartyPeer = PartyStats(party.Groups[2].Value); return; }
            var rs = Regex.Match(line, @"Room state: world=(\d+) room=(\d+)");
            if (rs.Success)
            {
                int w = int.Parse(rs.Groups[1].Value), r = int.Parse(rs.Groups[2].Value);
                string name; World = WorldNames.TryGetValue(w, out name) ? name : "World " + w.ToString("X2"); WorldId = w;
                Room = RoomNames.TryGetValue((w << 8) | r, out name) ? name : "Room " + r.ToString("X2");
                return;
            }
            if (Regex.IsMatch(line, @"Verified membership|SessionState .*actors=[2-9]")) { if (!Connected) Log("Connected."); Connected = true; Problem = ""; }
            else
            {
                var m = Regex.Match(line, @"Net: rtt=(\d+)ms.*?loss=([\d.]+)%");
                if (m.Success) { Connected = true; Ping = int.Parse(m.Groups[1].Value); Loss = m.Groups[2].Value; }
                else if (Regex.IsMatch(line, @"Networking stopped|Failed to create ENet|Steam host refused|refused by relay|bad-host-allowlist")) { Problem = "Connection problem"; Log(line); }
                else if (Regex.IsMatch(line, @"^\[Runtime\] (Room state:|Steam identity|Rejoin|Steam)|\[NetworkClient\]|refused|Failed|error")) Log(line);
            }
        }
        public void StartRuntime(string peer)
        {
            if (Mode == "local") { StartLocal(); return; }
            if (!GameAlive || MyId.Length == 0) { Log("Start the game first and wait for your SteamID."); return; }
            if (!SaveLoaded) { Log("Load your save first, then connect."); return; }
            peer = (peer ?? "").Trim();
            bool isHost = Mode != "join";
            // A host with no SteamID entered is open to any of its Steam friends (they can come in through Discord's Join).
            bool openToFriends = isHost && peer.Length == 0;
            if (!openToFriends && !Regex.IsMatch(peer, @"^\d{17}$")) { Log(isHost ? "Enter your friend's 17-digit SteamID, or leave it empty to let any Steam friend join." : "Enter the host's 17-digit SteamID."); return; }
            if (peer == MyId) { Log("That is your own SteamID. Enter the other player's."); return; }
            if (!openToFriends)
            {
                if (isHost) FriendId = peer; else HostId = peer;
                Recent.Remove(peer); Recent.Insert(0, peer); if (Recent.Count > 6) Recent.RemoveRange(6, Recent.Count - 6);
                ResolveName(peer);
            }
            SaveSettings(); ResolveName(MyId);
            string ini = WriteIni();
            string desync = Path.Combine(Root, @"build\rig\steam-desync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string role = isHost ? "player" : "friend1";
            string steamArgs = !isHost ? "--steam-join " + peer : openToFriends ? "--steam-host --steam-friends" : "--steam-host --steam-allow " + peer;
            string args = "--config \"" + ini + "\" --mode campaign_coop --network " + steamArgs + " --pid " + GamePid + " --role " + role + " --peer-id " + role + " --no-camera --tick-ms 16 --max-ticks 112500 --desync-dir \"" + desync + "\"";
            Connected = false; Ping = null; Loss = null; Problem = "";
            runtimeA = StartHidden(Runtime, args, Path.Combine(Logs, "runtime_" + GamePid + "_" + DateTime.Now.ToString("HHmmss") + ".log"), true);
            Log((openToFriends ? "Hosting for your Steam friends. " : isHost ? "Hosting; allowing " + peer + ". " : "Joining " + peer + ". ") + "Waiting for the other player...");
        }
        void StartLocal()
        {
            if (!GameAlive || !Game2Alive) { Log("Both games must be running. Press Start first."); return; }
            if (!SaveLoaded) { Log("Load a save in both games first."); return; }
            string ini = WriteIni(); string stamp = DateTime.Now.ToString("HHmmss");
            Connected = false; Ping = null; Loss = null; Problem = "";
            relay = StartHidden(Relay, "--port " + RelayPort + " --bind 127.0.0.1 --build 1.0.0.10-steam-global --content none --mod none", Path.Combine(Logs, "relay_" + stamp + ".log"), false);
            Thread.Sleep(800);
            if (!Alive(relay)) { Problem = "Relay failed"; Log("Local relay exited at once; see build\\rig\\logs\\relay_" + stamp + ".log"); return; }
            string common = "--config \"" + ini + "\" --mode campaign_coop --network --server 127.0.0.1 --port " + RelayPort + " --no-camera --tick-ms 16 --max-ticks 112500";
            runtimeA = StartHidden(Runtime, common + " --pid " + GamePid + " --role player --peer-id host", Path.Combine(Logs, "runtime_" + GamePid + "_" + stamp + ".log"), true);
            Thread.Sleep(1200);
            runtimeB = StartHidden(Runtime, common + " --pid " + GamePid2 + " --role friend1 --peer-id friend", Path.Combine(Logs, "runtime_" + GamePid2 + "_" + stamp + ".log"), false);
            Log("Local relay on 127.0.0.1:" + RelayPort + "; host runtime on PID " + GamePid + ", friend runtime on PID " + GamePid2 + ".");
        }
        public void StopRuntime()
        {
            bool was = RuntimeAlive;
            foreach (var p in new[] { runtimeA, runtimeB, relay }) { if (Alive(p)) { try { p.Kill(); } catch { } } }
            runtimeA = runtimeB = relay = null; Connected = false; Ping = null; Loss = null; World = ""; Room = ""; WorldId = -1; PartyMe = PartyPeer = null;
            if (was) Log("Disconnected.");
        }
        public void CloseGame()
        {
            StopRuntime();
            foreach (var pid in new[] { GamePid, GamePid2 }) if (Alive(pid)) { try { Process.GetProcessById(pid).Kill(); } catch { } }
            GamePid = GamePid2 = 0; MyId = ""; Problem = "";
            Log("Game closed.");
        }

        // ---- commands from the page
        public void Handle(Dictionary<string, object> c, Form owner)
        {
            object v; string cmd = c.TryGetValue("cmd", out v) && v != null ? v.ToString() : "";
            Func<string, string> str = k => c.TryGetValue(k, out v) && v != null ? v.ToString() : null;
            switch (cmd)
            {
                case "start":
                    if (str("gameDir") != null) GameDir = str("gameDir");
                    SaveSettings(); if (!GameAlive && !Launching) StartGame(); break;
                case "setGameDir": if (!GameAlive && str("gameDir") != null) { GameDir = str("gameDir").Trim(new[] { '"' }); SaveSettings(); } break;
                case "setMode": if (!RuntimeAlive && !GameAlive) { string m = str("mode"); Mode = (m == "join" || m == "local") ? m : "host"; SaveSettings(); } break;
                case "setPeer": if (!RuntimeAlive) { string pid = (str("peerId") ?? "").Trim(); if (Mode == "join") HostId = pid; else if (Mode != "local") FriendId = pid; SaveSettings(); ResolveName(pid); } break;
                case "pasteId":
                    if (!RuntimeAlive)
                    {
                        string clip = ""; try { clip = Clipboard.GetText() ?? ""; } catch { }
                        var pm = Regex.Match(clip, @"\b(7656\d{13})\b");
                        if (!pm.Success) { Log("No SteamID on the clipboard (17 digits starting with 7656)."); break; }
                        if (Mode == "join") HostId = pm.Value; else if (Mode != "local") FriendId = pm.Value;
                        SaveSettings(); ResolveName(pm.Value);
                    }
                    break;
                case "browse":
                    using (var d = new FolderBrowserDialog { Description = "Choose the folder that contains " + GameExe })
                    { if (GameDir.Length > 0) d.SelectedPath = GameDir; if (d.ShowDialog(owner) == DialogResult.OK) { GameDir = d.SelectedPath; SaveSettings(); } }
                    break;
                case "connect": if (!RuntimeAlive) StartRuntime(str("peerId")); break;
                case "disconnect": StopRuntime(); break;
                case "closeGame": CloseGame(); break;
                case "copyId": if (MyId.Length > 0 && MyId != "local") { try { Clipboard.SetText(MyId); } catch { } } break;
                case "update": if (!GameAlive) CheckUpdate(); break;
                case "installUpdate": InstallUpdate(); break;
                case "openLogs": Directory.CreateDirectory(Logs); try { Process.Start("explorer.exe", Logs); } catch { } break;
                case "tile": if (GameAlive && Game2Alive) { int t = Native.Tile(GamePid, GamePid2, TileBorderless); tileTries = 0; if (t == 0) Log("Could not find both game windows yet; try again in a moment."); else { tiled = true; Log(t == 1 ? "Game windows tiled side by side." : "Game windows are already tiled. If a game stays fullscreen, set it to Windowed in its Config menu."); } } break;
                case "collectLogs": CollectLogs(); break;
                case "forgetRecent": { string id = str("peerId") ?? ""; Recent.Remove(id); SaveSettings(); break; }
                case "test": if (TestKit) HandleTest(c); break;
            }
        }

        // ---- Test tab (developer only; needs bin\chtest.exe)
        class TestSwitch { public string Id; public string[] Env; public string On; }
        // Off removes the variables (the mod's default). Env[0] decides whether it reads as on.
        static readonly TestSwitch[] TestSwitches =
        {
            new TestSwitch { Id = "mirrorAll", Env = new[] { "KH2COOP_ENEMY_MIRROR_ALL" }, On = "1" },
            new TestSwitch { Id = "targetBoth", Env = new[] { "KH2COOP_ENEMY_TARGET_REMOTE" }, On = "1" },
            new TestSwitch { Id = "fixEnemies", Env = new[] { "KH2COOP_ENEMY_POPULATION", "KH2COOP_ENEMY_POPULATION_SPAWN" }, On = "1" },
            new TestSwitch { Id = "ballOff", Env = new[] { "KH2COOP_BALL_SYNC" }, On = "0" },
            new TestSwitch { Id = "keepParty", Env = new[] { "KH2COOP_NO_COMPANION" }, On = "0" },
            new TestSwitch { Id = "downed", Env = new[] { "KH2COOP_DOWNED_SPIKE" }, On = "1" },
            new TestSwitch { Id = "revivePrompt", Env = new[] { "KH2COOP_REVIVE_PROMPT" }, On = "1" },
            new TestSwitch { Id = "spectate", Env = new[] { "KH2COOP_DOWNED_SPECTATE" }, On = "1" },
        };
        bool SwitchOn(TestSwitch t) { string v; return DevEnv.TryGetValue(t.Env[0], out v) && v == t.On; }

        // chtest's poke/watch/capture need the games registered as rig-owned (pid + creation time).
        void RegisterOwned(List<int> pids)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var pid in pids) { try { sb.Append(pid).Append(' ').Append(Process.GetProcessById(pid).StartTime.ToFileTime()).Append('\n'); } catch { } }
                string dir = Path.Combine(Root, @"build\rig"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "owned.txt"), sb.ToString(), Encoding.ASCII);
            }
            catch (Exception ex) { Log("Test: could not register the games: " + ex.Message); }
        }

        List<int> TestTargets(string target)
        {
            var l = new List<int>();
            if (target != "2" && GameAlive) l.Add(GamePid);
            if (target != "1" && Game2Alive) l.Add(GamePid2);
            return l;
        }
        string GameLabel(int pid) { return Mode == "local" ? (pid == GamePid ? "Game 1" : "Game 2") : "Game"; }

        // Runs chtest once per game in the background and logs each answer.
        void RunTest(string label, List<int> pids, Func<int, string> args)
        {
            if (pids.Count == 0) { Log("Test: no game running."); return; }
            foreach (var pid in pids)
            {
                int p = pid; string a = args(p);
                if (a == null) continue;
                Interlocked.Increment(ref testBusy);
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        string outText = string.Join(" ", RunCapture(TestTool, a + " --pid " + p, null));
                        var m = Regex.Match(outText, "\"error\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                        bool ok = outText.Contains("\"ok\":true");
                        var path = Regex.Match(outText, "\"path\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                        Log("Test: " + label + " on " + GameLabel(p) + (ok ? ": done" + (path.Success ? " (" + Path.GetFileName(path.Groups[1].Value.Replace("\\\\", "\\")) + ")" : "") : ": " + (m.Success ? m.Groups[1].Value : "failed")));
                    }
                    catch (Exception ex) { Log("Test: " + label + " failed: " + ex.Message); }
                    finally { Interlocked.Decrement(ref testBusy); }
                });
            }
        }

        void ApplyTestFlags()
        {
            RunTest(TestGod ? "God mode on" : "God mode off", TestTargets("both"), p => "test god " + (TestGod ? "on" : "off"));
            RunTest(TestMp ? "Infinite MP on" : "Infinite MP off", TestTargets("both"), p => "test mp " + (TestMp ? "on" : "off"));
        }

        static int? Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return null; s = s.Trim();
            int v;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v) : int.TryParse(s, out v)) return v;
            return null;
        }
        // The room a game is in, from its status file ("... world=W room=R ...").
        int[] RoomOf(int pid)
        {
            try
            {
                var m = Regex.Match(ReadShared(Path.Combine(Logs, "status_" + pid + ".txt")), @"world=(\d+) room=(\d+)");
                if (m.Success) return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) };
            }
            catch { }
            return null;
        }

        void HandleTest(Dictionary<string, object> c)
        {
            object v;
            Func<string, string> str = k => c.TryGetValue(k, out v) && v != null ? v.ToString() : null;
            Func<string, bool> flag = k => c.TryGetValue(k, out v) && v is bool && (bool)v;
            string action = str("action") ?? "", target = str("target") ?? "both";
            var pids = TestTargets(target);
            switch (action)
            {
                case "warp":
                {
                    int? w = Num(str("world")), r = Num(str("room")), b = Num(str("btl")), e = Num(str("evt"));
                    if (w == null || r == null) { Log("Test: warp needs a world and a room."); return; }
                    string extra = (b != null ? " --btl " + b : "") + (e != null ? " --evt " + e : "");
                    RunTest("Warp to " + w.Value.ToString("X2") + "/" + r.Value.ToString("X2") + extra, pids, p => "warp --world " + w + " --room " + r + extra + " --timeout-ms 20000");
                    break;
                }
                case "restartRoom":
                    RunTest("Restart room", pids, p => { var wr = RoomOf(p); if (wr == null) { Log("Test: " + GameLabel(p) + " has no room yet."); return null; } return "warp --world " + wr[0] + " --room " + wr[1] + " --timeout-ms 20000"; });
                    break;
                case "god": TestGod = flag("value"); SaveSettings(); RunTest(TestGod ? "God mode on" : "God mode off", TestTargets("both"), p => "test god " + (TestGod ? "on" : "off")); break;
                case "mp": TestMp = flag("value"); SaveSettings(); RunTest(TestMp ? "Infinite MP on" : "Infinite MP off", TestTargets("both"), p => "test mp " + (TestMp ? "on" : "off")); break;
                case "refill": RunTest("Refill HP and MP", pids, p => "test refill"); break;
                case "knockout": RunTest("Knock out", pids, p => "test knockout"); break;
                case "revive": RunTest("Revive now", pids, p => "test revive"); break;
                case "screenshot": RunTest("Screenshot", pids, p => "capture"); break;
                case "mark":
                {
                    string text = Regex.Replace(str("text") ?? "", "[^A-Za-z0-9 .,:/#()+-]", "").Trim();
                    if (text.Length == 0) text = "mark"; if (text.Length > 80) text = text.Substring(0, 80);
                    Log("Mark: " + text);
                    RunTest("Mark", TestTargets("both"), p => "test mark " + text);
                    break;
                }
                case "reconnect":
                    if (!RuntimeAlive && !Connected) { Log("Test: not connected."); return; }
                    StopRuntime(); reconnectAt = DateTime.Now.AddSeconds(Math.Max(1, Math.Min(30, Num(str("seconds")) ?? 3)));
                    Log("Test: dropped the connection; reconnecting in a moment.");
                    break;
                case "switch":
                {
                    string id = str("id") ?? ""; bool on = flag("value");
                    if (id == "saveLock") { SaveLock = on; SaveSettings(); Log("Test: save lock " + (on ? "on" : "off") + " (next start)."); return; }
                    var t = TestSwitches.FirstOrDefault(x => x.Id == id);
                    if (t == null) return;
                    foreach (var key in t.Env) { if (on) DevEnv[key] = t.On; else DevEnv.Remove(key); }
                    SaveSettings();
                    Log("Test: switch " + id + " " + (on ? "on" : "off") + (GameAlive ? " (applies the next time the games start)" : "") + ".");
                    break;
                }
            }
        }

        // Remote testing: JSON commands dropped in build\test\inbox run like the page's, oldest first.
        void PollTestInbox()
        {
            string inbox = Path.Combine(testDir, "inbox");
            if (!Directory.Exists(inbox)) return;
            foreach (var f in new DirectoryInfo(inbox).GetFiles("*.json").OrderBy(x => x.Name).Take(4))
            {
                string text = null;
                try { text = File.ReadAllText(f.FullName); File.Delete(f.FullName); } catch { continue; }
                try
                {
                    var d = json.Deserialize<Dictionary<string, object>>(text);
                    if (d == null) continue;
                    object cv; string name = d.TryGetValue("cmd", out cv) && cv != null ? cv.ToString() : "";
                    if (name == "browse" || name == "closeUi") continue;
                    Log("Remote: " + name + (d.ContainsKey("action") ? " " + d["action"] : ""));
                    Handle(d, null);
                }
                catch (Exception ex) { Log("Remote command failed: " + ex.Message); }
            }
        }

        // Zips the newest logs (plus settings and version) to the Desktop for a bug report.
        void CollectLogs()
        {
            try
            {
                Directory.CreateDirectory(Logs);
                string outPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ConnectedHearts-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                var files = new DirectoryInfo(Logs).GetFiles().Where(f => f.Length < 60L * 1024 * 1024).OrderByDescending(f => f.LastWriteTimeUtc).Take(40).ToList();
                using (var zip = ZipFile.Open(outPath, ZipArchiveMode.Create))
                {
                    foreach (var f in files)
                    {
                        var entry = zip.CreateEntry("logs/" + f.Name, CompressionLevel.Optimal);
                        using (var src = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var dst = entry.Open()) src.CopyTo(dst);
                    }
                    foreach (var name in new[] { "settings.json", "version.txt", "package.json" })
                    {
                        string pth = Path.Combine(Root, name); if (!File.Exists(pth)) continue;
                        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                        using (var src = new FileStream(pth, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var dst = entry.Open()) src.CopyTo(dst);
                    }
                    var info = zip.CreateEntry("launcher-activity.txt");
                    using (var wr = new StreamWriter(info.Open())) { List<string> l; lock (gate) l = new List<string>(lines); foreach (var ln in l) wr.WriteLine(ln); }
                }
                Log("Logs saved to the Desktop: " + Path.GetFileName(outPath));
                try { Process.Start("explorer.exe", "/select,\"" + outPath + "\""); } catch { }
            }
            catch (Exception ex) { Log("Collect logs failed: " + ex.Message); }
        }

        // ---- periodic housekeeping (UI thread)
        public void Tick()
        {
            try
            {
                if (updateTask != null && updateTask.IsCompleted) { var t = updateTask; updateTask = null; CompleteCheckUpdate(t); }
                if (downloadTask != null && downloadTask.IsCompleted) { var t = downloadTask; downloadTask = null; CompleteInstallUpdate(t); }
                if (launchTask != null && launchTask.IsCompleted) { var t = launchTask; launchTask = null; CompleteStartGame(t); }
                PollSteamId();
                PollField();
                if (TestKit) { PollTestInbox(); if (reconnectAt != DateTime.MinValue && DateTime.Now >= reconnectAt) { reconnectAt = DateTime.MinValue; if (!RuntimeAlive) StartRuntime(Mode == "join" ? HostId : FriendId); } }
                // Keep the two windows side by side for the first ~3 minutes: KH2 re-applies its own display mode
                // after its splash and again after the first load, which undoes a single early tile.
                if (Mode == "local" && GameAlive && Game2Alive && tileTries < 500) { tileTries++; int t = Native.Tile(GamePid, GamePid2, TileBorderless); if (t == 1 && !tiled) { tiled = true; Log("Game windows tiled side by side."); } }
                if (!GameAlive) { tiled = false; tileTries = 0; }
                if (runtimeA != null && !Alive(runtimeA)) { runtimeA = null; Connected = false; Ping = null; Loss = null; Log("Co-op program exited."); if (Alive(runtimeB) || Alive(relay)) StopRuntime(); }
                if (++coreTicks % 10 == 0) RemoveLoaderIfIdle();
                PollLocalStatus();
                UpdateDiscord();
                if (GamePid != 0 && !GameAlive) { Log("KH2 closed."); GamePid = 0; MyId = ""; StopRuntime(); if (Game2Alive) { try { Process.GetProcessById(GamePid2).Kill(); } catch { } } GamePid2 = 0; }
                else if (GamePid2 != 0 && !Game2Alive) { Log("Second KH2 closed."); GamePid2 = 0; StopRuntime(); }
            }
            catch (Exception ex) { Log("Error: " + ex.Message); }
        }
        public void Shutdown() { StopRuntime(); RemoveLoaderIfIdle(); discord.Set(null); discord.Flush(); }

        // ---- Discord status. World pictures are the KH wiki's KHII images, linked by URL (Discord fetches them).
        static readonly Dictionary<int, string> WorldArt = new Dictionary<int, string>
        {
            { 0x02, "9/93/Twilight_Town_KHIII.png" }, { 0x03, "5/58/Destiny_Islands_KHII.png" },
            { 0x04, "b/b3/Hollow_Bastion_KHII.png" }, { 0x05, "0/06/Beast%27s_Castle_KHII.png" },
            { 0x06, "8/82/Olympus_Coliseum_KHII.png" }, { 0x07, "3/31/Agrabah_KHII.png" },
            { 0x08, "4/45/The_Land_of_Dragons_KHII.png" }, { 0x09, "6/66/100_Acre_Wood_KHII.png" },
            { 0x0A, "2/24/Pride_Lands_KHII.png" }, { 0x0B, "c/c8/Atlantica_KHII.png" },
            { 0x0C, "b/bd/Disney_Castle_KHII.png" }, { 0x0D, "a/a0/Timeless_River_KHII.png" },
            { 0x0E, "9/92/Halloween_Town_KHII.png" }, { 0x0F, "8/80/GummiShip.png" }, { 0x10, "d/df/Port_Royal_KHII.png" },
            { 0x11, "5/5a/Space_Paranoids_KHII.png" }, { 0x12, "e/e6/The_World_That_Never_Was_KHII.png" },
        };
        // "logo": the KHII logo, uploaded as a Rich Presence art asset in the Discord app. The title-screen
        // picture stands in when no world is known (title screen, before the save loads).
        const string ArtBase = "https://kh.wiki.gallery/images/", LogoAsset = "logo", TitleArt = "e/e2/Opening_Menu_HD_KHIIFM.png";
        static string Clip(string s, int max) { return s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026"; }
        // Without a session the mod's own status file (status_<pid>.txt) gives this player's location and stats.
        void PollLocalStatus()
        {
            if (!GameAlive) { if (!RuntimeAlive) PartyMe = null; return; }
            if (RuntimeAlive) return; // the runtime's [Party] lines
            string f = Path.Combine(Logs, "status_" + GamePid + ".txt");
            try { if (File.Exists(f)) PartyMe = PartyStats(ReadShared(f)); } catch { }
        }
        // A Join pressed in Discord: become the joiner of that host, start the game if needed, and connect
        // once the save is loaded.
        void HandleDiscordJoin()
        {
            for (string note; (note = discord.TakeNote()) != null; ) Log(note);
            string secret = discord.TakeJoin();
            if (secret != null)
            {
                var m = Regex.Match(secret, @"^ch1:(\d{17})$");
                if (!m.Success || m.Groups[1].Value == MyId) { Log("Discord join ignored (not a Connected Hearts host)."); }
                else if (RuntimeAlive || Game2Alive) { Log("Discord join ignored: already in a session. Disconnect first."); }
                else
                {
                    string host = m.Groups[1].Value;
                    Mode = "join"; HostId = host; SaveSettings(); ResolveName(host);
                    pendingJoin = host;
                    Log("Discord: joining " + host + ". " + (GameAlive ? "Connecting once your save is loaded." : "Starting the game; load your save."));
                    if (!GameAlive && !Launching) StartGame();
                }
            }
            if (pendingJoin != null && GameAlive && SaveLoaded && MyId.Length > 0 && !RuntimeAlive)
            {
                string host = pendingJoin; pendingJoin = null;
                StartRuntime(host);
            }
        }
        void UpdateDiscord()
        {
            if (!DiscordStatus) return;
            if (!discord.Started) discord.Start();
            HandleDiscordJoin();
            if (!GameAlive) { sessionStart = 0; discord.Set(null); return; }
            if (sessionStart == 0) sessionStart = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            var me = PartyMe;
            object o;
            string world = me != null && me.TryGetValue("world", out o) ? (string)o : World;
            string room = me != null && me.TryGetValue("room", out o) ? (string)o : Room;
            int worldId = me != null && me.TryGetValue("worldId", out o) ? (int)o : WorldId;
            bool paused = me != null && me.TryGetValue("paused", out o) && (bool)o;
            int level = me != null && me.TryGetValue("level", out o) ? (int)o : 0;
            bool title = !SaveLoaded;
            if (title) { world = ""; room = ""; worldId = -1; paused = false; level = 0; }
            string details = title ? "Title screen" : !string.IsNullOrEmpty(world) ? world + (string.IsNullOrEmpty(room) ? "" : " \u00b7 " + room) : "Kingdom Hearts II Final Mix";
            // The runtime is "connected" to its own session even while hosting alone; only the other player's
            // stats in the [Party] line mean a friend is actually in.
            bool together = Connected && PartyPeer != null;
            string state = Mode == "local" ? "Solo test" : together ? "Co-op" : RuntimeAlive ? (Mode == "join" ? "Joining a friend" : "Waiting for a friend") : "Playing solo";
            if (level > 0) state += " \u00b7 Lv " + level;
            if (paused) state = "Paused \u00b7 " + state;
            string art;
            var assets = new Dictionary<string, object>();
            if (worldId >= 0 && WorldArt.TryGetValue(worldId, out art))
            {
                assets["large_image"] = ArtBase + art; assets["large_text"] = Clip(world, 128);
                assets["small_image"] = LogoAsset; assets["small_text"] = "Connected Hearts";
            }
            else
            {
                assets["large_image"] = ArtBase + TitleArt; assets["large_text"] = "Kingdom Hearts II Final Mix";
                assets["small_image"] = LogoAsset; assets["small_text"] = "Connected Hearts";
            }
            var activity = new Dictionary<string, object>
            {
                { "details", Clip(details, 128) }, { "state", Clip(state, 128) },
                { "timestamps", new Dictionary<string, object> { { "start", sessionStart } } }, { "assets", assets },
            };
            // Hosting with a free seat: a Join button (the secret is this player's SteamID, which the joiner needs anyway).
            bool hosting = Mode == "host" && RuntimeAlive && Regex.IsMatch(MyId ?? "", @"^\d{17}$");
            if (hosting || (together && Mode != "local"))
                activity["party"] = new Dictionary<string, object> { { "id", "ch-" + (hosting ? MyId : sessionStart.ToString()) }, { "size", new[] { together ? 2 : 1, 2 } } };
            if (hosting && !together)
                activity["secrets"] = new Dictionary<string, object> { { "join", "ch1:" + MyId } };
            discord.Set(json.Serialize(activity));
        }
    }

    // ------------------------------------------------------------------ Discord status
    // Discord's local IPC (named pipe discord-ipc-N): handshake (op 0) with the app id, then SET_ACTIVITY
    // frames (op 1). Frames are a little-endian op and length, then UTF-8 JSON. Runs on its own thread so a
    // missing or slow Discord never stalls the window; sends only changes, at most one every 5 s.
    class DiscordPresence
    {
        public const string ClientId = "1560828967324491876";
        readonly object sync = new object(), writeLock = new object();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        string want, sent = "\u0001";
        bool haveWant;
        DateTime lastSend = DateTime.MinValue, lastConnect = DateTime.MinValue;
        NamedPipeClientStream pipe;
        Thread thread;
        readonly int pid = Process.GetCurrentProcess().Id;
        readonly Queue<string> joins = new Queue<string>();
        public bool Started { get { return thread != null; } }
        public void Start() { thread = new Thread(Run) { IsBackground = true, Name = "discord-status" }; thread.Start(); }
        public void Set(string activityJson) { lock (sync) { want = activityJson; haveWant = true; } }
        // Join secrets Discord handed us (a friend pressed Join on our status, or we pressed it on theirs).
        public string TakeJoin() { lock (sync) return joins.Count > 0 ? joins.Dequeue() : null; }
        // Discord's refusals (an ERROR reply), for the Activity log.
        readonly Queue<string> notes = new Queue<string>();
        public string TakeNote() { lock (sync) return notes.Count > 0 ? notes.Dequeue() : null; }
        // On exit: clear the status now (Discord also clears it when the pipe closes).
        public void Flush() { try { if (pipe != null && pipe.IsConnected) SendActivity(null); } catch { } Close(); }

        // Discord starts "<exe> --discord" (protocol discord-<app id>) when its Join button is pressed and the
        // launcher is not open; the join secret then arrives over the pipe. Per user, no admin rights.
        public static void Register(string exe)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\discord-" + ClientId))
                {
                    key.SetValue("", "URL:Run game " + ClientId + " protocol");
                    key.SetValue("URL Protocol", "");
                    using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", exe);
                    using (var cmd = key.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" --discord");
                }
            }
            catch { }
        }

        void Run()
        {
            for (;;)
            {
                try
                {
                    if (Connect())
                    {
                        string w; bool has;
                        lock (sync) { w = want; has = haveWant; }
                        if (has && w != sent && (DateTime.UtcNow - lastSend).TotalSeconds >= 5)
                        {
                            SendActivity(w);
                            sent = w; lastSend = DateTime.UtcNow;
                        }
                    }
                }
                catch { Close(); }
                Thread.Sleep(1000);
            }
        }
        // Connected even with no game running: a Join pressed in Discord must reach the launcher.
        bool Connect()
        {
            if (pipe != null && pipe.IsConnected) return true;
            Close();
            if ((DateTime.UtcNow - lastConnect).TotalSeconds < 15) return false;
            lastConnect = DateTime.UtcNow;
            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(300);
                    pipe = p;
                    Write(0, "{\"v\":1,\"client_id\":\"" + ClientId + "\"}");
                    Read(); // READY
                    sent = "\u0001"; // a new connection starts empty
                    Write(1, "{\"cmd\":\"SUBSCRIBE\",\"evt\":\"ACTIVITY_JOIN\",\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}");
                    var reader = new Thread(ReadLoop) { IsBackground = true, Name = "discord-events" };
                    reader.Start(p);
                    return true;
                }
                catch { if (p != null) try { p.Dispose(); } catch { } pipe = null; }
            }
            return false;
        }
        // Everything Discord sends after READY: replies, pings, and the ACTIVITY_JOIN event.
        void ReadLoop(object state)
        {
            var p = (NamedPipeClientStream)state;
            try
            {
                for (;;)
                {
                    int op; string body = Read(p, out op);
                    if (op == 2) break;                       // close
                    if (op == 3) { Write(4, body); continue; } // ping -> pong
                    var d = json.Deserialize<Dictionary<string, object>>(body);
                    object cmd, evt, data, secret;
                    if (d != null && d.TryGetValue("cmd", out cmd) && (cmd as string) == "DISPATCH" &&
                        d.TryGetValue("evt", out evt) && (evt as string) == "ACTIVITY_JOIN" &&
                        d.TryGetValue("data", out data) && data is Dictionary<string, object> &&
                        ((Dictionary<string, object>)data).TryGetValue("secret", out secret) && secret is string)
                        lock (sync) joins.Enqueue((string)secret);
                    else if (d != null && d.TryGetValue("evt", out evt) && (evt as string) == "ERROR")
                    {
                        object c2; d.TryGetValue("cmd", out c2);
                        string msg = d.TryGetValue("data", out data) && data is Dictionary<string, object> && ((Dictionary<string, object>)data).ContainsKey("message")
                            ? Convert.ToString(((Dictionary<string, object>)data)["message"]) : body;
                        lock (sync) { if (notes.Count < 20) notes.Enqueue("Discord refused " + c2 + ": " + msg); }
                    }
                }
            }
            catch { }
            if (pipe == p) Close();
        }
        void SendActivity(string activityJson)
        {
            Write(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + pid + ",\"activity\":" + (activityJson ?? "null") + "},\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}");
        }
        void Write(int op, string payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            byte[] frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            lock (writeLock) { pipe.Write(frame, 0, frame.Length); pipe.Flush(); }
        }
        string Read() { int op; return Read(pipe, out op); }
        static string Read(Stream s, out int op)
        {
            byte[] head = ReadExact(s, 8);
            op = BitConverter.ToInt32(head, 0);
            int len = BitConverter.ToInt32(head, 4);
            if (len < 0 || len > (1 << 20)) throw new IOException("bad frame");
            return Encoding.UTF8.GetString(ReadExact(s, len));
        }
        static byte[] ReadExact(Stream s, int n)
        {
            byte[] b = new byte[n]; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new IOException("pipe closed"); got += r; }
            return b;
        }
        void Close() { var p = pipe; pipe = null; if (p != null) { try { p.Dispose(); } catch { } } }
    }

    // ------------------------------------------------------------------ window
    class MainForm : Form
    {
        readonly Core core; readonly bool noUpdate;
        Microsoft.Web.WebView2.WinForms.WebView2 web;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        System.Windows.Forms.Timer timer; int ticks;

        public MainForm(Core core, bool noUpdate)
        {
            this.core = core; this.noUpdate = noUpdate;
            core.Exit = delegate { BeginInvoke(new Action(Close)); };
            Text = "Connected Hearts"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColorTranslator.FromHtml("#2A1434"); ShowInTaskbar = true;
            float scale = 1f; try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; } catch { }
            ClientSize = new Size((int)(1000 * scale), (int)(640 * scale));
            string ico = Path.Combine(core.Root, @"ui\icon.ico"); if (File.Exists(ico)) { try { Icon = new Icon(ico); } catch { } }
            Load += OnLoad; Shown += OnShown;
        }

        void OnShown(object s, EventArgs e) { try { int pref = 2; Native.DwmSetWindowAttribute(Handle, 33, ref pref, 4); } catch { } }

        void OnLoad(object s, EventArgs e)
        {
            try
            {
                string lib = Path.Combine(core.Root, "lib");
                try { Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(lib); } catch { }
                web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
                var props = new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties { UserDataFolder = Path.Combine(core.Root, @"build\webview2") };
                Directory.CreateDirectory(props.UserDataFolder);
                web.CreationProperties = props;
                Controls.Add(web);
                web.CoreWebView2InitializationCompleted += OnWebReady;
                web.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The launcher window could not start:\n\n" + ex.Message + "\n\nMake sure the lib\\ folder is present and Microsoft Edge is installed.", "Connected Hearts", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        void OnWebReady(object s, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                MessageBox.Show("WebView2 could not start:\n\n" + e.InitializationException.Message + "\n\nInstall the Microsoft Edge WebView2 Runtime (it comes with Windows 11 and Edge).", "Connected Hearts", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close(); return;
            }
            var c = web.CoreWebView2;
            c.Settings.AreDefaultContextMenusEnabled = false; c.Settings.IsStatusBarEnabled = false; c.Settings.AreDevToolsEnabled = false; c.Settings.IsZoomControlEnabled = false;
            c.SetVirtualHostNameToFolderMapping("app.connectedhearts", Path.Combine(core.Root, "ui"), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            c.WebMessageReceived += OnMessage;
            c.Navigate("https://app.connectedhearts/index.html");
            timer = new System.Windows.Forms.Timer { Interval = 350 };
            timer.Tick += delegate
            {
                ticks++;
                if (ticks % 3 == 0) core.Tick();
                if (!noUpdate && ticks == 2) core.CheckUpdate();
                PushState();
            };
            timer.Start();
        }

        void PushState() { try { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(core.StateJson()); } catch { } }

        void OnMessage(object s, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;
            string str = null; try { str = e.TryGetWebMessageAsString(); } catch { }
            if (str == "drag") { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); return; }
            if (str == "minimize") { WindowState = FormWindowState.Minimized; return; }
            if (str == "close") { Close(); return; }
            try
            {
                var d = json.Deserialize<Dictionary<string, object>>(raw);
                if (d != null) { core.Handle(d, this); PushState(); }
            }
            catch (Exception ex) { core.Log("Error: " + ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (core.RuntimeAlive && !core.RestartAfterExit && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, "Closing the launcher disconnects co-op (the game stays open). Close?", "Connected Hearts", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }
    }
}
