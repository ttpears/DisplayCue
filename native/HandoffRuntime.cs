using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorHotkeys
{
    public sealed partial class AppController
    {
        readonly object transitionSync = new object();
        sealed class LastChange { public NativeParticipant Local; public RemoteParticipant Remote; public DateTime Expires; }
        LastChange lastChange;
        volatile bool editingSettings;
        public bool IsBusy { get { lock (transitionSync) return !String.IsNullOrEmpty(activeTransitionId); } }
        static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        static string Decode(string value) { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }

        public string ProfileDigest(DisplayProfile profile)
        {
            DisplayProfile copy = new DisplayProfile { Id = profile.Id, Name = profile.Name, Devices = profile.Devices, MonitorInputs = profile.MonitorInputs, PeerProfileName = profile.PeerProfileName, Layout = profile.Layout };
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(DisplayProfile)).WriteObject(stream, copy);
                string peerIdentity = Config.PeerEnabled ? Config.PeerHost + ":" + Config.PeerPort + ":" + PeerKeyStore.Get() : "";
                return Digest(Convert.ToBase64String(stream.ToArray()) + "|" + peerIdentity);
            }
        }
        static string Digest(string value) { using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string PairDigest(string first, string second) { return Digest(String.Join("|", new[] { first, second ?? "" }.OrderBy(x => x, StringComparer.Ordinal))); }
        public bool IsCalibrated(DisplayProfile profile) { string digest = ProfileDigest(profile); return profile.Layout != null && profile.Layout.Count > 0 && profile.ValidatedProfileDigest == digest && profile.RecoveryProfileDigest == digest; }

        sealed class NativeParticipant : IHandoffParticipant
        {
            readonly AppController app; readonly string id; readonly DisplayProfile profile; public ProfileTransition State;
            public NativeParticipant(AppController app, string id, DisplayProfile profile) { this.app = app; this.id = id; this.profile = profile; }
            public void Begin()
            {
                lock (app.transitionSync)
                {
                    if (!String.IsNullOrEmpty(app.activeTransitionId) && app.activeTransitionId != id) throw new InvalidOperationException("Another display change is already running.");
                    DisplayEngine.ValidateLayout(profile.Devices, profile.Layout);
                    if (profile.Devices.Count == 0) throw new InvalidOperationException("Choose at least one display for this profile.");
                    List<SavedDisplayLayout> layout = DisplayEngine.CaptureLayout();
                    DisplayEngine.ValidateLayout(layout.Select(x => x.DevicePath), layout);
                    State = new ProfileTransition { Id = id, Profile = profile, PreviousLayout = layout, PreviousDisplays = layout.Select(x => x.DevicePath).ToList() };
                    if (app.inboundTransition != null && app.inboundTransition.Id != id) app.inboundTransition = null;
                    app.activeTransitionId = id;
                }
            }
            public IEnumerable<InputRequest> Plan() { return (profile.MonitorInputs ?? new List<MonitorInputAssignment>()).Select(x => new InputRequest { PhysicalId = Identity(x), Input = x.Input }); }
            public void Prepare() { DisplayEngine.PrepareForInputs(profile.Devices.Concat((profile.MonitorInputs ?? new List<MonitorInputAssignment>()).Select(x => x.DevicePath))); }
            public uint ReadInput(string identity, bool capture) { return app.WithMonitor(identity, delegate(DdcMonitorInfo monitor) { uint input = DdcEngine.ReadInput(monitor); if (capture) app.CaptureInput(State, identity, monitor, input); return input; }); }
            public void SetInput(string identity, uint input, bool capture)
            {
                app.WithMonitor(identity, delegate(DdcMonitorInfo monitor) {
                    uint before = DdcEngine.ReadInput(monitor);
                    if (capture) app.CaptureInput(State, identity, monitor, before);
                    if (before != input) { DdcEngine.SetInput(monitor, input); System.Threading.Thread.Sleep(750); }
                    return input;
                });
            }
            public void Apply() { DisplayEngine.ApplyProfile(profile); State.FinalApplied = true; }
            public void Commit()
            {
                if (State == null || !State.FinalApplied || State.Restored) throw new InvalidOperationException("Only a verified final layout can be committed.");
                State.Committed = true; profile.ValidatedProfileDigest = app.ProfileDigest(profile);
                profile.ValidatedSignature = PairDigest(profile.ValidatedProfileDigest, State.OtherDigest);
                ConfigStore.Save(app.Config); DesktopRecovery.BringWindowsIntoView();
                PeerDiagnostics.Write("TX " + id + " COMMIT " + profile.Name);
            }
            public void PrepareRestore() { if (State != null) DisplayEngine.PrepareForInputs(State.PreviousDisplays.Concat(State.PreviousInputs.Select(x => x.DevicePath))); }
            public IEnumerable<InputRequest> PreviousInputs() { return State == null ? new InputRequest[0] : State.PreviousInputs.Select(x => new InputRequest { PhysicalId = Identity(x), Input = x.Input }); }
            public void RestoreLayout() { if (State != null) { DisplayEngine.RestoreLayout(State.PreviousLayout); State.Restored = DisplayEngine.LayoutMatches(State.PreviousLayout, DisplayEngine.CaptureLayout()); } }
            public void CompleteRestore() { PeerDiagnostics.Write("TX " + id + " RESTORED"); }
            public void MarkRecovery() { if (State == null || !State.Restored) throw new InvalidOperationException("Reconnect all original displays before validating recovery."); profile.RecoveryProfileDigest = app.ProfileDigest(profile); ConfigStore.Save(app.Config); }
        }

        sealed class RemoteParticipant : IHandoffParticipant
        {
            readonly AppController app; public readonly string Id; readonly string name, localDigest;
            public string Digest, ValidatedSignature;
            bool begun;
            public RemoteParticipant(AppController app, string id, string name, string localDigest) { this.app = app; Id = id; this.name = name; this.localDigest = localDigest; }
            string Send(string action) { int split = action.IndexOf('|'); return app.peer.Send("TX|" + (split < 0 ? action : action.Substring(0, split)) + "|" + Id + (split < 0 ? "" : action.Substring(split))); }
            public void Begin()
            {
                if (app.peer.Send("PROTOCOL") != "2") throw new InvalidOperationException("Update DisplayCue on both computers before switching.");
                string[] response = Send("BEGIN|" + Encode(name) + "|" + Encode(localDigest)).Split('|');
                if (response.Length != 2) throw new InvalidOperationException("The peer returned invalid profile validation data.");
                Digest = response[0]; ValidatedSignature = response[1]; begun = true;
            }
            public IEnumerable<InputRequest> Plan() { return DecodeInputs(Send("PLAN")); }
            public void Prepare() { Send("PREPARE"); }
            public uint ReadInput(string identity, bool capture) { return UInt32.Parse(Send((capture ? "CAPTURE" : "READINPUT") + "|" + identity)); }
            public void SetInput(string identity, uint input, bool capture) { Send((capture ? "SETINPUT" : "FORCEINPUT") + "|" + identity + "|" + input); }
            public void Apply() { Send("APPLY"); }
            public void Commit() { Send("COMMIT"); }
            public void PrepareRestore() { if (begun) Send("ROLLBACK_PREPARE"); }
            public IEnumerable<InputRequest> PreviousInputs() { return begun ? DecodeInputs(Send("PREVIOUS")) : new List<InputRequest>(); }
            public void RestoreLayout() { if (begun) Send("ROLLBACK_APPLY"); }
            public void CompleteRestore() { if (begun) Send("ROLLBACK_DONE"); }
            public void MarkRecovery() { Send("RECOVERY_VALIDATED"); }
            public void CheckUndo() { Send("CAN_UNDO"); }
        }

        static string Identity(MonitorInputAssignment input) { return String.IsNullOrWhiteSpace(input.SharedId) ? input.PhysicalId ?? "" : input.SharedId.Trim(); }
        static string EncodeInputs(IEnumerable<InputRequest> inputs) { return String.Join(",", inputs.Select(x => x.PhysicalId + ":" + x.Input)); }
        static List<InputRequest> DecodeInputs(string value)
        {
            List<InputRequest> result = new List<InputRequest>();
            if (String.IsNullOrEmpty(value)) return result;
            foreach (string item in value.Split(',')) { int colon = item.LastIndexOf(':'); if (colon < 1 || !UInt32.TryParse(item.Substring(colon + 1), out uint input)) throw new InvalidOperationException("Invalid peer input plan."); result.Add(new InputRequest { PhysicalId = item.Substring(0, colon), Input = input }); }
            return result;
        }

        T WithMonitor<T>(string identity, Func<DdcMonitorInfo, T> operation)
        {
            if (String.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("Assign a shared monitor identity first.");
            List<DdcMonitorInfo> monitors = DdcEngine.Discover();
            try
            {
                List<string> paths = Config.Profiles.SelectMany(x => x.MonitorInputs ?? new List<MonitorInputAssignment>()).Where(x => String.Equals(Identity(x), identity, StringComparison.OrdinalIgnoreCase)).Select(x => x.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                List<DdcMonitorInfo> matches = monitors.Where(x => x.SupportsInputSwitching && (String.Equals(x.PhysicalId, identity, StringComparison.OrdinalIgnoreCase) || paths.Contains(x.DevicePath, StringComparer.OrdinalIgnoreCase))).ToList();
                if (matches.Count != 1) throw new InvalidOperationException(matches.Count > 1 ? "The monitor identity is ambiguous. Assign distinct left/right Peer IDs." : "This computer cannot currently control that monitor through DDC/CI.");
                return operation(matches[0]);
            }
            finally { DdcEngine.Release(monitors); }
        }
        void CaptureInput(ProfileTransition state, string identity, DdcMonitorInfo monitor, uint input)
        {
            if (state == null) throw new InvalidOperationException("The recovery snapshot is unavailable.");
            if (!state.PreviousInputs.Any(x => String.Equals(Identity(x), identity, StringComparison.OrdinalIgnoreCase)))
                state.PreviousInputs.Add(new MonitorInputAssignment { DevicePath = monitor.DevicePath, PhysicalId = monitor.PhysicalId, SharedId = identity, Input = input });
        }

        string HandlePeerCommand(string command)
        {
            if (command == "PROTOCOL") return "2";
            if (command == "PING") return Config.DeviceName;
            if (command == "INFO" || command == "STATUS") return Config.DeviceName + "\nProfiles: " + String.Join(", ", Config.Profiles.Select(x => x.Name)) + "\n" + String.Join(", ", DisplayEngine.GetMonitors().Where(x => x.Active).Select(x => x.Name));
            if (command.StartsWith("LEASE|")) return HandleLease(command);
            if (command.StartsWith("TX|"))
            {
                lock (transitionSync) return HandleTransaction(command);
            }
            if (command.StartsWith("PROFILE:"))
            {
                DisplayProfile profile = FindProfile(command.Substring(8));
                if (!IsCalibrated(profile)) throw new InvalidOperationException("Apply this profile and test Undo locally before using unattended peer control.");
                RunUnattendedLocal(profile); return profile.Name;
            }
            throw new InvalidOperationException("Unsupported command.");
        }
        DisplayProfile FindProfile(string name) { return Config.Profiles.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Profile not found: " + name); }
        string HandleTransaction(string command)
        {
            string[] parts = command.Split('|'); if (parts.Length < 3) throw new InvalidOperationException("Malformed transaction.");
            string action = parts[1], id = parts[2];
            if (action == "BEGIN")
            {
                if (parts.Length != 5) throw new InvalidOperationException("Both computers must run DisplayCue protocol 2.");
                if (inboundTransition != null && inboundTransition.Id == id) return ProfileDigest(inboundTransition.Profile) + "|" + inboundTransition.Profile.ValidatedSignature;
                if (IsBusy) throw new InvalidOperationException("Another display change is already running.");
                if (editingSettings) throw new InvalidOperationException("Close DisplayCue Settings on this computer before switching.");
                DisplayProfile profile = FindProfile(Decode(parts[3]));
                NativeParticipant native = new NativeParticipant(this, id, profile); native.Begin(); inboundTransition = native.State; inboundTransition.OtherDigest = Decode(parts[4]); lastChange = null; CancelReturnLease();
                return ProfileDigest(profile) + "|" + profile.ValidatedSignature;
            }
            if (inboundTransition == null || inboundTransition.Id != id) throw new InvalidOperationException("This transaction is no longer current.");
            inboundTransition.StartedUtc = DateTime.UtcNow;
            NativeParticipant participant = new NativeParticipant(this, id, inboundTransition.Profile) { State = inboundTransition };
            if (action == "CAN_UNDO") { if (IsBusy || editingSettings || !inboundTransition.Committed || inboundTransition.Restored) throw new InvalidOperationException("The peer cannot undo this change now. Choose a profile manually."); activeTransitionId = id; inboundTransition.Committed = false; inboundTransition.Undoing = true; CancelReturnLease(); return "OK"; }
            if (inboundTransition.Committed && (action == "PREPARE" || action == "CAPTURE" || action == "SETINPUT" || action == "APPLY")) throw new InvalidOperationException("This handoff is already complete. Start a new transaction.");
            if (action == "PLAN") return EncodeInputs(participant.Plan());
            if (action == "PREPARE") { participant.Prepare(); return "OK"; }
            if (action == "CAPTURE" || action == "READINPUT") { if (parts.Length != 4) throw new InvalidOperationException("Malformed monitor read."); return participant.ReadInput(parts[3], action == "CAPTURE").ToString(); }
            if (action == "SETINPUT" || action == "FORCEINPUT") { if (parts.Length != 5 || !UInt32.TryParse(parts[4], out uint input)) throw new InvalidOperationException("Malformed monitor input."); participant.SetInput(parts[3], input, action == "SETINPUT"); return "OK"; }
            if (action == "APPLY") { participant.Apply(); return "OK"; }
            if (action == "COMMIT") { if (inboundTransition.Restored || inboundTransition.Undoing) throw new InvalidOperationException("This transaction is being restored."); if (!inboundTransition.Committed) participant.Commit(); activeTransitionId = null; return "OK"; }
            if (action == "ROLLBACK_PREPARE") { if (IsBusy && activeTransitionId != id) throw new InvalidOperationException("Another change replaced this transaction."); activeTransitionId = id; CancelReturnLease(); participant.PrepareRestore(); return "OK"; }
            if (action == "PREVIOUS") return EncodeInputs(participant.PreviousInputs());
            if (action == "ROLLBACK_APPLY") { participant.RestoreLayout(); return "OK"; }
            if (action == "ROLLBACK_DONE") { participant.CompleteRestore(); inboundTransition.Committed = true; activeTransitionId = null; return "OK"; }
            if (action == "RECOVERY_VALIDATED") { participant.MarkRecovery(); return "OK"; }
            throw new InvalidOperationException("Unsupported transaction phase. Update both computers.");
        }

        async void Apply(DisplayProfile profile) { if (IsBusy) return; SuppressCurrentDesk(); await ReleaseDeskReturn(); ApplyProfileAsync(profile, false, null); }
        async void ApplyProfileAsync(DisplayProfile profile, bool automatic, DeskRule desk)
        {
            string id = Guid.NewGuid().ToString("N");
            lock (transitionSync) { if (IsBusy) return; activeTransitionId = id; lastChange = null; CancelReturnLease(); }
            RefreshMenu();
            NativeParticipant local = new NativeParticipant(this, id, profile);
            RemoteParticipant remote = null;
            try
            {
                remote = String.IsNullOrWhiteSpace(profile.PeerProfileName) ? null : new RemoteParticipant(this, id, profile.PeerProfileName, ProfileDigest(profile));
                if (desk != null && !String.IsNullOrWhiteSpace(desk.ReturnProfileName)) await Task.Run(delegate { CheckReturnProfile(desk.ReturnProfileName); });
                HandoffResult result = await Task.Run(delegate {
                    return HandoffSequence.Run(local, remote, delegate {
                        local.State.OtherDigest = remote == null ? null : remote.Digest;
                        string signature = PairDigest(ProfileDigest(profile), local.State.OtherDigest);
                        bool trusted = profile.ValidatedSignature == signature && (remote == null || remote.ValidatedSignature == signature);
                        if (trusted) return true;
                        if (automatic) throw new InvalidOperationException("This desk configuration changed. Apply it manually and test recovery again.");
                        return (bool)dispatcher.Invoke((Func<bool>)delegate { using (ConfirmLayoutForm form = new ConfirmLayoutForm(profile.Name)) return form.ShowDialog() == DialogResult.Yes; });
                    });
                });
                if (result.RecoveryErrors.Count > 0) throw new InvalidOperationException("Recovery needs attention: " + String.Join("; ", result.RecoveryErrors));
                if (result.Committed)
                {
                    lastChange = new LastChange { Local = local, Remote = remote, Expires = DateTime.UtcNow.AddMinutes(5) };
                    if (desk != null) try { await ArmDeskReturn(desk, id); } catch (Exception error) { throw new InvalidOperationException("The desk switched successfully, but automatic return could not be armed: " + error.Message); }
                    tray.ShowBalloonTip(3000, "DisplayCue", profile.Name + " is active. Undo is available from the tray.", ToolTipIcon.Info);
                }
            }
            catch (Exception error)
            {
                PeerDiagnostics.Write("TX " + id + " ERROR " + error.Message);
                if (automatic) tray.ShowBalloonTip(7000, "DisplayCue needs attention", error.Message, ToolTipIcon.Warning); else Error(error.Message);
            }
            finally { lock (transitionSync) activeTransitionId = null; RefreshMenu(); }
        }

        async void UndoLastChange()
        {
            LastChange change = lastChange;
            if (change == null || IsBusy) return;
            if (DateTime.UtcNow > change.Expires) { lastChange = null; RefreshMenu(); Error("Undo expired. Choose the return profile instead."); return; }
            lock (transitionSync) { if (IsBusy) return; activeTransitionId = change.Local.State.Id; }
            RefreshMenu();
            if (change.Remote != null) try { await Task.Run(change.Remote.CheckUndo); } catch (Exception error) { lastChange = null; lock (transitionSync) activeTransitionId = null; RefreshMenu(); Error("Undo is no longer available: " + error.Message); return; }
            SuppressCurrentDesk(); StopSendingLease();
            List<string> errors = new List<string>();
            try
            {
                await Task.Run(delegate {
                    HandoffSequence.Restore(change.Local, change.Remote, errors);
                    if (errors.Count == 0) { if (change.Remote != null) change.Remote.MarkRecovery(); change.Local.MarkRecovery(); }
                });
                if (errors.Count > 0) Error("Recovery needs attention: " + String.Join("; ", errors));
                else tray.ShowBalloonTip(4000, "DisplayCue", "Previous inputs and layout restored. Recovery test passed for this profile.", ToolTipIcon.Info);
            }
            catch (Exception error) { Error(error.Message); }
            finally { lastChange = null; lock (transitionSync) activeTransitionId = null; RefreshMenu(); }
        }

        void RunUnattendedLocal(DisplayProfile profile)
        {
            if (!String.IsNullOrWhiteSpace(profile.PeerProfileName)) throw new InvalidOperationException("The desktop return profile must work locally without the laptop.");
            string id = Guid.NewGuid().ToString("N");
            lock (transitionSync) { if (IsBusy) throw new InvalidOperationException("Another change is running."); activeTransitionId = id; lastChange = null; }
            try { NativeParticipant local = new NativeParticipant(this, id, profile); HandoffSequence.Run(local, null, delegate { if (!IsCalibrated(profile)) throw new InvalidOperationException("Test this profile and its recovery before automatic return."); return true; }); }
            finally { lock (transitionSync) activeTransitionId = null; }
        }
    }
}
