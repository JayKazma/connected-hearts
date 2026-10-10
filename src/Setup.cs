// Connected Hearts installer. The release package (kh2coop-update.zip) is embedded as resource "payload.zip";
// this unpacks it to a folder of the user's choice, makes shortcuts and launches the launcher.
// Built by tools/build_exe.sh (Mono) or build.bat (Windows csc).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KH2CoopSetup
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool DeleteFile(string name);
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { if (!Native.SetProcessDpiAwarenessContext((IntPtr)(-4))) Native.SetProcessDPIAware(); } catch { try { Native.SetProcessDPIAware(); } catch { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    class SetupForm : Form
    {
        static readonly Color Ink = ColorTranslator.FromHtml("#2A1434"), Panel = ColorTranslator.FromHtml("#3B1F4A"), Text2 = ColorTranslator.FromHtml("#FFE2C6"), Orange = ColorTranslator.FromHtml("#FF7A3C");
        TextBox folder; CheckBox desk, menu, launch; Button install, browse; Label status; ProgressBar bar;
        bool working;

        public SetupForm()
        {
            Text = "Connected Hearts Setup"; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ink; ForeColor = Color.FromArgb(255, 246, 238); Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(560, 400);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            var title = new Label { Text = "Connected Hearts", Font = new Font("Segoe UI", 20f, FontStyle.Bold), Location = new Point(28, 24), AutoSize = true };
            var sub = new Label { Text = "Online co-op for Kingdom Hearts II Final Mix (Steam). The other player joins your party as Sora 2.", ForeColor = Text2, Location = new Point(30, 66), AutoSize = true };
            var lbl = new Label { Text = "Install to", ForeColor = Text2, Location = new Point(30, 112), AutoSize = true };
            folder = new TextBox { Location = new Point(30, 134), Size = new Size(400, 28), BackColor = Panel, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Connected Hearts");
            browse = Flat("Browse", new Point(440, 132), new Size(90, 30)); browse.Click += delegate { Browse(); };
            desk = Check("Desktop shortcut", 184, true);
            menu = Check("Start menu shortcut", 212, true);
            launch = Check("Open Connected Hearts when done", 240, true);
            var note = new Label { Text = "Needs: Kingdom Hearts HD 1.5+2.5 ReMiX on Steam, Steam running. Windows Defender may ask once about the mod's DLL.", ForeColor = Text2, Location = new Point(30, 276), Size = new Size(500, 40) };
            bar = new ProgressBar { Location = new Point(30, 322), Size = new Size(500, 8), Style = ProgressBarStyle.Continuous, Visible = false };
            status = new Label { Text = "", ForeColor = Text2, Location = new Point(30, 338), AutoSize = true };
            install = Flat("Install", new Point(410, 352), new Size(120, 36)); install.BackColor = Orange; install.ForeColor = Color.White; install.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            install.Click += delegate { Install(); };
            Controls.AddRange(new Control[] { title, sub, lbl, folder, browse, desk, menu, launch, note, bar, status, install });
        }

        Button Flat(string text, Point at, Size size)
        {
            var b = new Button { Text = text, Location = at, Size = size, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = ForeColor, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#6B4A7A"); return b;
        }
        CheckBox Check(string text, int y, bool on) { return new CheckBox { Text = text, Checked = on, Location = new Point(30, y), AutoSize = true, ForeColor = ForeColor }; }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where to install Connected Hearts", SelectedPath = folder.Text })
                if (d.ShowDialog(this) == DialogResult.OK) folder.Text = Path.Combine(d.SelectedPath, "Connected Hearts");
        }

        void Install()
        {
            if (working) return;
            string dir = folder.Text.Trim();
            if (dir.Length == 0) { Say("Choose a folder."); return; }
            if (Process.GetProcessesByName("KH2Coop").Length > 0)
            {
                if (MessageBox.Show(this, "Connected Hearts is open. Close it and continue?", "Connected Hearts Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                foreach (var p in Process.GetProcessesByName("KH2Coop")) { try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) p.Kill(); } catch { } }
                Thread.Sleep(500);
            }
            working = true; install.Enabled = false; browse.Enabled = false; bar.Visible = true; bar.Value = 0;
            bool mkDesk = desk.Checked, mkMenu = menu.Checked, run = launch.Checked;
            var t = new Thread((ThreadStart)delegate()
            {
                try
                {
                    Say("Unpacking..."); Progress(10);
                    Directory.CreateDirectory(dir);
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                    using (var z = new ZipArchive(s, ZipArchiveMode.Read))
                    {
                        int n = 0;
                        foreach (var e in z.Entries)
                        {
                            string target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', '\\')));
                            if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                            if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            if (Path.GetFileName(target).Equals("settings.json", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                            try { e.ExtractToFile(target, true); }
                            catch (IOException) { string aside = target + ".old"; try { File.Delete(aside); } catch { } File.Move(target, aside); e.ExtractToFile(target, true); }
                            try { Native.DeleteFile(target + ":Zone.Identifier"); } catch { }
                            n++; Progress(10 + Math.Min(70, n * 70 / Math.Max(1, z.Entries.Count)));
                        }
                    }
                    string exe = Path.Combine(dir, "KH2Coop.exe");
                    if (!File.Exists(exe)) throw new Exception("KH2Coop.exe was not in the package.");
                    Say("Creating shortcuts..."); Progress(85);
                    if (mkDesk) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Connected Hearts.lnk"), exe, dir);
                    if (mkMenu)
                    {
                        string pm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Connected Hearts");
                        Directory.CreateDirectory(pm); Shortcut(Path.Combine(pm, "Connected Hearts.lnk"), exe, dir);
                    }
                    Progress(100); Say("Installed to " + dir);
                    if (run) { try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true }); } catch { } }
                    BeginInvoke(new Action(delegate { install.Text = "Done"; install.Enabled = true; install.Click += delegate { Close(); }; working = false; }));
                }
                catch (Exception ex)
                {
                    Say("Failed: " + ex.Message);
                    BeginInvoke(new Action(delegate { install.Enabled = true; browse.Enabled = true; working = false; }));
                }
            });
            t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start();
        }

        static void Shortcut(string lnk, string target, string workDir)
        {
            // WScript.Shell through late-bound COM (no interop assembly needed).
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { Path.Combine(workDir, @"ui\icon.ico") + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Connected Hearts" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        void Say(string s) { try { BeginInvoke(new Action(delegate { status.Text = s; })); } catch { } }
        void Progress(int v) { try { BeginInvoke(new Action(delegate { bar.Value = Math.Max(0, Math.Min(100, v)); })); } catch { } }
    }
}
