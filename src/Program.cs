// KH2 Co-op launcher. Single-file C# (C# 5, .NET Framework 4.x) so it builds with the csc.exe that ships
// with Windows: see build.bat. Hosts ui\index.html in a borderless WebView2 window and runs the
// game/runtime/update logic. Layout next to the exe: bin\, ui\, lib\ (WebView2 wrappers), version.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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

namespace KH2Coop
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool DeleteFile(string name);
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

        // Remove the "downloaded from the internet" flag (Zone.Identifier stream) that makes .NET refuse to load DLLs.
        public static void Unblock(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { try { DeleteFile(f + ":Zone.Identifier"); } catch { } }
            }
            catch { }
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
            foreach (var d in new[] { "lib", "bin", "ui" }) Native.Unblock(Path.Combine(Root, d));
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name + ".dll";
                string p = Path.Combine(Path.Combine(Root, "lib"), name);
                return File.Exists(p) ? Assembly.UnsafeLoadFrom(p) : null;
            };
            CleanOld(Root); CleanOld(Path.Combine(Root, "lib")); CleanOld(Path.Combine(Root, "bin"));
            bool created;
            using (var mutex = new Mutex(true, "KH2CoopLauncher", out created))
            {
                if (!created) { MessageBox.Show("KH2 Co-op is already open.", "KH2 Co-op"); return 0; }
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
                try { Process.Start(new ProcessStartInfo(Path.Combine(Root, "KH2Coop.exe"), "-NoUpdate") { WorkingDirectory = Root, UseShellExecute = true }); } catch { }
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ launcher logic
    class Core
    {
        public readonly string Root, Bin, Kh2ctl, Runtime, Dll, Relay, Logs, SettingsFile, VersionFile;
        const string UpdateBase = "https://raw.githubusercontent.com/JayKazma/kh2-coop/main";
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
        readonly HashSet<string> resolving = new HashSet<string>();
        public string World = "", Room = ""; // from the runtime's room-state lines
        bool tiled; int tileTries;
        public bool TileBorderless = true; // solo test: the two game windows meet edge to edge (settings.json "tileBorderless")
        public bool CastReplay = true;     // replay the other player's magic/items on their clone (settings.json "castReplay")

        // state
        public int GamePid, GamePid2;
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
            Kh2ctl = Path.Combine(Bin, "kh2ctl.exe"); Runtime = Path.Combine(Bin, "kh2coop_runtime_scaffold.exe");
            Dll = Path.Combine(Bin, "kh2coop_inject.dll"); Relay = Path.Combine(Bin, "kh2coop_server.exe");
            Logs = Path.Combine(root, @"build\rig\logs"); SettingsFile = Path.Combine(root, "settings.json"); VersionFile = Path.Combine(root, "version.txt");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            LoadSettings();
            ResolveKnownNames();
            if (string.IsNullOrEmpty(GameDir)) GameDir = FindGameDir();
            Log("KH2 Co-op launcher " + LocalVersion() + " ready.");
            if (string.IsNullOrEmpty(GameDir)) Log("Game folder not found automatically. Use Browse.");
        }

        // ---- helpers
        public void Log(string text)
        {
            lock (gate) { lines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text); if (lines.Count > 300) lines.RemoveRange(0, lines.Count - 300); }
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
                if (d.TryGetValue("mode", out v) && v != null) Mode = v.ToString();
                if (d.TryGetValue("hostId", out v) && v != null) HostId = v.ToString();
                if (d.TryGetValue("friendId", out v) && v != null) FriendId = v.ToString();
                if (d.TryGetValue("recent", out v) && v is System.Collections.ArrayList)
                    foreach (var r in (System.Collections.ArrayList)v) if (r != null && Regex.IsMatch(r.ToString(), @"^\d{17}$")) Recent.Add(r.ToString());
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
                var d = new Dictionary<string, object> { { "gameDir", GameDir }, { "tileBorderless", TileBorderless }, { "castReplay", CastReplay }, { "mode", Mode }, { "hostId", HostId }, { "friendId", FriendId }, { "recent", Recent }, { "names", Names } };
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
                { "native", true }, { "lines", l }, { "recent", new List<string>(Recent) }, { "names", NamesSnapshot() },
                { "world", World }, { "room", Room }, { "tiled", tiled }
            };
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
                        wc.Headers[HttpRequestHeader.UserAgent] = "KH2Coop launcher";
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
                    wc.Headers["User-Agent"] = "KH2Coop-Launcher"; wc.Headers["Cache-Control"] = "no-cache";
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
                    wc.Headers["User-Agent"] = "KH2Coop-Launcher"; wc.Headers["Cache-Control"] = "no-cache";
                    string bust = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string tmp = Path.Combine(Path.GetTempPath(), "kh2coop-update-" + Guid.NewGuid().ToString("N") + ".zip");
                    wc.DownloadFile(UpdateBase + "/dist/kh2coop-update.zip" + bust, tmp);
                    string stage = Path.Combine(Path.GetTempPath(), "kh2coop-stage-" + Guid.NewGuid().ToString("N"));
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
                foreach (var f in new[] { "version.txt", "KH2COOP-PACKAGE", "package.json", "README.md", "KH2Coop.exe.config", "build.bat", "KH2Coop.ps1" })
                {
                    string src = Path.Combine(stage, f); if (File.Exists(src)) ReplaceFile(src, Path.Combine(Root, f));
                }
                string newExe = Path.Combine(stage, "KH2Coop.exe"), me = Path.Combine(Root, "KH2Coop.exe");
                if (File.Exists(newExe) && !SameFile(newExe, me)) { ReplaceFile(newExe, me); restart = true; }
                try { Directory.Delete(stage, true); } catch { }
                foreach (var d in new[] { "lib", "bin", "ui" }) Native.Unblock(Path.Combine(Root, d));
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
            foreach (var f in new[] { Kh2ctl, Runtime, Dll }) if (!File.Exists(f)) { Problem = "Files missing"; Log("Missing " + f + ". Check for updates."); return; }
            bool local = Mode == "local";
            if (local && !File.Exists(Relay)) { Problem = "Relay missing"; Log(@"Solo test needs bin\kh2coop_server.exe (the local relay). Copy it from the build's Release folder."); return; }
            if (launchTask != null) return;
            Problem = ""; GameDir = dir; SaveSettings();
            var env = new Dictionary<string, string> { { "SteamAppId", "2552430" }, { "SteamGameId", "2552430" }, { "KH2COOP_PUPPET_TRACE", "1" }, { "KH2COOP_AVATAR_DIAG", "1" } };
            if (!local) env["KH2COOP_STEAM_BROKER"] = "1";
            env["KH2COOP_CAST_REPLAY"] = CastReplay ? "1" : "0";
            // The other player is always a second Sora (one-clone party).
            env["KH2COOP_PARTY_NATIVE"] = "1"; env["KH2COOP_NATIVE_SORA_PRIVATE_STATUS"] = "1"; env["KH2COOP_CLONE_NEUTRAL_INPUT"] = "1"; env["KH2COOP_ALLY_HIT"] = "1";
            int count = local ? 2 : 1;
            Log(local ? "Starting two copies of KH2 (solo test)..." : "Starting KH2...");
            Launching = true;
            launchTask = Task.Factory.StartNew(() =>
            {
                var all = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    all.AddRange(RunCapture(Kh2ctl, "launch --game-dir \"" + dir + "\" --dll \"" + Dll + "\"", env));
                    if (i < count - 1) Thread.Sleep(3000);
                }
                return all.ToArray();
            });
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
            if (Mode == "local")
            {
                if (pids.Count < 2) { Problem = "Second game failed"; Log("Second KH2 did not start: " + string.Join(" ", t.Result)); return; }
                GamePid2 = pids[1]; MyId = "local";
                Log("Two games started (PIDs " + GamePid + ", " + GamePid2 + "). Load a save in both, then press Connect both.");
            }
            else Log("KH2 started (PID " + GamePid + "). Waiting for the Steam session (load to the title screen)...");
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
        void ParseRuntimeLine(string line)
        {
            var rs = Regex.Match(line, @"Room state: world=(\d+) room=(\d+)");
            if (rs.Success)
            {
                int w = int.Parse(rs.Groups[1].Value), r = int.Parse(rs.Groups[2].Value);
                string name; World = WorldNames.TryGetValue(w, out name) ? name : "World " + w.ToString("X2");
                Room = "room " + r.ToString("X2");
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
            peer = (peer ?? "").Trim();
            if (!Regex.IsMatch(peer, @"^\d{17}$")) { Log("Enter the other player's 17-digit SteamID."); return; }
            if (peer == MyId) { Log("That is your own SteamID. Enter the other player's."); return; }
            bool isHost = Mode != "join";
            if (isHost) FriendId = peer; else HostId = peer;
            Recent.Remove(peer); Recent.Insert(0, peer); if (Recent.Count > 6) Recent.RemoveRange(6, Recent.Count - 6);
            SaveSettings(); ResolveName(peer); ResolveName(MyId);
            string ini = WriteIni();
            string desync = Path.Combine(Root, @"build\rig\steam-desync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string role = isHost ? "player" : "friend1";
            string steamArgs = isHost ? "--steam-host --steam-allow " + peer : "--steam-join " + peer;
            string args = "--config \"" + ini + "\" --mode campaign_coop --network " + steamArgs + " --pid " + GamePid + " --role " + role + " --peer-id " + role + " --no-camera --tick-ms 16 --max-ticks 112500 --desync-dir \"" + desync + "\"";
            Connected = false; Ping = null; Loss = null; Problem = "";
            runtimeA = StartHidden(Runtime, args, Path.Combine(Logs, "runtime_" + GamePid + "_" + DateTime.Now.ToString("HHmmss") + ".log"), true);
            Log((isHost ? "Hosting; allowing " + peer + ". " : "Joining " + peer + ". ") + "Waiting for the other player...");
        }
        void StartLocal()
        {
            if (!GameAlive || !Game2Alive) { Log("Both games must be running. Press Start first."); return; }
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
            runtimeA = runtimeB = relay = null; Connected = false; Ping = null; Loss = null; World = ""; Room = "";
            if (was) Log("Disconnected.");
        }
        public void CloseGame()
        {
            StopRuntime();
            if (GameAlive) { try { RunCapture(Kh2ctl, "kill --all", null); } catch { } }
            Thread.Sleep(500);
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
            }
        }

        // Zips the newest logs (plus settings and version) to the Desktop for a bug report.
        void CollectLogs()
        {
            try
            {
                Directory.CreateDirectory(Logs);
                string outPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "KH2Coop-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
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
                // Keep the two windows side by side for the first ~3 minutes: KH2 re-applies its own display mode
                // after its splash and again after the first load, which undoes a single early tile.
                if (Mode == "local" && GameAlive && Game2Alive && tileTries < 500) { tileTries++; int t = Native.Tile(GamePid, GamePid2, TileBorderless); if (t == 1 && !tiled) { tiled = true; Log("Game windows tiled side by side."); } }
                if (!GameAlive) { tiled = false; tileTries = 0; }
                if (runtimeA != null && !Alive(runtimeA)) { runtimeA = null; Connected = false; Ping = null; Loss = null; Log("Co-op program exited."); if (Alive(runtimeB) || Alive(relay)) StopRuntime(); }
                if (GamePid != 0 && !GameAlive) { Log("KH2 closed."); GamePid = 0; MyId = ""; StopRuntime(); if (Game2Alive) { try { Process.GetProcessById(GamePid2).Kill(); } catch { } } GamePid2 = 0; }
                else if (GamePid2 != 0 && !Game2Alive) { Log("Second KH2 closed."); GamePid2 = 0; StopRuntime(); }
            }
            catch (Exception ex) { Log("Error: " + ex.Message); }
        }
        public void Shutdown() { StopRuntime(); }
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
            Text = "KH2 Co-op"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
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
                MessageBox.Show("The launcher window could not start:\n\n" + ex.Message + "\n\nMake sure the lib\\ folder is present and Microsoft Edge is installed.", "KH2 Co-op", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        void OnWebReady(object s, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                MessageBox.Show("WebView2 could not start:\n\n" + e.InitializationException.Message + "\n\nInstall the Microsoft Edge WebView2 Runtime (it comes with Windows 11 and Edge).", "KH2 Co-op", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close(); return;
            }
            var c = web.CoreWebView2;
            c.Settings.AreDefaultContextMenusEnabled = false; c.Settings.IsStatusBarEnabled = false; c.Settings.AreDevToolsEnabled = false; c.Settings.IsZoomControlEnabled = false;
            c.SetVirtualHostNameToFolderMapping("app.kh2coop", Path.Combine(core.Root, "ui"), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            c.WebMessageReceived += OnMessage;
            c.Navigate("https://app.kh2coop/index.html");
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
                if (MessageBox.Show(this, "Closing the launcher disconnects co-op (the game stays open). Close?", "KH2 Co-op", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }
    }
}
