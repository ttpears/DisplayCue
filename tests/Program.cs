using MonitorHotkeys;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Drawing;
using System.Windows.Forms;

static class Tests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Fails(Action action, string expected) { try { action(); } catch (Exception error) { Check(error.GetBaseException().Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "Unexpected error: " + error); return; } throw new Exception("Expected failure: " + expected); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static InputRequest Input(string id, uint input) => new() { PhysicalId = id, Input = input };

    sealed class Desk
    {
        public Dictionary<string, uint> Inputs = new() { ["left"] = 15, ["right"] = 17 };
        public List<string> Events = new();
        public readonly List<InputRequest> Target = new() { Input("left", 17), Input("right", 15) };
    }
    sealed class Endpoint : IHandoffParticipant
    {
        public Desk Desk; public string Name; public bool Active, Before, FinalActive, AllowInactive;
        public bool FailSecondSwitch, AcceptWithoutSwitching, PeerLost, FailAfterAccept;
        public Dictionary<string, uint> Original = new();
        public IEnumerable<InputRequest> PlanOverride;
        public Endpoint(Desk desk, string name, bool active, bool final, bool inactive) { Desk = desk; Name = name; Active = active; FinalActive = final; AllowInactive = inactive; }
        public void Begin() { Before = Active; Desk.Events.Add(Name + ":begin"); }
        public IEnumerable<InputRequest> Plan() => PlanOverride ?? Desk.Target;
        public void Prepare() { if (PeerLost) throw new InvalidOperationException("Peer unavailable"); Active = true; Desk.Events.Add(Name + ":prepare"); }
        public uint ReadInput(string id, bool capture)
        {
            if (PeerLost || !Active || (!AllowInactive && Desk.Inputs[id] != Desk.Target.First(x => x.PhysicalId == id).Input)) throw new InvalidOperationException("No route");
            uint value = Desk.Inputs[id]; if (capture && !Original.ContainsKey(id)) Original[id] = value;
            Desk.Events.Add(Name + (capture ? ":capture:" : ":verify:") + id); return value;
        }
        public void SetInput(string id, uint input, bool capture)
        {
            ReadInput(id, capture);
            if (FailSecondSwitch && id == "right" && input == 15) throw new InvalidOperationException("Right monitor rejected switch");
            if (capture && !Original.ContainsKey(id)) Original[id] = Desk.Inputs[id];
            if (!AcceptWithoutSwitching) Desk.Inputs[id] = input;
            Desk.Events.Add(Name + ":set:" + id + ":" + input);
            if (FailAfterAccept && input == Desk.Target.First(x => x.PhysicalId == id).Input) throw new InvalidOperationException("Lost acknowledgement");
        }
        public void Apply()
        {
            Check(Desk.Target.All(x => Desk.Inputs[x.PhysicalId] == x.Input), "A Windows path was released before physical inputs switched");
            if (PeerLost) throw new InvalidOperationException("Peer unavailable"); Active = FinalActive; Desk.Events.Add(Name + ":apply");
        }
        public void Commit() { Desk.Events.Add(Name + ":commit"); }
        public void PrepareRestore() { if (PeerLost) throw new InvalidOperationException("Peer unavailable"); Active = true; Desk.Events.Add(Name + ":restore-prepare"); }
        public IEnumerable<InputRequest> PreviousInputs() => Original.Select(x => Input(x.Key, x.Value));
        public void RestoreLayout() { if (PeerLost) throw new InvalidOperationException("Peer unavailable"); Active = Before; Desk.Events.Add(Name + ":restore-layout"); }
        public void CompleteRestore() { Desk.Events.Add(Name + ":restored"); }
    }
    static (Desk Desk, Endpoint Laptop, Endpoint Desktop) Setup()
    {
        Desk desk = new(); return (desk, new Endpoint(desk, "laptop", false, true, false), new Endpoint(desk, "desktop", true, false, true));
    }
    static AppController App(AppConfig config)
    {
        AppController app = (AppController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        typeof(AppController).GetField("<Config>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, config);
        typeof(AppController).GetField("transitionSync", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, new object());
        return app;
    }
    static object Invoke(AppController app, string method, params object[] args)
    {
        try { return typeof(AppController).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void SetInbound(AppController app, DisplayProfile profile, bool committed)
    {
        Type type = typeof(AppController).GetNestedType("ProfileTransition", BindingFlags.NonPublic);
        object state = Activator.CreateInstance(type);
        type.GetField("Id").SetValue(state, "current"); type.GetField("Profile").SetValue(state, profile); type.GetField("Committed").SetValue(state, committed);
        typeof(AppController).GetField("inboundTransition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, state);
    }
    static DisplayProfile Profile() => new() { Name = "Desktop", Devices = new() { "a" }, Layout = new() { new SavedDisplayLayout { DevicePath = "a", Width = 2560, Height = 1440, X = 0, Y = 0, Rotation = 1, RefreshNumerator = 60, RefreshDenominator = 1, TargetSignal = Convert.ToBase64String(new byte[48]) } } };
    static string B64(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Test("mixed-input takeover preserves DDC until both switches and verification", () => {
                var (desk, laptop, desktop) = Setup(); HandoffResult result = HandoffSequence.Run(laptop, desktop, () => true);
                Check(result.Committed && laptop.Active && !desktop.Active, "Wrong committed state");
                int apply = desk.Events.FindIndex(x => x.EndsWith(":apply")); int lastSet = desk.Events.FindLastIndex(x => x.Contains(":set:"));
                Check(apply > lastSet && desk.Events.Take(apply).Any(x => x.Contains(":verify:")), "Missing pre-release verification");
            });
            Test("cancel restores both mixed inputs and both Windows layouts", () => {
                var (desk, laptop, desktop) = Setup(); HandoffResult result = HandoffSequence.Run(laptop, desktop, () => false);
                Check(!result.Committed && result.RecoveryErrors.Count == 0 && desk.Inputs["left"] == 15 && desk.Inputs["right"] == 17 && !laptop.Active && desktop.Active, "Incomplete cancellation recovery");
            });
            Test("second monitor failure restores the first switched monitor", () => {
                var (desk, laptop, desktop) = Setup(); desktop.FailSecondSwitch = true;
                Fails(() => HandoffSequence.Run(laptop, desktop, () => true), "rejected switch");
                Check(desk.Inputs["left"] == 15 && desk.Inputs["right"] == 17 && desktop.Active && !laptop.Active, "Partial handoff left a monitor stranded");
            });
            Test("acknowledged but ineffective DDC writes cannot commit", () => {
                var (desk, laptop, desktop) = Setup(); desktop.AcceptWithoutSwitching = true;
                Fails(() => HandoffSequence.Run(laptop, desktop, () => true), "did not confirm");
                Check(!desk.Events.Any(x => x.EndsWith(":commit")), "Unverified input committed");
            });
            Test("conflicting peer plans fail before any display preparation", () => {
                var (desk, laptop, desktop) = Setup(); desktop.PlanOverride = new[] { Input("left", 15) };
                Fails(() => HandoffSequence.Run(laptop, desktop, () => true), "different inputs");
                Check(!desk.Events.Any(x => x.EndsWith(":prepare") || x.Contains(":set:")), "Conflicting plans mutated hardware");
            });
            Test("unreadable originals prevent input writes", () => {
                var (desk, laptop, desktop) = Setup(); desktop.AllowInactive = false; desk.Inputs["left"] = 99;
                Fails(() => HandoffSequence.Run(laptop, desktop, () => true), "previous input");
                Check(!desk.Events.Any(x => x.Contains(":set:")), "Switched without an original input snapshot");
            });
            Test("lost peer recovery still attempts the local layout", () => {
                var (desk, laptop, desktop) = Setup();
                Fails(() => HandoffSequence.Run(laptop, desktop, () => { desktop.PeerLost = true; throw new InvalidOperationException("Laptop disconnected"); }), "Laptop disconnected");
                Check(desk.Events.Contains("laptop:restore-layout"), "Peer error prevented local recovery");
            });
            Test("fallback after a lost acknowledgement retains the original input", () => {
                var (desk, laptop, desktop) = Setup(); laptop.AllowInactive = true; laptop.FailAfterAccept = true;
                HandoffResult result = HandoffSequence.Run(laptop, desktop, () => false);
                Check(result.RecoveryErrors.Count == 0 && desk.Inputs["left"] == 15 && desk.Inputs["right"] == 17, "Fallback overwrote an original input");
            });
            Test("desk matching handles late video, ambiguity and monitor changes", () => {
                DeskRule home = new() { Enabled = true, DockAnchor = "home-dock", DisplayPaths = new() { "left", "right" } };
                DeskRule office = new() { Enabled = true, DockAnchor = "office-dock", DisplayPaths = new() { "office" } };
                Check(DeskDetection.Select(new[] { home, office }, new DeskObservation { DockAnchors = new() { "home-dock" } }) == home, "Late video did not identify home");
                Check(DeskDetection.Select(new[] { home, office }, new DeskObservation { DockAnchors = new() { "home-dock" }, ExternalDisplays = new() { "unknown" } }) == null, "Wrong monitors matched home");
                Check(DeskDetection.Select(new[] { home, home }, new DeskObservation { DockAnchors = new() { "home-dock" }, ExternalDisplays = new() { "left", "right" } }) == null, "Ambiguous desk matched");
                Check(DeskDetection.Select(new[] { home, office }, new DeskObservation { DockAnchors = new() { "office-dock" }, ExternalDisplays = new() { "office" } }) == office, "Office did not match");
            });
            Test("native display buffers have the Windows ABI sizes", () => {
                foreach (var expected in new[] { ("MODE", 64), ("PATH", 72), ("SIGNAL", 48), ("SOURCEMODE", 20) })
                    Check(Marshal.SizeOf(typeof(DisplayEngine).GetNestedType(expected.Item1, BindingFlags.NonPublic)) == expected.Item2, "Wrong native size for " + expected.Item1);
            });
            Test("layout comparison detects position, primary, refresh and resolution drift", () => {
                DisplayProfile p = Profile(); SavedDisplayLayout actual = new() { DevicePath = "a", Width = 2560, Height = 1440, Rotation = 1, RefreshNumerator = 60, RefreshDenominator = 1 };
                Check(DisplayEngine.LayoutMatches(p.Layout, new[] { actual }), "Equal layouts mismatch");
                actual.X = 1; Check(!DisplayEngine.LayoutMatches(p.Layout, new[] { actual }), "Position drift missed"); actual.X = 0;
                actual.RefreshNumerator = 120; Check(!DisplayEngine.LayoutMatches(p.Layout, new[] { actual }), "Refresh drift missed"); actual.RefreshNumerator = 60;
                actual.Width = 1920; Check(!DisplayEngine.LayoutMatches(p.Layout, new[] { actual }), "Resolution drift missed");
                Fails(() => DisplayEngine.ValidateLayout(new[] { "b" }, p.Layout), "does not match");
            });
            Test("legacy profile JSON can be normalized without enabling automation", () => {
                using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes("{\"Profiles\":[{\"Name\":\"Old\",\"Devices\":[\"a\"]}]}"));
                AppConfig config = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(stream);
                Check(config.Profiles.Count == 1 && config.Desks == null && config.Profiles[0].ValidatedSignature == null, "Legacy configuration unexpectedly trusted");
            });
            Test("editing a TV-only profile retains unavailable G5 input mappings", () => {
                MonitorInputAssignment left = new() { DevicePath = "left", SharedId = "left", Input = 17 };
                MonitorInputAssignment right = new() { DevicePath = "right", SharedId = "right", Input = 15 };
                var retained = MonitorInputsForm.MergeAssignments(new[] { left, right }, new[] { "tv" }, Array.Empty<MonitorInputAssignment>());
                Check(retained.Count == 2 && retained.Contains(left) && retained.Contains(right), "Unavailable mappings silently removed");
                var removed = MonitorInputsForm.MergeAssignments(new[] { left, right }, new[] { "right" }, Array.Empty<MonitorInputAssignment>());
                Check(removed.Count == 1 && removed[0] == left, "Explicit Do not change was not respected");
            });
            Test("calibration invalidates when saved inputs or layout change", () => {
                DisplayProfile p = Profile(); AppController app = App(new AppConfig { Profiles = new() { p } });
                p.ValidatedProfileDigest = p.RecoveryProfileDigest = app.ProfileDigest(p); Check(app.IsCalibrated(p), "Test profile untrusted");
                p.Layout[0].RefreshNumerator = 120; Check(!app.IsCalibrated(p), "Changed mode retained calibration");
                p.Layout[0].RefreshNumerator = 60; p.MonitorInputs.Add(new MonitorInputAssignment { SharedId = "left", Input = 17 }); Check(!app.IsCalibrated(p), "Changed input retained calibration");
            });
            Test("desktop return lease requires commit, rejects stale tokens and expires", () => {
                DisplayProfile p = Profile(); AppController app = App(new AppConfig { Profiles = new() { p } });
                p.ValidatedProfileDigest = p.RecoveryProfileDigest = app.ProfileDigest(p);
                SetInbound(app, p, false);
                string arm = "LEASE|ARM|current|" + B64(p.Name) + "|" + B64(app.ProfileDigest(p));
                Fails(() => Invoke(app, "HandleLease", arm), "committed"); SetInbound(app, p, true);
                Check((string)Invoke(app, "HandleLease", arm) == "OK", "Lease not armed");
                Fails(() => Invoke(app, "HandleLease", "LEASE|RENEW|stale"), "no longer active");
                Check((string)Invoke(app, "HandleLease", "LEASE|RENEW|current") == "OK", "Lease not renewed");
                Invoke(app, "HandleLease", "LEASE|RELEASE|current");
                Fails(() => Invoke(app, "HandleLease", "LEASE|RENEW|current"), "expired");
                p.Layout[0].Width = 1920; Fails(() => Invoke(app, "HandleLease", arm), "changed");
            });
            Test("protocol negotiation and malformed phases fail without hardware access", () => {
                AppController app = App(new AppConfig());
                Check((string)Invoke(app, "HandlePeerCommand", "PROTOCOL") == "2", "Wrong protocol");
                Fails(() => Invoke(app, "HandlePeerCommand", "TX|BEGIN|id|old-client"), "protocol 2");
                Fails(() => Invoke(app, "HandlePeerCommand", "TX|COMMIT|stale"), "no longer current");
            });
            Test("Undo reserves the peer and cannot be recommitted or superseded", () => {
                DisplayProfile p = Profile(); AppController app = App(new AppConfig { Profiles = new() { p } }); SetInbound(app, p, true);
                Check((string)Invoke(app, "HandleTransaction", "TX|CAN_UNDO|current") == "OK" && app.IsBusy, "Undo did not reserve its peer");
                Fails(() => Invoke(app, "HandleTransaction", "TX|CAN_UNDO|current"), "cannot undo");
                Fails(() => Invoke(app, "HandleTransaction", "TX|COMMIT|current"), "being restored");
                Fails(() => Invoke(app, "HandleTransaction", "TX|BEGIN|new|" + B64(p.Name) + "|" + B64("digest")), "already running");
            });
            Test("session override survives resume and clears on physical undock", () => {
                DeskRule home = new() { Enabled = true, DockAnchor = "dock", DisplayPaths = new() { "left", "right" }, ReturnProfileName = "Desktop" };
                AppController app = App(new AppConfig { Desks = new() { home } });
                using ContextMenuStrip menu = new(); using NotifyIcon tray = new();
                typeof(AppController).GetField("menu", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, menu);
                typeof(AppController).GetField("tray", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, tray);
                void Observe(DeskObservation observation) => ((Task)Invoke(app, "ObserveDesk", observation)).GetAwaiter().GetResult();
                string Field(string name) => (string)typeof(AppController).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
                DeskObservation connected = new() { DockAnchors = new() { "dock" }, ExternalDisplays = new() { "left", "right" } };
                Observe(connected); Invoke(app, "SuppressCurrentDesk"); Observe(connected); Observe(connected);
                Check(Field("activeDesk") == home.Id && Field("suppressedDesk") == home.Id, "Override before takeover was lost");
                Observe(new DeskObservation { DockAnchors = new() { "dock" } }); Observe(connected); Observe(connected); Observe(connected);
                Check(Field("suppressedDesk") == home.Id, "Resume/video churn cleared the override");
                DeskObservation unpluggedWithStaleEdid = new() { ExternalDisplays = new() { "left", "right" } };
                Observe(unpluggedWithStaleEdid); Observe(unpluggedWithStaleEdid); Observe(unpluggedWithStaleEdid);
                Check(Field("activeDesk") == "" && Field("suppressedDesk") == "", "Physical undock did not reset the session");
            });
            Test("authenticated loopback executes correctly ordered transaction phases", () => {
                byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                List<string> commands = new();
                using PeerService server = new(new AppConfig { PeerEnabled = true, ListenPort = 0 }, command => {
                    lock (commands) commands.Add(command);
                    if (command == "PROTOCOL") return "2";
                    if (command == "FAIL") throw new InvalidOperationException("intentional failure");
                    string[] parts = command.Split('|'); Check(parts.Length >= 3 && parts[0] == "TX" && parts[2] == "transaction", "Transaction ID moved to the wrong wire field");
                    if (parts[1] == "BEGIN") { Check(parts.Length == 5 && System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])) == "TV only", "Profile name moved to wrong wire field"); return "remote-digest|"; }
                    if (parts[1] == "PLAN" || parts[1] == "PREVIOUS") return "left:17,right:15";
                    if (parts[1] == "CAPTURE" || parts[1] == "READINPUT") { Check(parts.Length == 4 && parts[3] == "left", "Monitor read malformed"); return "17"; }
                    if (parts[1] == "SETINPUT" || parts[1] == "FORCEINPUT") Check(parts.Length == 5 && parts[3] == "left" && parts[4] == "17", "Monitor set malformed");
                    return "OK";
                }, () => key, System.Net.IPAddress.Loopback);
                server.Start();
                using PeerService client = new(new AppConfig { PeerHost = "127.0.0.1", PeerPort = server.BoundPort }, _ => "", () => key);
                AppController app = App(new AppConfig()); typeof(AppController).GetField("peer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, client);
                Type type = typeof(AppController).GetNestedType("RemoteParticipant", BindingFlags.NonPublic);
                IHandoffParticipant remote = (IHandoffParticipant)Activator.CreateInstance(type, app, "transaction", "TV only", "local-digest");
                remote.Begin(); Check(remote.Plan().Count() == 2, "Peer plan decoding failed"); remote.Prepare(); Check(remote.ReadInput("left", true) == 17, "Capture failed");
                remote.SetInput("left", 17, false); remote.SetInput("left", 17, true); remote.ReadInput("left", false); remote.Apply(); remote.Commit();
                remote.PrepareRestore(); Check(remote.PreviousInputs().Count() == 2, "Previous inputs missing"); remote.RestoreLayout(); remote.CompleteRestore();
                Fails(() => client.Send("FAIL"), "intentional failure");
                using PeerService wrong = new(new AppConfig { PeerHost = "127.0.0.1", PeerPort = server.BoundPort }, _ => "", () => new byte[32]);
                Fails(() => wrong.Send("PROTOCOL"), "Authentication failed");
                Check(commands.Contains("TX|FORCEINPUT|transaction|left|17") && commands.Contains("TX|SETINPUT|transaction|left|17"), "Input phases not transported");
            });

            if (args.Contains("--read-only-hardware"))
            {
                Test("read-only connected display modes and dock enumeration", () => {
                    List<MonitorInfo> monitors = DisplayEngine.GetMonitors(); List<SavedDisplayLayout> layout = DisplayEngine.CaptureLayout();
                    Check(layout.Count > 0 && layout.All(x => x.Width > 0 && x.Height > 0 && x.RefreshDenominator > 0), "Native mode capture failed");
                    Console.WriteLine("Read-only: " + monitors.Count + " connected, " + layout.Count + " active; " + DockProbe.Connected().Count + " Lenovo USB connections.");
                });
            }
            int render = Array.IndexOf(args, "--render-ui");
            if (render >= 0 && render + 1 < args.Length)
            {
                Application.EnableVisualStyles(); string folder = args[render + 1]; Directory.CreateDirectory(folder);
                AppController app = App(new AppConfig { Profiles = new() { Profile() } });
                using SettingsForm settings = new(app);
                typeof(SettingsForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(settings, new object[] { 1 });
                Render(settings, Path.Combine(folder, "profiles.png"));
                using DeskAutomationForm automation = new(app); Render(automation, Path.Combine(folder, "automatic-desks.png"));
                Console.WriteLine("Rendered setup forms to " + folder);
            }
            Console.WriteLine(passed + " regression tests passed. No display settings or pairing keys changed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Render(Form form, string path)
    {
        form.AutoScaleMode = AutoScaleMode.None; form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); form.Refresh(); Application.DoEvents();
        using Bitmap bitmap = new(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path);
        form.Hide();
    }
}
