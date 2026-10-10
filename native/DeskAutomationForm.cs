using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorHotkeys
{
    public sealed class DeskAutomationForm : Form
    {
        readonly AppController app;
        readonly ListBox desks;
        readonly TextBox name, returnProfile;
        readonly ComboBox profile, dock;
        readonly CheckBox enabled;
        readonly Label captured;
        readonly Button save;
        List<string> displayPaths = new List<string>();
        public DeskAutomationForm(AppController app)
        {
            this.app = app; Text = "DisplayCue · Automatic desks"; ClientSize = new Size(760, 640); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Back; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
            Label title = Theme.Label("Automatic desks", 21, true); title.Location = new Point(24, 20); Controls.Add(title);
            Label help = Theme.Label("First apply a saved profile, choose Keep, then use Undo to test recovery.", 9, false); help.Location = new Point(26, 65); Controls.Add(help);
            desks = new ListBox { Location = new Point(24, 104), Size = new Size(200, 420), BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
            foreach (DeskRule desk in app.Config.Desks) desks.Items.Add(desk); desks.SelectedIndexChanged += LoadDesk; Controls.Add(desks);
            Button add = Theme.Button("New desk", false); add.SetBounds(24, 536, 96, 38); add.Click += delegate { desks.ClearSelected(); name.Text = ""; returnProfile.Text = ""; profile.SelectedIndex = -1; dock.SelectedIndex = 0; enabled.Checked = false; displayPaths = new List<string>(); UpdateCaptured(); }; Controls.Add(add);
            Button delete = Theme.Button("Delete", false); delete.SetBounds(128, 536, 96, 38); delete.Click += delegate { if (app.IsBusy) return; DeskRule desk = desks.SelectedItem as DeskRule; if (desk != null) { app.Config.Desks.Remove(desk); desks.Items.Remove(desk); ConfigStore.Save(app.Config); } }; Controls.Add(delete);
            Controls.Add(LabelAt("Desk name", 246, 104)); name = TextAt(246, 130, 488); Controls.Add(name);
            Controls.Add(LabelAt("Profile to apply on this computer", 246, 174));
            profile = ComboAt(246, 200, 488); foreach (DisplayProfile item in app.Config.Profiles) profile.Items.Add(item); Controls.Add(profile);
            Controls.Add(LabelAt("Dock connection", 246, 244)); dock = ComboAt(246, 270, 488); dock.Items.Add(new DockConnection { Id = "", Name = "Recognize by connected monitors" });
            foreach (DockConnection connection in DockProbe.Connected()) dock.Items.Add(connection); dock.SelectedIndex = 0; Controls.Add(dock);
            Controls.Add(LabelAt("Connected monitors", 246, 314)); captured = Theme.Label("", 9, false); captured.Location = new Point(246, 340); Controls.Add(captured);
            Button capture = Theme.Button("Recognize this desk", false); capture.SetBounds(246, 368, 190, 38); capture.Click += delegate {
                displayPaths = DisplayEngine.GetMonitors().Where(x => !x.Internal).Select(x => x.DevicePath).ToList(); UpdateCaptured();
                if (displayPaths.Count == 0) MessageBox.Show(this, "Connect this desk's monitors before saving its identity.", "Automatic desks");
            }; Controls.Add(capture);
            Controls.Add(LabelAt("Desktop profile to restore after undocking (optional)", 246, 422)); returnProfile = TextAt(246, 448, 488); Controls.Add(returnProfile);
            enabled = new CheckBox { Text = "Enable automatic switching for this desk", AutoSize = true, Location = new Point(246, 492), ForeColor = Theme.Text }; Controls.Add(enabled);
            Label lease = new Label { Text = "Automatic return needs a dock connection and a tested local profile on the desktop.\nThe desktop returns after undocking or 30 seconds without a connection heartbeat.", Location = new Point(246, 528), Size = new Size(488, 50), ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f) }; Controls.Add(lease);
            save = Theme.Button("Save desk", true); save.SetBounds(594, 588, 140, 38); save.Click += SaveDesk; Controls.Add(save);
            if (desks.Items.Count > 0) desks.SelectedIndex = 0; else UpdateCaptured();
        }
        static Label LabelAt(string text, int x, int y) { Label label = Theme.Label(text, 9, true); label.Location = new Point(x, y); return label; }
        static TextBox TextAt(int x, int y, int width) { return new TextBox { Location = new Point(x, y), Width = width, BackColor = Theme.Input, ForeColor = Theme.Text }; }
        static ComboBox ComboAt(int x, int y, int width) { return new ComboBox { Location = new Point(x, y), Width = width, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Input, ForeColor = Theme.Text }; }
        void UpdateCaptured() { captured.Text = displayPaths.Count + " external monitor" + (displayPaths.Count == 1 ? "" : "s") + " saved for recognition"; }
        void LoadDesk(object sender, EventArgs args)
        {
            DeskRule desk = desks.SelectedItem as DeskRule; if (desk == null) return;
            name.Text = desk.Name; returnProfile.Text = desk.ReturnProfileName; enabled.Checked = desk.Enabled; displayPaths = new List<string>(desk.DisplayPaths ?? new List<string>());
            profile.SelectedIndex = -1; for (int i = 0; i < profile.Items.Count; i++) if (((DisplayProfile)profile.Items[i]).Id == desk.ProfileId) profile.SelectedIndex = i;
            dock.SelectedIndex = 0; bool found = String.IsNullOrEmpty(desk.DockAnchor);
            for (int i = 1; i < dock.Items.Count; i++) if (((DockConnection)dock.Items[i]).Id == desk.DockAnchor) { dock.SelectedIndex = i; found = true; }
            if (!found) { dock.Items.Add(new DockConnection { Id = desk.DockAnchor, Name = "Saved dock connection (disconnected)" }); dock.SelectedIndex = dock.Items.Count - 1; }
            UpdateCaptured();
        }
        async void SaveDesk(object sender, EventArgs args)
        {
            if (app.IsBusy) { MessageBox.Show(this, "Wait for the display change to finish."); return; }
            DisplayProfile selected = profile.SelectedItem as DisplayProfile;
            DockConnection connection = dock.SelectedItem as DockConnection;
            if (String.IsNullOrWhiteSpace(name.Text) || selected == null || displayPaths.Count == 0) { MessageBox.Show(this, "Name this desk, choose its profile, and recognize its connected monitors."); return; }
            if (enabled.Checked && !app.IsCalibrated(selected)) { MessageBox.Show(this, "Capture this profile's layout, apply it manually and choose Keep, then test Undo before enabling automation."); return; }
            string returning = returnProfile.Text.Trim();
            if (!String.IsNullOrEmpty(returning) && (String.IsNullOrEmpty(connection.Id) || String.IsNullOrWhiteSpace(selected.PeerProfileName))) { MessageBox.Show(this, "Automatic desktop return requires a dock connection and a paired takeover profile."); return; }
            DeskRule existing = desks.SelectedItem as DeskRule;
            if (app.Config.Desks.Any(x => x != existing && new HashSet<string>(x.DisplayPaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase).SetEquals(displayPaths) && String.Equals(x.DockAnchor, connection.Id, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "This connection already belongs to another desk. Edit that desk instead."); return; }
            save.Enabled = false;
            try
            {
                if (enabled.Checked && returning.Length > 0) await Task.Run(delegate { app.CheckReturnProfile(returning); });
                if (app.IsBusy) throw new InvalidOperationException("A peer change began while saving. Try again after it finishes.");
                DeskRule desk = existing ?? new DeskRule(); desk.Name = name.Text.Trim(); desk.ProfileId = selected.Id; desk.ReturnProfileName = returning; desk.DockAnchor = connection.Id; desk.DisplayPaths = new List<string>(displayPaths); desk.Enabled = enabled.Checked;
                if (existing == null) app.Config.Desks.Add(desk);
                ConfigStore.Save(app.Config); Close();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Automatic desks", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { save.Enabled = true; }
        }
    }
}
