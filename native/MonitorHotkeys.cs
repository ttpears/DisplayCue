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
        [DataMember] public string TvMatch = "TV";
        [DataMember] public string TvOnlyHotkey = "Ctrl+Shift+F11";
        [DataMember] public string AllHotkey = "Ctrl+Shift+F12";
        [DataMember] public List<DisplayProfile> Profiles = new List<DisplayProfile>();
    }

    [DataContract]
    public sealed class DisplayProfile
    {
        [DataMember] public string Name = "New profile";
        [DataMember] public List<string> Devices = new List<string>();
        [DataMember] public List<MonitorInputAssignment> MonitorInputs = new List<MonitorInputAssignment>();
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
                        foreach (DisplayProfile p in c.Profiles) if (p.MonitorInputs == null) p.MonitorInputs = new List<MonitorInputAssignment>();
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
            int e = SetDisplayConfig(0, null, 0, null, SDC_APPLY | 0x4); if (e != 0) throw new InvalidOperationException("Windows could not restore the extended desktop (error " + e + ").");
        }
        public static void ApplyDevicePaths(IEnumerable<string> devicePaths)
        {
            HashSet<string> wantedDevices = new HashSet<string>(devicePaths, StringComparer.OrdinalIgnoreCase);
            RestoreExtended(); Thread.Sleep(800);
            List<MonitorInfo> monitors = GetMonitors(); HashSet<string> wantedKeys = new HashSet<string>(monitors.Where(x => wantedDevices.Contains(x.DevicePath)).Select(x => x.Key));
            if (wantedKeys.Count == 0) throw new InvalidOperationException("None of the displays in this profile are connected.");
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
        readonly AppController owner; readonly AppConfig config; readonly List<MonitorInfo> monitors;
        ComboBox tv; TextBox tvHotkey, allHotkey; ListBox profiles; CheckedListBox checks; TextBox profileName; Panel content; Button generalNav, profilesNav;
        List<MonitorInputAssignment> profileInputs = new List<MonitorInputAssignment>(); Button inputButton;
        public SettingsForm(AppController owner)
        {
            this.owner = owner; config = owner.Config; monitors = DisplayEngine.GetMonitors();
            Text = "DisplayCue Settings"; ClientSize = new Size(900, 620); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; BackColor = Theme.Back; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = Theme.Sidebar, Padding = new Padding(16, 22, 16, 16) }; Controls.Add(sidebar);
            Label brand = Theme.Label("DisplayCue", 18, true); brand.Location = new Point(18, 20); sidebar.Controls.Add(brand);
            Label tagline = Theme.Label("Displays, on cue.", 9, false); tagline.ForeColor = Theme.Muted; tagline.Location = new Point(20, 54); sidebar.Controls.Add(tagline);
            generalNav = NavButton("Quick switching", 94); generalNav.Click += delegate { ShowPage(false); }; sidebar.Controls.Add(generalNav);
            profilesNav = NavButton("Display profiles", 142); profilesNav.Click += delegate { ShowPage(true); }; sidebar.Controls.Add(profilesNav);
            Label version = Theme.Label("Version " + Application.ProductVersion, 8.5f, false); version.ForeColor = Theme.Muted; version.Location = new Point(20, 566); sidebar.Controls.Add(version);
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back }; Controls.Add(content); content.BringToFront(); ShowPage(false);
        }
        Button NavButton(string text, int y) { Button b = new Button { Text = text, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(12, y), Size = new Size(172, 42), Padding = new Padding(12, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Theme.Sidebar, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9.5f) }; b.FlatAppearance.BorderSize = 0; return b; }
        void ShowPage(bool showProfiles)
        {
            content.Controls.Clear(); generalNav.BackColor = showProfiles ? Theme.Sidebar : Theme.Panel; generalNav.ForeColor = showProfiles ? Theme.Muted : Theme.Text; profilesNav.BackColor = showProfiles ? Theme.Panel : Theme.Sidebar; profilesNav.ForeColor = showProfiles ? Theme.Text : Theme.Muted;
            if (showProfiles) BuildProfiles(content); else BuildGeneral(content);
        }
        void BuildGeneral(Control page)
        {
            Label title = Theme.Label("Quick switching", 21, true); title.Location = new Point(34, 28); page.Controls.Add(title);
            Label help = Theme.Label("Set up the two display changes you use most.", 10, false); help.ForeColor = Theme.Muted; help.Location = new Point(36, 67); page.Controls.Add(help);
            Button identify = Theme.Button("Identify displays", false); identify.SetBounds(518, 29, 148, 40); identify.Click += delegate { owner.Identify(); }; page.Controls.Add(identify);
            Panel displayCard = Theme.Card(34, 106, 632, 136); page.Controls.Add(displayCard);
            displayCard.Controls.Add(At(Theme.Label("TV-only display", 11, true), 20, 18));
            Label displayHelp = Theme.Label("This is the one screen that stays on when you choose TV only.", 9, false); displayHelp.ForeColor = Theme.Muted; displayHelp.Location = new Point(20, 45); displayCard.Controls.Add(displayHelp);
            tv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Input, ForeColor = Theme.Text, Width = 592, Location = new Point(20, 79), Font = new Font("Segoe UI", 10.5f), FlatStyle = FlatStyle.Flat };
            for (int i = 0; i < monitors.Count; i++) tv.Items.Add((i + 1) + ". " + monitors[i].Name + (monitors[i].Active ? "  ·  On" : "  ·  Off"));
            int selected = monitors.FindIndex(x => x.Name == config.TvMatch); tv.SelectedIndex = selected >= 0 ? selected : (tv.Items.Count > 0 ? 0 : -1); displayCard.Controls.Add(tv);
            Panel hotkeyCard = Theme.Card(34, 258, 632, 204); page.Controls.Add(hotkeyCard);
            hotkeyCard.Controls.Add(At(Theme.Label("Keyboard shortcuts", 11, true), 20, 18));
            Label hotkeyHelp = Theme.Label("Click a field, then press a key combination. Leave it blank to disable it.", 9, false); hotkeyHelp.ForeColor = Theme.Muted; hotkeyHelp.Location = new Point(20, 45); hotkeyCard.Controls.Add(hotkeyHelp);
            hotkeyCard.Controls.Add(At(Theme.Label("TV only", 9, true), 20, 82)); tvHotkey = HotkeyBox(config.TvOnlyHotkey, 20, 108); tvHotkey.Width = 280; hotkeyCard.Controls.Add(tvHotkey);
            hotkeyCard.Controls.Add(At(Theme.Label("All displays", 9, true), 332, 82)); allHotkey = HotkeyBox(config.AllHotkey, 332, 108); allHotkey.Width = 280; hotkeyCard.Controls.Add(allHotkey);
            Label note = Theme.Label("More display combinations are available from the tray menu.", 8.5f, false); note.ForeColor = Theme.Muted; note.Location = new Point(20, 157); hotkeyCard.Controls.Add(note);
            Button cancel = Theme.Button("Cancel", false); cancel.SetBounds(424, 527, 104, 42); cancel.Click += delegate { Close(); }; page.Controls.Add(cancel);
            Button save = Theme.Button("Save changes", true); save.SetBounds(540, 527, 126, 42); save.Click += SaveGeneral; page.Controls.Add(save);
        }
        TextBox HotkeyBox(string value, int x, int y)
        {
            TextBox box = new TextBox { ReadOnly = true, Text = value, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Location = new Point(x, y), Width = 320 };
            box.KeyDown += delegate(object s, KeyEventArgs e) { List<string> p = new List<string>(); if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape) { ((TextBox)s).Text = ""; e.SuppressKeyPress = true; return; } if (e.Control) p.Add("Ctrl"); if (e.Alt) p.Add("Alt"); if (e.Shift) p.Add("Shift"); if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.ShiftKey) p.Add(e.KeyCode.ToString()); if (p.Count > 1) ((TextBox)s).Text = String.Join("+", p); e.SuppressKeyPress = true; };
            return box;
        }
        void SaveGeneral(object sender, EventArgs e)
        {
            if (tv.SelectedIndex >= 0) config.TvMatch = monitors[tv.SelectedIndex].Name; config.TvOnlyHotkey = tvHotkey.Text; config.AllHotkey = allHotkey.Text; ConfigStore.Save(config); owner.RegisterHotkeys(); Close();
        }
        void BuildProfiles(Control page)
        {
            Label title = Theme.Label("Display profiles", 21, true); title.Location = new Point(34, 28); page.Controls.Add(title);
            Label help = Theme.Label("Save any display combination and launch it from the tray.", 10, false); help.ForeColor = Theme.Muted; help.Location = new Point(36, 67); page.Controls.Add(help);
            Button identify = Theme.Button("Identify displays", false); identify.SetBounds(518, 29, 148, 40); identify.Click += delegate { owner.Identify(); }; page.Controls.Add(identify);
            Panel listCard = Theme.Card(34, 106, 216, 399); page.Controls.Add(listCard); listCard.Controls.Add(At(Theme.Label("Your profiles", 11, true), 16, 15));
            profiles = new ListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), Location = new Point(16, 50), Size = new Size(184, 282), IntegralHeight = false }; foreach (DisplayProfile p in config.Profiles) profiles.Items.Add(p); profiles.SelectedIndexChanged += LoadProfile; listCard.Controls.Add(profiles);
            Button add = Theme.Button("New profile", false); add.SetBounds(16, 345, 184, 38); add.Click += delegate { profiles.ClearSelected(); profileName.Text = ""; profileInputs = new List<MonitorInputAssignment>(); UpdateInputButton(); for (int i = 0; i < checks.Items.Count; i++) checks.SetItemChecked(i, false); }; listCard.Controls.Add(add);
            Panel editorCard = Theme.Card(266, 106, 400, 399); page.Controls.Add(editorCard); editorCard.Controls.Add(At(Theme.Label("Profile details", 11, true), 18, 15));
            editorCard.Controls.Add(At(Theme.Label("Name", 9, true), 18, 53));
            profileName = new TextBox { BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Location = new Point(18, 78), Width = 364 }; editorCard.Controls.Add(profileName);
            editorCard.Controls.Add(At(Theme.Label("Displays to keep active", 9, true), 18, 119));
            checks = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), Location = new Point(18, 145), Size = new Size(364, 165) };
            Dictionary<string, int> totals = monitors.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.Count()); Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int i = 0; i < monitors.Count; i++) { MonitorInfo m = monitors[i]; if (!seen.ContainsKey(m.Name)) seen[m.Name] = 0; seen[m.Name]++; string suffix = totals[m.Name] > 1 ? " #" + seen[m.Name] : ""; checks.Items.Add((i + 1) + ". " + m.Name + suffix + (m.Active ? "  ·  On" : "  ·  Off")); } editorCard.Controls.Add(checks);
            inputButton = Theme.Button("Monitor inputs…", false); inputButton.SetBounds(18, 315, 154, 40); inputButton.Click += ConfigureInputs; editorCard.Controls.Add(inputButton);
            Button delete = Theme.Button("Delete", false); delete.SetBounds(184, 315, 82, 40); delete.Click += DeleteProfile; editorCard.Controls.Add(delete);
            Button save = Theme.Button("Save profile", true); save.SetBounds(276, 315, 106, 40); save.Click += SaveProfile; editorCard.Controls.Add(save);
            Label note = Theme.Label("Profiles are always available by right-clicking the tray icon.", 8.5f, false); note.ForeColor = Theme.Muted; note.Location = new Point(36, 529); page.Controls.Add(note);
        }
        void LoadProfile(object sender, EventArgs e)
        {
            DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) return; profileName.Text = p.Name; profileInputs = p.MonitorInputs == null ? new List<MonitorInputAssignment>() : p.MonitorInputs.Select(x => new MonitorInputAssignment { DevicePath = x.DevicePath, Input = x.Input }).ToList(); UpdateInputButton(); for (int i = 0; i < monitors.Count; i++) checks.SetItemChecked(i, p.Devices.Contains(monitors[i].DevicePath));
        }
        void ConfigureInputs(object sender, EventArgs e) { using (MonitorInputsForm form = new MonitorInputsForm(profileInputs)) if (form.ShowDialog(this) == DialogResult.OK) { profileInputs = form.Assignments; UpdateInputButton(); } }
        void UpdateInputButton() { if (inputButton != null) inputButton.Text = profileInputs.Count == 0 ? "Monitor inputs…" : "Monitor inputs (" + profileInputs.Count + ")"; }
        void SaveProfile(object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace(profileName.Text)) { MessageBox.Show(this, "Enter a profile name.", "Display profiles"); return; }
            List<string> devices = new List<string>(); for (int i = 0; i < monitors.Count; i++) if (checks.GetItemChecked(i)) devices.Add(monitors[i].DevicePath);
            if (devices.Count == 0) { MessageBox.Show(this, "Choose at least one display.", "Display profiles"); return; }
            DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) { p = new DisplayProfile(); config.Profiles.Add(p); profiles.Items.Add(p); } p.Name = profileName.Text.Trim(); p.Devices = devices; p.MonitorInputs = profileInputs.Select(x => new MonitorInputAssignment { DevicePath = x.DevicePath, Input = x.Input }).ToList(); profiles.Refresh(); ConfigStore.Save(config); owner.RefreshMenu();
        }
        void DeleteProfile(object sender, EventArgs e) { DisplayProfile p = profiles.SelectedItem as DisplayProfile; if (p == null) return; config.Profiles.Remove(p); profiles.Items.Remove(p); ConfigStore.Save(config); owner.RefreshMenu(); }
        static Control At(Control c, int x, int y) { c.Location = new Point(x, y); return c; }
    }

    public sealed class AppController : ApplicationContext
    {
        public AppConfig Config { get; private set; }
        readonly NotifyIcon tray; readonly ContextMenuStrip menu; readonly HotKeyWindow hotkeys; readonly Icon icon;
        public AppController(bool openSettings)
        {
            Config = ConfigStore.Load(); icon = LoadIcon(); menu = new ContextMenuStrip(); tray = new NotifyIcon { Icon = icon, Text = "DisplayCue", Visible = true, ContextMenuStrip = menu }; tray.DoubleClick += delegate { OpenSettings(); };
            hotkeys = new HotKeyWindow(); hotkeys.Pressed += delegate(int id) { if (id == 1) TvOnly(); else if (id == 2) AllDisplays(); }; RegisterHotkeys(); RefreshMenu();
            if (openSettings) { System.Windows.Forms.Timer startup = new System.Windows.Forms.Timer(); startup.Interval = 250; startup.Tick += delegate { startup.Stop(); startup.Dispose(); OpenSettings(); }; startup.Start(); }
        }
        Icon LoadIcon() { string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-icon.ico"); return File.Exists(p) ? new Icon(p) : SystemIcons.Application; }
        public void RefreshMenu()
        {
            menu.Items.Clear(); ToolStripMenuItem profiles = new ToolStripMenuItem("Display profiles");
            profiles.DropDownItems.Add("TV only", null, delegate { TvOnly(); }); profiles.DropDownItems.Add("All displays", null, delegate { AllDisplays(); });
            if (Config.Profiles.Count > 0) profiles.DropDownItems.Add(new ToolStripSeparator());
            foreach (DisplayProfile profile in Config.Profiles) { DisplayProfile captured = profile; profiles.DropDownItems.Add(profile.Name, null, delegate { Apply(captured); }); }
            menu.Items.Add(profiles); menu.Items.Add("Identify displays", null, delegate { Identify(); }); menu.Items.Add("Settings and profiles…", null, delegate { OpenSettings(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, delegate { ExitThread(); });
        }
        public void RegisterHotkeys()
        {
            hotkeys.Remove(1); hotkeys.Remove(2); bool ok = true; uint m, k; if (!String.IsNullOrWhiteSpace(Config.TvOnlyHotkey)) ok = ParseHotkey(Config.TvOnlyHotkey, out m, out k) && hotkeys.Add(1, m, k); uint m2, k2; if (!String.IsNullOrWhiteSpace(Config.AllHotkey)) ok = ParseHotkey(Config.AllHotkey, out m2, out k2) && hotkeys.Add(2, m2, k2) && ok;
            if (!ok) tray.ShowBalloonTip(4000, "DisplayCue", "A chosen hotkey is already in use. Choose another in Settings.", ToolTipIcon.Warning);
        }
        bool ParseHotkey(string text, out uint mods, out uint key)
        {
            mods = 0; key = 0; try { foreach (string raw in text.Split('+')) { string p = raw.Trim(); if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 2; else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 1; else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 4; else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= 8; else key = (uint)(Keys)Enum.Parse(typeof(Keys), p, true); } return key != 0; } catch { return false; }
        }
        void OpenSettings() { SettingsForm f = new SettingsForm(this); f.ShowDialog(); RefreshMenu(); }
        void TvOnly()
        {
            List<MonitorInfo> m = DisplayEngine.GetMonitors(); MonitorInfo tv = m.FirstOrDefault(x => x.Name == Config.TvMatch); if (tv == null) { Error("The selected TV is not connected."); return; } Apply(new DisplayProfile { Name = "TV only", Devices = new List<string> { tv.DevicePath } });
        }
        void AllDisplays() { try { DisplayEngine.RestoreExtended(); } catch (Exception ex) { Error(ex.Message); } }
        void Apply(DisplayProfile p)
        {
            List<string> previousDisplays = new List<string>(); List<MonitorInputAssignment> previousInputs = new List<MonitorInputAssignment>();
            try
            {
                previousDisplays = DisplayEngine.GetMonitors().Where(x => x.Active).Select(x => x.DevicePath).ToList();
                previousInputs = ApplyMonitorInputs(p.MonitorInputs);
                DisplayEngine.ApplyDevicePaths(p.Devices);
                ConfirmOrRollback(previousDisplays, previousInputs, p.Name);
            }
            catch (Exception ex)
            {
                try { if (previousDisplays.Count > 0) DisplayEngine.ApplyDevicePaths(previousDisplays); ApplyMonitorInputs(previousInputs); } catch { }
                Error(ex.Message);
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
                foreach (MonitorInputAssignment assignment in assignments)
                {
                    DdcMonitorInfo monitor = ddc.First(x => x.DevicePath.Equals(assignment.DevicePath, StringComparison.OrdinalIgnoreCase));
                    if (!monitor.CurrentInput.HasValue || monitor.CurrentInput.Value != assignment.Input) { DdcEngine.SetInput(monitor, assignment.Input); Thread.Sleep(180); }
                }
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
            using (ConfirmLayoutForm confirm = new ConfirmLayoutForm(name)) if (confirm.ShowDialog() != DialogResult.Yes) try { DisplayEngine.ApplyDevicePaths(previousDisplays); ApplyMonitorInputs(previousInputs); } catch { }
        }
        public void Identify()
        {
            List<MonitorInfo> monitors = DisplayEngine.GetMonitors(); List<IdentifyOverlay> overlays = new List<IdentifyOverlay>();
            for (int i = 0; i < monitors.Count; i++) { MonitorInfo m = monitors[i]; if (!m.Active) continue; Screen s = Screen.AllScreens.FirstOrDefault(x => x.DeviceName.Equals(m.GdiName, StringComparison.OrdinalIgnoreCase)); if (s == null) continue; IdentifyOverlay o = new IdentifyOverlay(i + 1, m.Name, s); overlays.Add(o); o.Show(); }
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer(); timer.Interval = 3500; timer.Tick += delegate { timer.Stop(); foreach (IdentifyOverlay o in overlays) o.Close(); timer.Dispose(); }; timer.Start();
        }
        void Error(string message) { MessageBox.Show(message, "DisplayCue", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected override void ExitThreadCore() { tray.Visible = false; tray.Dispose(); hotkeys.Dispose(); if (icon != SystemIcons.Application) icon.Dispose(); base.ExitThreadCore(); }
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
