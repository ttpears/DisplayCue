using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace MonitorHotkeys
{
    [DataContract]
    public sealed class AppConfig
    {
        // Legacy fields are retained only so existing 1.x settings can be migrated.
        [DataMember] public string TvMatch = "TV";
        [DataMember] public string TvOnlyHotkey = "Ctrl+Shift+F11";
        [DataMember] public string AllHotkey = "Ctrl+Shift+F12";
        [DataMember] public string QuickOneProfileId = "";
        [DataMember] public string QuickTwoProfileId = "$all";
        [DataMember] public string QuickOneHotkey = "Ctrl+Shift+F11";
        [DataMember] public string QuickTwoHotkey = "Ctrl+Shift+F12";
        [DataMember] public bool PeerEnabled;
        [DataMember] public string PeerHost = "";
        [DataMember] public int PeerPort = 45831;
        [DataMember] public int ListenPort = 45831;
        [DataMember] public string DeviceName = Environment.MachineName;
        [DataMember] public List<DisplayProfile> Profiles = new List<DisplayProfile>();
    }

    [DataContract]
    public sealed class DisplayProfile
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "New profile";
        [DataMember] public List<string> Devices = new List<string>();
        [DataMember] public List<MonitorInputAssignment> MonitorInputs = new List<MonitorInputAssignment>();
        [DataMember] public string PeerProfileName = "";
        public override string ToString() { return Name; }
    }

    [DataContract]
    public sealed class MonitorInputAssignment
    {
        [DataMember] public string DevicePath = "";
        [DataMember] public uint Input;
    }

    public sealed class MonitorInfo
    {
        public string Name, Key, DevicePath, GdiName;
        public bool Active;
        public override string ToString() { return Name; }
    }

    public static class ConfigStore
    {
        public static readonly string ConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayCue");
        public static readonly string PathName = Path.Combine(ConfigDirectory, "settings.json");
        public static AppConfig Load()
        {
            string path = PathName;
            if (!File.Exists(path))
            {
                string previousNative = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor-hotkeys.json");
                string prototype = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "monitor-wheel.json"));
                if (File.Exists(previousNative)) path = previousNative;
                else if (File.Exists(prototype)) path = prototype;
            }
            try
            {
                if (File.Exists(path))
                    using (FileStream s = File.OpenRead(path))
                    {
                        AppConfig c = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(s);
                        if (c.Profiles == null) c.Profiles = new List<DisplayProfile>();
                        foreach (DisplayProfile p in c.Profiles) { if (String.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N"); if (p.MonitorInputs == null) p.MonitorInputs = new List<MonitorInputAssignment>(); }
                        if (String.IsNullOrWhiteSpace(c.QuickOneHotkey) || (c.QuickOneHotkey == "Ctrl+Shift+F11" && !String.IsNullOrWhiteSpace(c.TvOnlyHotkey) && c.TvOnlyHotkey != "Ctrl+Shift+F11")) c.QuickOneHotkey = String.IsNullOrWhiteSpace(c.TvOnlyHotkey) ? "Ctrl+Shift+F11" : c.TvOnlyHotkey;
                        if (String.IsNullOrWhiteSpace(c.QuickTwoHotkey) || (c.QuickTwoHotkey == "Ctrl+Shift+F12" && !String.IsNullOrWhiteSpace(c.AllHotkey) && c.AllHotkey != "Ctrl+Shift+F12")) c.QuickTwoHotkey = String.IsNullOrWhiteSpace(c.AllHotkey) ? "Ctrl+Shift+F12" : c.AllHotkey;
                        if (c.QuickOneProfileId == null) c.QuickOneProfileId = "";
                        if (String.IsNullOrWhiteSpace(c.QuickTwoProfileId)) c.QuickTwoProfileId = "$all";
                        if (c.PeerPort <= 0) c.PeerPort = 45831; if (c.ListenPort <= 0) c.ListenPort = 45831; if (String.IsNullOrWhiteSpace(c.DeviceName)) c.DeviceName = Environment.MachineName;
                        return c;
                    }
            }
            catch { }
            return new AppConfig();
        }
        public static void Save(AppConfig c)
        {
            Directory.CreateDirectory(ConfigDirectory);
            using (FileStream s = File.Create(PathName))
                new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(s, c);
        }
    }

    public static class DisplayEngine
    {
        const uint QDC_ALL_PATHS = 1, QDC_ONLY_ACTIVE_PATHS = 2;
        const uint SDC_APPLY = 0x80, SDC_USE_SUPPLIED = 0x20, SDC_SAVE = 0x200, SDC_ALLOW = 0x400;
        const uint PATH_ACTIVE = 1;

        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)] struct SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct TARGET { public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling; public RATIONAL refreshRate; public uint scanLineOrdering; [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct PATH { public SOURCE sourceInfo; public TARGET targetInfo; public uint flags; }
        [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODEUNION { }
        [StructLayout(LayoutKind.Sequential)] struct MODE { public uint infoType, id; public LUID adapterId; public MODEUNION data; }
        [StructLayout(LayoutKind.Sequential)] struct HEADER { public int type, size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TARGETNAME { public HEADER header; public uint flags, outputTechnology; public ushort edidManufactureId, edidProductCodeId; public uint connectorInstance; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SOURCENAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PATH[] pathInfo, ref uint modes, [Out] MODE[] modeInfo, IntPtr topology);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, PATH[] pathInfo, uint modes, MODE[] modeInfo, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DisplayConfigGetDeviceInfo(ref TARGETNAME request);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DisplayConfigGetDeviceInfo(ref SOURCENAME request);

        static void Query(uint flag, out PATH[] paths, out MODE[] modes)
        {
            uint pc, mc; int e = GetDisplayConfigBufferSizes(flag, out pc, out mc); if (e != 0) throw new InvalidOperationException("Display query failed: " + e);
            paths = new PATH[pc]; modes = new MODE[mc]; e = QueryDisplayConfig(flag, ref pc, paths, ref mc, modes, IntPtr.Zero); if (e != 0) throw new InvalidOperationException("Display query failed: " + e);
            Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
        }
        static string Key(LUID id, uint target) { return id.HighPart + ":" + id.LowPart + ":" + target; }
        static TARGETNAME TargetName(PATH p)
        {
            TARGETNAME n = new TARGETNAME(); n.header.type = 2; n.header.size = Marshal.SizeOf(typeof(TARGETNAME)); n.header.adapterId = p.targetInfo.adapterId; n.header.id = p.targetInfo.id; DisplayConfigGetDeviceInfo(ref n); return n;
        }
        static string SourceName(PATH p)
        {
            SOURCENAME n = new SOURCENAME(); n.header.type = 1; n.header.size = Marshal.SizeOf(typeof(SOURCENAME)); n.header.adapterId = p.sourceInfo.adapterId; n.header.id = p.sourceInfo.id; return DisplayConfigGetDeviceInfo(ref n) == 0 ? n.viewGdiDeviceName : "";
        }
        public static List<MonitorInfo> GetMonitors()
        {
            PATH[] all, active; MODE[] a, b; Query(QDC_ALL_PATHS, out all, out a); Query(QDC_ONLY_ACTIVE_PATHS, out active, out b);
            HashSet<string> on = new HashSet<string>(); Dictionary<string, string> gdi = new Dictionary<string, string>();
            foreach (PATH p in active) { string k = Key(p.targetInfo.adapterId, p.targetInfo.id); on.Add(k); gdi[k] = SourceName(p); }
            List<MonitorInfo> result = new List<MonitorInfo>(); HashSet<string> seen = new HashSet<string>();
            foreach (PATH p in all)
            {
                if (!p.targetInfo.targetAvailable) continue; string k = Key(p.targetInfo.adapterId, p.targetInfo.id); if (!seen.Add(k)) continue;
                TARGETNAME n = TargetName(p); result.Add(new MonitorInfo { Name = String.IsNullOrWhiteSpace(n.monitorFriendlyDeviceName) ? "Unknown monitor" : n.monitorFriendlyDeviceName, Key = k, Active = on.Contains(k), DevicePath = n.monitorDevicePath, GdiName = gdi.ContainsKey(k) ? gdi[k] : SourceName(p) });
            }
            return result;
        }
        public static void RestoreExtended()
        {
            int e = 0;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                e = SetDisplayConfig(0, null, 0, null, SDC_APPLY | 0x4);
                if (e == 0) return;
                Thread.Sleep(600);
            }
            throw new InvalidOperationException("Windows could not restore the extended desktop (error " + e + ").");
        }
        public static void ApplyDevicePaths(IEnumerable<string> devicePaths)
        {
            HashSet<string> wantedDevices = new HashSet<string>(devicePaths, StringComparer.OrdinalIgnoreCase);
            if (wantedDevices.Count == 0) throw new InvalidOperationException("This profile does not contain any displays.");
            List<MonitorInfo> monitors = new List<MonitorInfo>(); DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            do
            {
                monitors = GetMonitors();
                if (monitors.Count(x => wantedDevices.Contains(x.DevicePath)) == wantedDevices.Count) break;
                Thread.Sleep(500);
            }
            while (DateTime.UtcNow < deadline);
            int available = monitors.Count(x => wantedDevices.Contains(x.DevicePath));
            if (available != wantedDevices.Count) throw new InvalidOperationException("Only " + available + " of " + wantedDevices.Count + " displays in this profile became available. Check their input source, cable, or power.");
            RestoreExtended(); Thread.Sleep(1000);
            monitors = GetMonitors(); HashSet<string> wantedKeys = new HashSet<string>(monitors.Where(x => wantedDevices.Contains(x.DevicePath)).Select(x => x.Key));
            if (wantedKeys.Count != wantedDevices.Count) throw new InvalidOperationException("Windows did not make every display in this profile available after restoring the extended desktop.");
            if (wantedKeys.Count == monitors.Count) return;
            PATH[] active; MODE[] modes; Query(QDC_ONLY_ACTIVE_PATHS, out active, out modes); List<PATH> selected = new List<PATH>();
            foreach (PATH original in active)
            {
                if (!wantedKeys.Contains(Key(original.targetInfo.adapterId, original.targetInfo.id))) continue;
                PATH p = original; p.flags |= PATH_ACTIVE; p.sourceInfo.modeInfoIdx = 0xffffffff; p.targetInfo.modeInfoIdx = 0xffffffff; selected.Add(p);
            }
            if (selected.Count == 0) throw new InvalidOperationException("The requested displays are unavailable.");
            int e = SetDisplayConfig((uint)selected.Count, selected.ToArray(), 0, null, SDC_APPLY | SDC_USE_SUPPLIED | SDC_SAVE | SDC_ALLOW);
            if (e != 0) throw new InvalidOperationException("Windows rejected the display profile (error " + e + ").");
        }
    }

    public sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        public HotKeyWindow() { CreateHandle(new CreateParams()); }
        public bool Add(int id, uint mods, uint key) { return RegisterHotKey(Handle, id, mods, key); }
        public void Remove(int id) { UnregisterHotKey(Handle, id); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x0312 && Pressed != null) Pressed(m.WParam.ToInt32()); base.WndProc(ref m); }
        public void Dispose() { Remove(1); Remove(2); DestroyHandle(); }
    }

    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(17, 20, 27), Sidebar = Color.FromArgb(22, 26, 35), Panel = Color.FromArgb(29, 34, 45), Input = Color.FromArgb(38, 44, 57), Accent = Color.FromArgb(53, 181, 211), AccentDark = Color.FromArgb(22, 103, 124), Text = Color.FromArgb(246, 248, 252), Muted = Color.FromArgb(163, 174, 193), Border = Color.FromArgb(55, 63, 80);
        public static Button Button(string text, bool primary)
        {
            Button b = new Button(); b.Text = text; b.Height = 40; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Border; b.BackColor = primary ? AccentDark : Panel; b.ForeColor = Text; b.Cursor = Cursors.Hand; b.Font = new Font("Segoe UI Semibold", 9.5f); return b;
        }
        public static Label Label(string text, float size, bool bold) { return new Label { Text = text, AutoSize = true, ForeColor = Text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) }; }
        public static Panel Card(int x, int y, int width, int height) { return new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Panel, Padding = new Padding(20) }; }
    }

    public sealed class IdentifyOverlay : Form
    {
        public IdentifyOverlay(int number, string model, Screen screen)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Size = new Size(280, 165); BackColor = Theme.Accent; Opacity = .95;
            Location = new Point(screen.Bounds.Left + (screen.Bounds.Width - Width) / 2, screen.Bounds.Top + (screen.Bounds.Height - Height) / 2);
            Controls.Add(new Label { Text = model, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold) });
            Controls.Add(new Label { Text = number.ToString(), Dock = DockStyle.Top, Height = 108, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 58, FontStyle.Bold) });
        }
    }

    public sealed class ConfirmLayoutForm : Form
    {
        readonly Label countdown; readonly System.Windows.Forms.Timer timer; int seconds = 15;
        public ConfirmLayoutForm(string profileName)
        {
            Text = "Keep display configuration?"; Size = new Size(470, 245); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; TopMost = true; BackColor = Theme.Back; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); DialogResult = DialogResult.No;
            Label title = Theme.Label("Keep this display configuration?", 16, true); title.Location = new Point(28, 25); Controls.Add(title);
            Label description = Theme.Label("Profile: " + profileName, 10, false); description.ForeColor = Theme.Muted; description.Location = new Point(30, 66); Controls.Add(description);
            countdown = Theme.Label("Reverting automatically in 15 seconds", 10, false); countdown.ForeColor = Color.FromArgb(255, 190, 90); countdown.Location = new Point(30, 98); Controls.Add(countdown);
            Button revert = Theme.Button("Revert", false); revert.SetBounds(205, 143, 105, 40); revert.DialogResult = DialogResult.No; Controls.Add(revert);
            Button keep = Theme.Button("Keep", true); keep.SetBounds(325, 143, 105, 40); keep.DialogResult = DialogResult.Yes; Controls.Add(keep); AcceptButton = keep; CancelButton = revert;
            timer = new System.Windows.Forms.Timer(); timer.Interval = 1000; timer.Tick += delegate { seconds--; countdown.Text = "Reverting automatically in " + seconds + " second" + (seconds == 1 ? "" : "s"); if (seconds <= 0) { timer.Stop(); DialogResult = DialogResult.No; Close(); } }; timer.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); timer.Dispose(); base.OnFormClosed(e); }
    }

    public sealed class MonitorInputsForm : Form
    {
        sealed class InputChoice
        {
            public uint? Value; public string Text;
            public override string ToString() { return Text; }
        }
        readonly List<DdcMonitorInfo> monitors;
        readonly Dictionary<DdcMonitorInfo, ComboBox> choices = new Dictionary<DdcMonitorInfo, ComboBox>();
        public List<MonitorInputAssignment> Assignments { get; private set; }

        public MonitorInputsForm(IEnumerable<MonitorInputAssignment> existing)
        {
            Assignments = existing == null ? new List<MonitorInputAssignment>() : existing.Select(x => new MonitorInputAssignment { DevicePath = x.DevicePath, Input = x.Input }).ToList();
            Text = "Monitor inputs"; ClientSize = new Size(650, 480); MinimumSize = new Size(650, 480); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Back; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Label title = Theme.Label("Monitor inputs", 18, true); title.Location = new Point(28, 22); Controls.Add(title);
            Label help = Theme.Label("Optionally switch a monitor's physical HDMI or DisplayPort input when this profile runs.", 9.5f, false); help.ForeColor = Theme.Muted; help.Location = new Point(30, 58); Controls.Add(help);
            Label safety = Theme.Label("DisplayCue reads each monitor through DDC/CI. Unsupported displays are left unchanged.", 8.5f, false); safety.ForeColor = Theme.Muted; safety.Location = new Point(30, 82); Controls.Add(safety);
            Panel rows = new Panel { Location = new Point(28, 114), Size = new Size(594, 280), BackColor = Theme.Panel, AutoScroll = true, Padding = new Padding(18) }; Controls.Add(rows);
            monitors = DdcEngine.Discover(); int y = 12; Dictionary<string, int> totals = monitors.Where(x => x.SupportsInputSwitching).GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.Count()); Dictionary<string, int> seen = new Dictionary<string, int>();
            foreach (DdcMonitorInfo monitor in monitors)
            {
                if (!monitor.SupportsInputSwitching) continue;
                if (!seen.ContainsKey(monitor.Name)) seen[monitor.Name] = 0; seen[monitor.Name]++; string suffix = totals[monitor.Name] > 1 ? " #" + seen[monitor.Name] : "";
                Label name = Theme.Label(monitor.Name + suffix, 10, true); name.Location = new Point(18, y); rows.Controls.Add(name);
                Label current = Theme.Label("Current: " + (monitor.CurrentInput.HasValue ? DdcEngine.InputName(monitor.CurrentInput.Value) : "Unknown"), 8.5f, false); current.ForeColor = Theme.Muted; current.Location = new Point(18, y + 27); rows.Controls.Add(current);
                ComboBox input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Input, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10), Location = new Point(286, y + 5), Width = 270 };
                input.Items.Add(new InputChoice { Value = null, Text = "Do not change" });
                foreach (DdcInputOption option in monitor.Inputs) input.Items.Add(new InputChoice { Value = option.Value, Text = option.Name });
                MonitorInputAssignment selected = Assignments.FirstOrDefault(x => x.DevicePath.Equals(monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
                int selectedIndex = 0; if (selected != null) for (int i = 1; i < input.Items.Count; i++) if (((InputChoice)input.Items[i]).Value == selected.Input) selectedIndex = i;
                input.SelectedIndex = selectedIndex; choices[monitor] = input; rows.Controls.Add(input); y += 68;
            }
            if (choices.Count == 0)
            {
                Label none = Theme.Label("No active monitor currently reports DDC/CI input-source control.", 10, false); none.ForeColor = Theme.Muted; none.Location = new Point(18, 22); rows.Controls.Add(none);
            }
            Button cancel = Theme.Button("Cancel", false); cancel.SetBounds(390, 414, 108, 40); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel);
            Button save = Theme.Button("Use these inputs", true); save.SetBounds(510, 414, 112, 40); save.Click += Save; Controls.Add(save); AcceptButton = save; CancelButton = cancel;
        }
        void Save(object sender, EventArgs e)
        {
            Assignments = new List<MonitorInputAssignment>();
            foreach (KeyValuePair<DdcMonitorInfo, ComboBox> pair in choices)
            {
                InputChoice choice = pair.Value.SelectedItem as InputChoice;
                if (choice != null && choice.Value.HasValue) Assignments.Add(new MonitorInputAssignment { DevicePath = pair.Key.DevicePath, Input = choice.Value.Value });
            }
            DialogResult = DialogResult.OK; Close();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { DdcEngine.Release(monitors); base.OnFormClosed(e); }
    }

    public sealed class SettingsForm : Form
    {
        sealed class QuickActionOption
        {
            public string Id, Name;
            public override string ToString() { return Name; }
        }
        readonly AppController owner; readonly AppConfig config; readonly List<MonitorInfo> monitors;
        ComboBox quickOne, quickTwo; TextBox quickOneHotkey, quickTwoHotkey; ListBox profiles; CheckedListBox checks; TextBox profileName, profilePeerName; Panel content; Button generalNav, profilesNav, peerNav;
        TextBox peerHost, peerPort, listenPort, pairingKey, deviceName; CheckBox peerEnabled;
        List<MonitorInputAssignment> profileInputs = new List<MonitorInputAssignment>(); Button inputButton;
        public SettingsForm(AppController owner)
        {
            this.owner = owner; config = owner.Config; monitors = DisplayEngine.GetMonitors();
            Text = "DisplayCue Settings"; ClientSize = new Size(900, 620); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Theme.Back; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = Theme.Sidebar, Padding = new Padding(16, 22, 16, 16) }; Controls.Add(sidebar);
            Label brand = Theme.Label("DisplayCue", 18, true); brand.Location = new Point(18, 20); sidebar.Controls.Add(brand);
            Label tagline = Theme.Label("Displays, on cue.", 9, false); tagline.ForeColor = Theme.Muted; tagline.Location = new Point(20, 54); sidebar.Controls.Add(tagline);
            generalNav = NavButton("Quick switching", 94); generalNav.Click += delegate { ShowPage(0); }; sidebar.Controls.Add(generalNav);
            profilesNav = NavButton("Display profiles", 142); profilesNav.Click += delegate { ShowPage(1); }; sidebar.Controls.Add(profilesNav);
            peerNav = NavButton("Paired computer", 190); peerNav.Click += delegate { ShowPage(2); }; sidebar.Controls.Add(peerNav);
            Label version = Theme.Label("Version " + Application.ProductVersion, 8.5f, false); version.ForeColor = Theme.Muted; version.Location = new Point(20, 566); sidebar.Controls.Add(version);
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back }; Controls.Add(content); content.BringToFront(); ShowPage(0);
        }
        Button NavButton(string text, int y) { Button b = new Button { Text = text, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(12, y), Size = new Size(172, 42), Padding = new Padding(12, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Theme.Sidebar, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9.5f) }; b.FlatAppearance.BorderSize = 0; return b; }
        void ShowPage(int page)
        {
            content.Controls.Clear(); Button[] nav = { generalNav, profilesNav, peerNav }; for (int i = 0; i < nav.Length; i++) { nav[i].BackColor = i == page ? Theme.Panel : Theme.Sidebar; nav[i].ForeColor = i == page ? Theme.Text : Theme.Muted; }
            if (page == 1) BuildProfiles(content); else if (page == 2) BuildPeer(content); else BuildGeneral(content);
        }
        void BuildGeneral(Control page)
        {
            Label title = Theme.Label("Quick switching", 21, true); title.Location = new Point(34, 28); page.Controls.Add(title);
            Label help = Theme.Label("Set up the two display changes you use most.", 10, false); help.ForeColor = Theme.Muted; help.Location = new Point(36, 67); page.Controls.Add(help);
            Button identify = Theme.Button("Identify displays", false); identify.SetBounds(518, 29, 148, 40); identify.Click += delegate { owner.Identify(); }; page.Controls.Add(identify);
            Panel actionCard = Theme.Card(34, 106, 632, 356); page.Controls.Add(actionCard);
            actionCard.Controls.Add(At(Theme.Label("Quick actions", 11, true), 20, 18));
            Label actionHelp = Theme.Label("Assign any two profiles to global shortcuts. Leave a shortcut blank to disable it.", 9, false); actionHelp.ForeColor = Theme.Muted; actionHelp.Location = new Point(20, 45); actionCard.Controls.Add(actionHelp);
            actionCard.Controls.Add(At(Theme.Label("Quick action 1", 9, true), 20, 84));
            quickOne = QuickActionBox(config.QuickOneProfileId, 20, 110); actionCard.Controls.Add(quickOne);
            actionCard.Controls.Add(At(Theme.Label("Keyboard shortcut", 8.5f, false), 332, 84));
            quickOneHotkey = HotkeyBox(config.QuickOneHotkey, 332, 110); quickOneHotkey.Width = 280; actionCard.Controls.Add(quickOneHotkey);
            actionCard.Controls.Add(At(Theme.Label("Quick action 2", 9, true), 20, 182));
            quickTwo = QuickActionBox(config.QuickTwoProfileId, 20, 208); actionCard.Controls.Add(quickTwo);
            actionCard.Controls.Add(At(Theme.Label("Keyboard shortcut", 8.5f, false), 332, 182));
            quickTwoHotkey = HotkeyBox(config.QuickTwoHotkey, 332, 208); quickTwoHotkey.Width = 280; actionCard.Controls.Add(quickTwoHotkey);
            Label note = Theme.Label("Create and edit profiles under Display profiles. Every profile also stays available from the tray.", 8.5f, false); note.ForeColor = Theme.Muted; note.Location = new Point(20, 286); actionCard.Controls.Add(note);
            Button cancel = Theme.Button("Cancel", false); cancel.SetBounds(424, 527, 104, 42); cancel.Click += delegate { Close(); }; page.Controls.Add(cancel);
            Button save = Theme.Button("Save changes", true); save.SetBounds(540, 527, 126, 42); save.Click += SaveGeneral; page.Controls.Add(save);
        }
        ComboBox QuickActionBox(string selectedId, int x, int y)
        {
            ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Input, ForeColor = Theme.Text, Width = 280, Location = new Point(x, y), Font = new Font("Segoe UI", 10.5f), FlatStyle = FlatStyle.Flat };
            box.Items.Add(new QuickActionOption { Id = "", Name = "No action" });
            box.Items.Add(new QuickActionOption { Id = "$all", Name = "All displays" });
            foreach (DisplayProfile profile in config.Profiles) box.Items.Add(new QuickActionOption { Id = profile.Id, Name = profile.Name });
            for (int i = 0; i < box.Items.Count; i++) if (((QuickActionOption)box.Items[i]).Id == selectedId) { box.SelectedIndex = i; break; }
            if (box.SelectedIndex < 0) box.SelectedIndex = 0;
            return box;
        }
        TextBox HotkeyBox(string value, int x, int y)
        {
            TextBox box = new TextBox { ReadOnly = true, Text = value, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Location = new Point(x, y), Width = 320 };
            box.KeyDown += delegate(object s, KeyEventArgs e) { List<string> p = new List<string>(); if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape) { ((TextBox)s).Text = ""; e.SuppressKeyPress = true; return; } if (e.Control) p.Add("Ctrl"); if (e.Alt) p.Add("Alt"); if (e.Shift) p.Add("Shift"); if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.ShiftKey) p.Add(e.KeyCode.ToString()); if (p.Count > 1) ((TextBox)s).Text = String.Join("+", p); e.SuppressKeyPress = true; };
            return box;
        }
        void SaveGeneral(object sender, EventArgs e)
        {
            config.QuickOneProfileId = ((QuickActionOption)quickOne.SelectedItem).Id; config.QuickTwoProfileId = ((QuickActionOption)quickTwo.SelectedItem).Id; config.QuickOneHotkey = quickOneHotkey.Text; config.QuickTwoHotkey = quickTwoHotkey.Text; ConfigStore.Save(config); owner.RegisterHotkeys(); Close();
        }
        void BuildProfiles(Control page)
        {
            Label title = Theme.Label("Display profiles", 21, true); title.Location = new Point(34, 28); page.Controls.Add(title);
            Label help = Theme.Label("Save any display combination and launch it from the tray.", 10, false); help.ForeColor = Theme.Muted; help.Location = new Point(36, 67); page.Controls.Add(help);
            Button identify = Theme.Button("Identify displays", false); identify.SetBounds(518, 29, 148, 40); identify.Click += delegate { owner.Identify(); }; page.Controls.Add(identify);
            Panel listCard = Theme.Card(34, 106, 216, 399); page.Controls.Add(listCard); listCard.Controls.Add(At(Theme.Label("Your profiles", 11, true), 16, 15));
            profiles = new ListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), Location = new Point(16, 50), Size = new Size(184, 282), IntegralHeight = false }; foreach (DisplayProfile p in config.Profiles) profiles.Items.Add(p); profiles.SelectedIndexChanged += LoadProfile; listCard.Controls.Add(profiles);
            Button add = Theme.Button("New profile", false); add.SetBounds(16, 345, 184, 38); add.Click += delegate { profiles.ClearSelected(); profileName.Text = ""; profilePeerName.Text = ""; profileInputs = new List<MonitorInputAssignment>(); UpdateInputButton(); for (int i = 0; i < checks.Items.Count; i++) checks.SetItemChecked(i, false); }; listCard.Controls.Add(add);
            Panel editorCard = Theme.Card(266, 106, 400, 399); page.Controls.Add(editorCard); editorCard.Controls.Add(At(Theme.Label("Profile details", 11, true), 18, 15));
            editorCard.Controls.Add(At(Theme.Label("Name", 9, true), 18, 53));
            profileName = new TextBox { BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Location = new Point(18, 78), Width = 364 }; editorCard.Controls.Add(profileName);
            editorCard.Controls.Add(At(Theme.Label("Displays to keep active", 9, true), 18, 119));
            checks = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), Location = new Point(18, 145), Size = new Size(364, 105) };
            Dictionary<string, int> totals = monitors.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.Count()); Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int i = 0; i < monitors.Count; i++) { MonitorInfo m = monitors[i]; if (!seen.ContainsKey(m.Name)) seen[m.Name] = 0; seen[m.Name]++; string suffix = totals[m.Name] > 1 ? " #" + seen[m.Name] : ""; checks.Items.Add((i + 1) + ". " + m.Name + suffix + (m.Active ? "  ·  On" : "  ·  Off")); } editorCard.Controls.Add(checks);
            editorCard.Controls.Add(At(Theme.Label("Profile to run on paired computer", 8.5f, true), 18, 260));
            profilePeerName = new TextBox { BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10), Location = new Point(18, 283), Width = 364 }; editorCard.Controls.Add(profilePeerName);
            inputButton = Theme.Button("Monitor inputs…", false); inputButton.SetBounds(18, 327, 154, 40); inputButton.Click += ConfigureInputs; editorCard.Controls.Add(inputButton);
            Button delete = Theme.Button("Delete", false); delete.SetBounds(184, 327, 82, 40); delete.Click += DeleteProfile; editorCard.Controls.Add(delete);
            Button save = Theme.Button("Save profile", true); save.SetBounds(276, 327, 106, 40); save.Click += SaveProfile; editorCard.Controls.Add(save);
            Label note = Theme.Label("Profiles are always available by right-clicking the tray icon.", 8.5f, false); note.ForeColor = Theme.Muted; note.Location = new Point(36, 529); page.Controls.Add(note);
        }
        void LoadProfile(object sender, EventArgs e)
        {
            DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) return; profileName.Text = p.Name; profilePeerName.Text = p.PeerProfileName ?? ""; profileInputs = p.MonitorInputs == null ? new List<MonitorInputAssignment>() : p.MonitorInputs.Select(x => new MonitorInputAssignment { DevicePath = x.DevicePath, Input = x.Input }).ToList(); UpdateInputButton(); for (int i = 0; i < monitors.Count; i++) checks.SetItemChecked(i, p.Devices.Contains(monitors[i].DevicePath));
        }
        void ConfigureInputs(object sender, EventArgs e) { using (MonitorInputsForm form = new MonitorInputsForm(profileInputs)) if (form.ShowDialog(this) == DialogResult.OK) { profileInputs = form.Assignments; UpdateInputButton(); } }
        void UpdateInputButton() { if (inputButton != null) inputButton.Text = profileInputs.Count == 0 ? "Monitor inputs…" : "Monitor inputs (" + profileInputs.Count + ")"; }
        void SaveProfile(object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace(profileName.Text)) { MessageBox.Show(this, "Enter a profile name.", "Display profiles"); return; }
            List<string> devices = new List<string>(); for (int i = 0; i < monitors.Count; i++) if (checks.GetItemChecked(i)) devices.Add(monitors[i].DevicePath);
            if (devices.Count == 0) { MessageBox.Show(this, "Choose at least one display.", "Display profiles"); return; }
            DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) { p = new DisplayProfile(); config.Profiles.Add(p); profiles.Items.Add(p); } p.Name = profileName.Text.Trim(); p.PeerProfileName = profilePeerName.Text.Trim(); p.Devices = devices; p.MonitorInputs = profileInputs.Select(x => new MonitorInputAssignment { DevicePath = x.DevicePath, Input = x.Input }).ToList(); int selected = profiles.Items.IndexOf(p); profiles.Items.Remove(p); profiles.Items.Insert(selected, p); profiles.SelectedIndex = selected; ConfigStore.Save(config); owner.RefreshMenu();
        }
        void BuildPeer(Control page)
        {
            Label title = Theme.Label("Paired computer", 21, true); title.Location = new Point(34, 28); page.Controls.Add(title);
            Label help = Theme.Label("Coordinate display profiles with another PC on your private network.", 10, false); help.ForeColor = Theme.Muted; help.Location = new Point(36, 67); page.Controls.Add(help);
            Panel card = Theme.Card(34, 106, 632, 399); page.Controls.Add(card);
            peerEnabled = new CheckBox { Text = "Allow authenticated peer control", Checked = config.PeerEnabled, AutoSize = true, ForeColor = Theme.Text, Location = new Point(20, 18), Font = new Font("Segoe UI Semibold", 10) }; card.Controls.Add(peerEnabled);
            card.Controls.Add(At(Theme.Label("This computer", 8.5f, true), 20, 61)); deviceName = PeerText(config.DeviceName, 20, 84, 280); card.Controls.Add(deviceName);
            card.Controls.Add(At(Theme.Label("Listen port", 8.5f, true), 332, 61)); listenPort = PeerText(config.ListenPort.ToString(), 332, 84, 280); card.Controls.Add(listenPort);
            card.Controls.Add(At(Theme.Label("Paired computer address", 8.5f, true), 20, 132)); peerHost = PeerText(config.PeerHost, 20, 155, 280); card.Controls.Add(peerHost);
            card.Controls.Add(At(Theme.Label("Peer port", 8.5f, true), 332, 132)); peerPort = PeerText(config.PeerPort.ToString(), 332, 155, 280); card.Controls.Add(peerPort);
            card.Controls.Add(At(Theme.Label("Pairing key", 8.5f, true), 20, 203)); pairingKey = PeerText(PeerKeyStore.Get(), 20, 226, 438); card.Controls.Add(pairingKey);
            Button generate = Theme.Button("Generate", false); generate.SetBounds(470, 222, 142, 38); generate.Click += delegate { pairingKey.Text = PeerKeyStore.Generate(); }; card.Controls.Add(generate);
            Label keyHelp = Theme.Label("Generate on one PC, then paste the same key on the other. Windows DPAPI protects it locally.", 8.3f, false); keyHelp.ForeColor = Theme.Muted; keyHelp.Location = new Point(20, 266); card.Controls.Add(keyHelp);
            Label profileHelp = Theme.Label("In each local profile, enter the profile name that should run on the paired PC first.", 8.3f, false); profileHelp.ForeColor = Theme.Muted; profileHelp.Location = new Point(20, 293); card.Controls.Add(profileHelp);
            Button test = Theme.Button("Test connection", false); test.SetBounds(350, 335, 132, 40); test.Click += TestPeer; card.Controls.Add(test);
            Button save = Theme.Button("Save", true); save.SetBounds(494, 335, 118, 40); save.Click += SavePeer; card.Controls.Add(save);
        }
        TextBox PeerText(string value, int x, int y, int width) { return new TextBox { Text = value ?? "", BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Location = new Point(x, y), Width = width }; }
        bool StorePeerSettings()
        {
            int remote, local; if (!Int32.TryParse(peerPort.Text, out remote) || remote < 1 || remote > 65535 || !Int32.TryParse(listenPort.Text, out local) || local < 1 || local > 65535) { MessageBox.Show(this, "Enter valid TCP ports from 1 to 65535.", "Paired computer"); return false; }
            if (peerEnabled.Checked) try { if (Convert.FromBase64String(pairingKey.Text.Trim()).Length < 32) throw new FormatException(); } catch { MessageBox.Show(this, "Generate or paste a valid pairing key before enabling peer control.", "Paired computer"); return false; }
            try { PeerKeyStore.Set(pairingKey.Text.Trim()); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Paired computer"); return false; }
            config.PeerEnabled = peerEnabled.Checked; config.DeviceName = deviceName.Text.Trim(); config.PeerHost = peerHost.Text.Trim(); config.PeerPort = remote; config.ListenPort = local; ConfigStore.Save(config); try { owner.RestartPeer(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Paired computer", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; } return true;
        }
        void SavePeer(object sender, EventArgs e) { if (StorePeerSettings()) MessageBox.Show(this, "Peer settings saved.", "Paired computer"); }
        void TestPeer(object sender, EventArgs e) { if (!StorePeerSettings()) return; try { MessageBox.Show(this, "Connected to " + owner.TestPeer(), "Paired computer"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        void DeleteProfile(object sender, EventArgs e) { DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) return; config.Profiles.Remove(p); profiles.Items.Remove(p); if (config.QuickOneProfileId == p.Id) config.QuickOneProfileId = ""; if (config.QuickTwoProfileId == p.Id) config.QuickTwoProfileId = ""; ConfigStore.Save(config); owner.RegisterHotkeys(); owner.RefreshMenu(); }
        static Control At(Control c, int x, int y) { c.Location = new Point(x, y); return c; }
    }

    public sealed class AppController : ApplicationContext
    {
        public AppConfig Config { get; private set; }
        readonly NotifyIcon tray; readonly ContextMenuStrip menu; readonly HotKeyWindow hotkeys; readonly Icon icon; readonly Control dispatcher; PeerService peer;
        public AppController(bool openSettings)
        {
            Config = ConfigStore.Load(); icon = LoadIcon(); menu = new ContextMenuStrip(); tray = new NotifyIcon { Icon = icon, Text = "DisplayCue", Visible = true, ContextMenuStrip = menu }; tray.DoubleClick += delegate { OpenSettings(); };
            dispatcher = new Control(); IntPtr dispatcherHandle = dispatcher.Handle; peer = new PeerService(Config, HandlePeerCommand); try { peer.Start(); } catch (Exception ex) { tray.ShowBalloonTip(5000, "DisplayCue peer", ex.Message, ToolTipIcon.Warning); }
            hotkeys = new HotKeyWindow(); hotkeys.Pressed += delegate(int id) { if (id == 1) RunQuickAction(Config.QuickOneProfileId, "Quick action 1"); else if (id == 2) RunQuickAction(Config.QuickTwoProfileId, "Quick action 2"); }; RegisterHotkeys(); RefreshMenu();
            if (openSettings) { System.Windows.Forms.Timer startup = new System.Windows.Forms.Timer(); startup.Interval = 250; startup.Tick += delegate { startup.Stop(); startup.Dispose(); OpenSettings(); }; startup.Start(); }
        }
        Icon LoadIcon() { string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-icon.ico"); return File.Exists(p) ? new Icon(p) : SystemIcons.Application; }
        public void RefreshMenu()
        {
            menu.Items.Clear(); ToolStripMenuItem profiles = new ToolStripMenuItem("Display profiles");
            profiles.DropDownItems.Add("All displays", null, delegate { AllDisplays(); });
            if (Config.Profiles.Count > 0) profiles.DropDownItems.Add(new ToolStripSeparator());
            foreach (DisplayProfile profile in Config.Profiles) { DisplayProfile captured = profile; profiles.DropDownItems.Add(profile.Name, null, delegate { Apply(captured); }); }
            menu.Items.Add(profiles); menu.Items.Add("Identify displays", null, delegate { Identify(); }); menu.Items.Add("Settings and profiles…", null, delegate { OpenSettings(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, delegate { ExitThread(); });
        }
        public void RegisterHotkeys()
        {
            hotkeys.Remove(1); hotkeys.Remove(2); bool ok = true; uint m, k; if (!String.IsNullOrWhiteSpace(Config.QuickOneHotkey) && !String.IsNullOrWhiteSpace(Config.QuickOneProfileId)) ok = ParseHotkey(Config.QuickOneHotkey, out m, out k) && hotkeys.Add(1, m, k); uint m2, k2; if (!String.IsNullOrWhiteSpace(Config.QuickTwoHotkey) && !String.IsNullOrWhiteSpace(Config.QuickTwoProfileId)) ok = ParseHotkey(Config.QuickTwoHotkey, out m2, out k2) && hotkeys.Add(2, m2, k2) && ok;
            if (!ok) tray.ShowBalloonTip(4000, "DisplayCue", "A chosen hotkey is already in use. Choose another in Settings.", ToolTipIcon.Warning);
        }
        bool ParseHotkey(string text, out uint mods, out uint key)
        {
            mods = 0; key = 0; try { foreach (string raw in text.Split('+')) { string p = raw.Trim(); if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 2; else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 1; else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 4; else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= 8; else key = (uint)(Keys)Enum.Parse(typeof(Keys), p, true); } return key != 0; } catch { return false; }
        }
        void OpenSettings() { SettingsForm f = new SettingsForm(this); f.ShowDialog(); RefreshMenu(); }
        public void RestartPeer() { try { peer.Start(); } catch (Exception ex) { throw new InvalidOperationException("The peer listener could not start: " + ex.Message, ex); } }
        public string TestPeer()
        {
            try { return peer.Send("INFO"); }
            catch (InvalidOperationException ex) { if (ex.Message.IndexOf("closed the connection", StringComparison.OrdinalIgnoreCase) < 0 && ex.Message.IndexOf("unsupported response", StringComparison.OrdinalIgnoreCase) < 0) throw; return peer.Send("PING") + Environment.NewLine + "Connected, but the peer must be updated to show profiles and detailed errors."; }
        }
        string HandlePeerCommand(string command)
        {
            if (command == "PING") return Config.DeviceName;
            if (command == "INFO") return Config.DeviceName + Environment.NewLine + "Profiles: " + (Config.Profiles.Count == 0 ? "none" : String.Join(", ", Config.Profiles.Select(x => x.Name)));
            if (!command.StartsWith("PROFILE:")) throw new InvalidOperationException("Unsupported command.");
            string name = command.Substring(8); string result = null; using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                dispatcher.BeginInvoke((Action)delegate
                {
                    try { DisplayProfile profile = Config.Profiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (profile == null) throw new InvalidOperationException("Profile not found: " + name); ApplyLocal(profile, false); result = profile.Name; }
                    catch (Exception ex) { result = "ERROR:" + ex.Message; }
                    finally { done.Set(); }
                });
                if (!done.Wait(50000)) throw new InvalidOperationException("The display change timed out.");
            }
            if (result.StartsWith("ERROR:")) throw new InvalidOperationException(result.Substring(6)); return result;
        }
        void RunQuickAction(string profileId, string actionName)
        {
            if (profileId == "$all") { AllDisplays(); return; }
            DisplayProfile profile = Config.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (profile == null) { tray.ShowBalloonTip(3500, "DisplayCue", actionName + " has no profile assigned. Choose one in Settings.", ToolTipIcon.Info); return; }
            Apply(profile);
        }
        void AllDisplays() { try { DisplayEngine.RestoreExtended(); } catch (Exception ex) { Error(ex.Message); } }
        void Apply(DisplayProfile p)
        {
            try { if (!String.IsNullOrWhiteSpace(p.PeerProfileName)) peer.Send("PROFILE:" + p.PeerProfileName); ApplyLocal(p, true); }
            catch (Exception ex) { Error(ex.Message); }
        }
        void ApplyLocal(DisplayProfile p, bool confirm)
        {
            List<string> previousDisplays = new List<string>(); List<MonitorInputAssignment> previousInputs = new List<MonitorInputAssignment>();
            try
            {
                previousDisplays = DisplayEngine.GetMonitors().Where(x => x.Active).Select(x => x.DevicePath).ToList();
                previousInputs = ApplyMonitorInputs(p.MonitorInputs);
                DisplayEngine.ApplyDevicePaths(p.Devices);
                if (confirm) ConfirmOrRollback(previousDisplays, previousInputs, p.Name);
            }
            catch (Exception ex)
            {
                try { ApplyMonitorInputs(previousInputs); if (previousDisplays.Count > 0) DisplayEngine.ApplyDevicePaths(previousDisplays); } catch { }
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
        List<MonitorInputAssignment> ApplyMonitorInputs(IEnumerable<MonitorInputAssignment> requested)
        {
            List<MonitorInputAssignment> assignments = requested == null ? new List<MonitorInputAssignment>() : requested.ToList();
            List<MonitorInputAssignment> previous = new List<MonitorInputAssignment>(); if (assignments.Count == 0) return previous;
            List<DdcMonitorInfo> ddc = DdcEngine.Discover();
            try
            {
                foreach (MonitorInputAssignment assignment in assignments)
                {
                    DdcMonitorInfo monitor = ddc.FirstOrDefault(x => x.DevicePath.Equals(assignment.DevicePath, StringComparison.OrdinalIgnoreCase));
                    if (monitor == null || !monitor.SupportsInputSwitching) throw new InvalidOperationException("A monitor input in this profile is not currently reachable through DDC/CI.");
                    if (monitor.CurrentInput.HasValue) previous.Add(new MonitorInputAssignment { DevicePath = monitor.DevicePath, Input = monitor.CurrentInput.Value });
                }
                bool changed = false; foreach (MonitorInputAssignment assignment in assignments)
                {
                    DdcMonitorInfo monitor = ddc.First(x => x.DevicePath.Equals(assignment.DevicePath, StringComparison.OrdinalIgnoreCase));
                    if (!monitor.CurrentInput.HasValue || monitor.CurrentInput.Value != assignment.Input) { DdcEngine.SetInput(monitor, assignment.Input); changed = true; Thread.Sleep(250); }
                }
                if (changed) Thread.Sleep(1250);
                return previous;
            }
            catch
            {
                foreach (MonitorInputAssignment restore in previous) try { DdcMonitorInfo monitor = ddc.FirstOrDefault(x => x.DevicePath.Equals(restore.DevicePath, StringComparison.OrdinalIgnoreCase)); if (monitor != null) DdcEngine.SetInput(monitor, restore.Input); } catch { }
                throw;
            }
            finally { DdcEngine.Release(ddc); }
        }
        void ConfirmOrRollback(List<string> previousDisplays, List<MonitorInputAssignment> previousInputs, string name)
        {
            using (ConfirmLayoutForm confirm = new ConfirmLayoutForm(name)) if (confirm.ShowDialog() != DialogResult.Yes) try { ApplyMonitorInputs(previousInputs); DisplayEngine.ApplyDevicePaths(previousDisplays); } catch { }
        }
        public void Identify()
        {
            List<MonitorInfo> monitors = DisplayEngine.GetMonitors(); List<IdentifyOverlay> overlays = new List<IdentifyOverlay>();
            for (int i = 0; i < monitors.Count; i++) { MonitorInfo m = monitors[i]; if (!m.Active) continue; Screen s = Screen.AllScreens.FirstOrDefault(x => x.DeviceName.Equals(m.GdiName, StringComparison.OrdinalIgnoreCase)); if (s == null) continue; IdentifyOverlay o = new IdentifyOverlay(i + 1, m.Name, s); overlays.Add(o); o.Show(); }
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer(); timer.Interval = 3500; timer.Tick += delegate { timer.Stop(); foreach (IdentifyOverlay o in overlays) o.Close(); timer.Dispose(); }; timer.Start();
        }
        void Error(string message) { MessageBox.Show(message, "DisplayCue", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected override void ExitThreadCore() { peer.Dispose(); dispatcher.Dispose(); tray.Visible = false; tray.Dispose(); hotkeys.Dispose(); if (icon != SystemIcons.Application) icon.Dispose(); base.ExitThreadCore(); }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            int reportIndex = Array.FindIndex(args, x => x.Equals("--ddc-report", StringComparison.OrdinalIgnoreCase));
            if (reportIndex >= 0)
            {
                string reportPath = reportIndex + 1 < args.Length ? args[reportIndex + 1] : Path.Combine(ConfigStore.ConfigDirectory, "ddc-report.txt");
                List<DdcMonitorInfo> monitors = DdcEngine.Discover();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                    using (StreamWriter writer = new StreamWriter(reportPath, false))
                    {
                        writer.WriteLine("DisplayCue DDC/CI report"); writer.WriteLine("Generated: " + DateTimeOffset.Now.ToString("O")); writer.WriteLine();
                        foreach (DdcMonitorInfo monitor in monitors)
                        {
                            writer.WriteLine(monitor.Name); writer.WriteLine("  Device: " + monitor.DevicePath); writer.WriteLine("  Transport: " + (monitor.Handle == IntPtr.Zero ? "Unavailable" : "DDC/CI"));
                            writer.WriteLine("  Current input: " + (monitor.CurrentInput.HasValue ? DdcEngine.InputName(monitor.CurrentInput.Value) + " (0x" + monitor.CurrentInput.Value.ToString("X2") + ")" : "Unknown"));
                            writer.WriteLine("  Inputs: " + (monitor.Inputs.Count == 0 ? "None advertised" : String.Join(", ", monitor.Inputs.Select(x => x.Name))));
                            if (!String.IsNullOrWhiteSpace(monitor.Error)) writer.WriteLine("  Error: " + monitor.Error); writer.WriteLine();
                        }
                    }
                }
                finally { DdcEngine.Release(monitors); }
                return;
            }
            bool created; using (Mutex mutex = new Mutex(true, "DisplayCue.V1.Singleton", out created))
            {
                if (!created) { MessageBox.Show("DisplayCue is already running in the notification area.", "DisplayCue"); return; }
                bool openSettings = args.Any(x => x.Equals("--settings", StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "open-settings.flag"));
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new AppController(openSettings));
            }
        }
    }
}
