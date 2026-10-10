using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorHotkeys
{
    [DataContract]
    public sealed class DeskRule
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N"), Name = "New desk", ProfileId = "", ReturnProfileName = "", DockAnchor = "";
        [DataMember] public List<string> DisplayPaths = new List<string>();
        [DataMember] public bool Enabled;
        public override string ToString() { return Name; }
    }

    public sealed class DeskObservation
    {
        public List<string> ExternalDisplays = new List<string>(), DockAnchors = new List<string>();
    }

    public static class DeskDetection
    {
        public static DeskRule Select(IEnumerable<DeskRule> rules, DeskObservation observation)
        {
            List<DeskRule> candidates = rules.Where(x => x.Enabled && x.DisplayPaths != null && x.DisplayPaths.Count > 0).ToList();
            List<DeskRule> matches = candidates.Where(x => (String.IsNullOrEmpty(x.DockAnchor) || observation.DockAnchors.Contains(x.DockAnchor, StringComparer.OrdinalIgnoreCase))
                && new HashSet<string>(x.DisplayPaths, StringComparer.OrdinalIgnoreCase).SetEquals(observation.ExternalDisplays)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1 || observation.ExternalDisplays.Count > 0) return null;
            // A dock can be present before its video paths appear. A unique physical
            // anchor allows the source PC to switch inputs and bring those paths back.
            matches = candidates.Where(x => !String.IsNullOrEmpty(x.DockAnchor) && observation.DockAnchors.Contains(x.DockAnchor, StringComparer.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
    }

    public sealed class DockConnection
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }

    public static class DockProbe
    {
        [StructLayout(LayoutKind.Sequential)] struct DEVICE { public uint Size; public Guid Class; public uint DevInst; public IntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DEVICE device);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DEVICE device, StringBuilder id, uint length, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DEVICE device, uint property, out uint type, byte[] buffer, uint length, out uint required);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        public static List<DockConnection> Connected()
        {
            List<DockConnection> result = new List<DockConnection>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, 2 | 4);
            if (set == new IntPtr(-1)) throw new InvalidOperationException("Windows could not read USB connections.");
            try
            {
                for (uint index = 0; ; index++)
                {
                    DEVICE device = new DEVICE { Size = (uint)Marshal.SizeOf(typeof(DEVICE)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref device)) { if (Marshal.GetLastWin32Error() != 259) throw new InvalidOperationException("USB connection enumeration failed."); break; }
                    StringBuilder id = new StringBuilder(1024); uint required;
                    if (!SetupDiGetDeviceInstanceId(set, ref device, id, 1024, out required) || !id.ToString().StartsWith("USB\\VID_17EF", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] buffer = new byte[2048]; uint type;
                    string name = SetupDiGetDeviceRegistryProperty(set, ref device, 12, out type, buffer, (uint)buffer.Length, out required) || SetupDiGetDeviceRegistryProperty(set, ref device, 0, out type, buffer, (uint)buffer.Length, out required)
                        ? Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, (uint)buffer.Length)).TrimEnd('\0') : "Lenovo USB connection";
                    result.Add(new DockConnection { Id = id.ToString(), Name = name + " · " + id.ToString().Split('\\').Last() });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result.OrderBy(x => x.Name).ToList();
        }
        public static DeskObservation Observe() { return new DeskObservation { ExternalDisplays = DisplayEngine.GetMonitors().Where(x => !x.Internal).Select(x => x.DevicePath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), DockAnchors = Connected().Select(x => x.Id).ToList() }; }
    }

    public sealed partial class AppController
    {
        System.Windows.Forms.Timer automationTimer; int observing;
        string candidateDesk = "", activeDesk = "", suppressedDesk = ""; int stableSamples, detachedSamples;
        DeskRule sendingDesk; string sendingToken; DateTime lastRenewal;
        sealed class ReturnLease { public string Token, ProfileName, ProfileDigest; public DateTime Expires; }
        ReturnLease returnLease;
        bool shuttingDown;

        void StartAutomation()
        {
            automationTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            automationTimer.Tick += async delegate {
                if (shuttingDown || Interlocked.Exchange(ref observing, 1) != 0) return;
                try
                {
                    await Task.Run(RecoverExpiredTransaction);
                    if (IsBusy || Application.OpenForms.OfType<SettingsForm>().Any() || Application.OpenForms.OfType<DeskAutomationForm>().Any()) return;
                    await CheckReturnLease();
                    bool hasRules = Config.Desks.Any(x => x.Enabled) || sendingDesk != null || !String.IsNullOrEmpty(suppressedDesk);
                    if (hasRules) await ObserveDesk(await Task.Run(DockProbe.Observe));
                }
                catch (Exception error) { PeerDiagnostics.Write("AUTOMATION ERROR " + error.Message); }
                finally { Interlocked.Exchange(ref observing, 0); }
            };
            automationTimer.Start();
        }
        void StopAutomation() { shuttingDown = true; if (automationTimer != null) { automationTimer.Stop(); automationTimer.Dispose(); } }
        void RecoverExpiredTransaction()
        {
            lock (transitionSync)
            {
                if (inboundTransition == null || inboundTransition.Committed || DateTime.UtcNow - inboundTransition.StartedUtc < TimeSpan.FromMinutes(2)) return;
                NativeParticipant participant = new NativeParticipant(this, inboundTransition.Id, inboundTransition.Profile) { State = inboundTransition };
                List<string> errors = new List<string>(); HandoffSequence.Restore(participant, null, errors);
                inboundTransition = null; activeTransitionId = null;
                if (errors.Count > 0) Notify("Recovery needs attention", String.Join("; ", errors));
            }
        }
        async Task ObserveDesk(DeskObservation observation)
        {
            // Keep session ownership tied to the dock even if a monitor temporarily
            // disappears when its input changes or when the user chooses the desktop.
            DeskRule session = Config.Desks.FirstOrDefault(x => x.Id == activeDesk);
            if (session != null && !String.IsNullOrEmpty(session.DockAnchor))
                detachedSamples = observation.DockAnchors.Contains(session.DockAnchor, StringComparer.OrdinalIgnoreCase) ? 0 : detachedSamples + 1;
            else if (session != null && !session.DisplayPaths.Any(x => observation.ExternalDisplays.Contains(x, StringComparer.OrdinalIgnoreCase)))
                detachedSamples++;
            else detachedSamples = 0;
            if (detachedSamples >= 3)
            {
                await ReleaseDeskReturn(); activeDesk = ""; suppressedDesk = ""; candidateDesk = ""; stableSamples = 0; detachedSamples = 0;
            }
            if (sendingDesk != null && DateTime.UtcNow - lastRenewal >= TimeSpan.FromSeconds(8))
            {
                // Never renew after observing that the dock has gone, even before debounce finishes.
                if (observation.DockAnchors.Contains(sendingDesk.DockAnchor, StringComparer.OrdinalIgnoreCase))
                {
                    try { await Task.Run(delegate { peer.Send("LEASE|RENEW|" + sendingToken); }); lastRenewal = DateTime.UtcNow; }
                    catch (Exception error) { PeerDiagnostics.Write("LEASE RENEW_ERROR " + error.Message); if (error.Message.Contains("lease expired", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("lease is no longer active", StringComparison.OrdinalIgnoreCase)) { StopSendingLease(); Notify("Desktop ownership changed", "The desktop ended this connection lease. Choose the laptop profile manually to take over again."); } }
                }
            }
            DeskRule desk = DeskDetection.Select(Config.Desks, observation); string selected = desk == null ? "" : desk.Id;
            if (selected != candidateDesk) { candidateDesk = selected; stableSamples = 1; RefreshMenu(); return; }
            stableSamples++;
            if (desk == null || stableSamples < 3 || selected == activeDesk || IsBusy) return;
            if (selected == suppressedDesk) { activeDesk = selected; return; }
            if (!String.IsNullOrEmpty(activeDesk)) await ReleaseDeskReturn();
            activeDesk = selected; suppressedDesk = "";
            DisplayProfile profile = Config.Profiles.FirstOrDefault(x => x.Id == desk.ProfileId);
            if (profile == null || !IsCalibrated(profile)) { suppressedDesk = selected; Notify("Set up this desk", "Apply its profile manually, choose Keep, and test Undo before enabling automatic switching."); return; }
            ApplyProfileAsync(profile, true, desk);
        }
        void SuppressCurrentDesk() { if (!String.IsNullOrEmpty(activeDesk)) suppressedDesk = activeDesk; else if (!String.IsNullOrEmpty(candidateDesk)) suppressedDesk = candidateDesk; }
        async void LeaveOnDesktop()
        {
            if (IsBusy) return;
            SuppressCurrentDesk();
            if (lastChange != null && DateTime.UtcNow <= lastChange.Expires) { UndoLastChange(); return; }
            try { await ReleaseDeskReturn(); List<string> panel = DisplayEngine.GetMonitors().Where(x => x.Internal).Select(x => x.DevicePath).ToList(); if (panel.Count > 0) await Task.Run(delegate { DisplayEngine.ApplyDevicePaths(panel); }); }
            catch (Exception error) { Error(error.Message); }
        }
        void OpenDesks() { if (IsBusy) return; editingSettings = true; try { using (DeskAutomationForm form = new DeskAutomationForm(this)) form.ShowDialog(); } finally { editingSettings = false; } candidateDesk = ""; stableSamples = 0; RefreshMenu(); }
        void Notify(string title, string message) { if (!shuttingDown && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate { tray.ShowBalloonTip(7000, title, message, ToolTipIcon.Warning); }); }
        void CancelReturnLease() { lock (transitionSync) returnLease = null; }
        void StopSendingLease() { sendingDesk = null; sendingToken = null; }

        public string CheckReturnProfile(string name) { return peer.Send("LEASE|CHECK|" + Encode(name)); }
        async Task ArmDeskReturn(DeskRule desk, string transaction)
        {
            if (String.IsNullOrWhiteSpace(desk.ReturnProfileName)) return;
            string digest = await Task.Run(delegate { return CheckReturnProfile(desk.ReturnProfileName); });
            await Task.Run(delegate { peer.Send("LEASE|ARM|" + transaction + "|" + Encode(desk.ReturnProfileName) + "|" + Encode(digest)); });
            sendingDesk = desk; sendingToken = transaction; lastRenewal = DateTime.UtcNow;
        }
        async Task ReleaseDeskReturn()
        {
            string token = sendingToken; StopSendingLease();
            if (!String.IsNullOrEmpty(token))
                try { await Task.Run(delegate { peer.Send("LEASE|RELEASE|" + token); }); }
                catch (Exception error) { PeerDiagnostics.Write("LEASE RELEASE_ERROR " + error.Message); Notify("Desktop return pending", "The desktop will try its return profile when the connection lease expires."); }
        }
        string HandleLease(string command)
        {
            string[] parts = command.Split('|'); if (parts.Length < 3) throw new InvalidOperationException("Malformed desk lease.");
            lock (transitionSync)
            {
                if (parts[1] == "CHECK")
                {
                    DisplayProfile profile = FindProfile(Decode(parts[2]));
                    if (!String.IsNullOrWhiteSpace(profile.PeerProfileName) || !IsCalibrated(profile)) throw new InvalidOperationException("The desktop return profile must have a saved layout, pass Keep and Undo, and work without a peer profile.");
                    return ProfileDigest(profile);
                }
                if (parts[1] == "ARM")
                {
                    if (parts.Length != 5 || inboundTransition == null || inboundTransition.Id != parts[2] || !inboundTransition.Committed || IsBusy) throw new InvalidOperationException("A return lease requires a committed handoff.");
                    DisplayProfile profile = FindProfile(Decode(parts[3])); string digest = Decode(parts[4]);
                    if (!String.IsNullOrWhiteSpace(profile.PeerProfileName) || !IsCalibrated(profile) || ProfileDigest(profile) != digest) throw new InvalidOperationException("The return profile changed. Test it again on the desktop.");
                    returnLease = new ReturnLease { Token = parts[2], ProfileName = profile.Name, ProfileDigest = digest, Expires = DateTime.UtcNow.AddSeconds(30) }; return "OK";
                }
                if (returnLease == null || returnLease.Token != parts[2]) throw new InvalidOperationException("This desk lease is no longer active.");
                if (parts[1] == "RENEW") { if (returnLease.Expires <= DateTime.UtcNow) throw new InvalidOperationException("The desk lease expired."); returnLease.Expires = DateTime.UtcNow.AddSeconds(30); return "OK"; }
                if (parts[1] == "RELEASE") { returnLease.Expires = DateTime.UtcNow; return "OK"; }
                throw new InvalidOperationException("Unsupported desk lease command.");
            }
        }
        async Task CheckReturnLease()
        {
            ReturnLease lease;
            lock (transitionSync) { lease = returnLease; if (lease == null || lease.Expires > DateTime.UtcNow || IsBusy) return; returnLease = null; }
            try
            {
                DisplayProfile profile = FindProfile(lease.ProfileName);
                if (ProfileDigest(profile) != lease.ProfileDigest) throw new InvalidOperationException("The desktop return profile changed. Choose it manually.");
                await Task.Run(delegate { RunUnattendedLocal(profile); });
                tray.ShowBalloonTip(4000, "DisplayCue", "Desktop monitors restored after the laptop disconnected.", ToolTipIcon.Info);
            }
            catch (Exception error) { Notify("Desktop return needs attention", error.Message); }
            RefreshMenu();
        }
    }
}
