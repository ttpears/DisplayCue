using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;

namespace MonitorHotkeys
{
    [DataContract]
    public sealed class SavedDisplayLayout
    {
        [DataMember] public string DevicePath = "";
        [DataMember] public uint Width, Height, PixelFormat, Rotation, Scaling, RefreshNumerator, RefreshDenominator;
        [DataMember] public int X, Y;
        [DataMember] public string TargetSignal = "";
        public bool Primary { get { return X == 0 && Y == 0; } }
    }

    public static partial class DisplayEngine
    {
        public static List<SavedDisplayLayout> CaptureLayout()
        {
            PATH[] paths; MODE[] modes; Query(QDC_ONLY_ACTIVE_PATHS, out paths, out modes);
            List<SavedDisplayLayout> result = new List<SavedDisplayLayout>();
            foreach (PATH path in paths)
            {
                if (path.sourceInfo.modeInfoIdx >= modes.Length || path.targetInfo.modeInfoIdx >= modes.Length)
                    throw new InvalidOperationException("Windows has not supplied complete display modes yet.");
                MODE source = modes[path.sourceInfo.modeInfoIdx], target = modes[path.targetInfo.modeInfoIdx];
                if (source.infoType != 1 || target.infoType != 2) throw new InvalidOperationException("Windows returned an unexpected display mode.");
                result.Add(new SavedDisplayLayout {
                    DevicePath = TargetName(path).monitorDevicePath,
                    Width = source.data.source.width, Height = source.data.source.height, PixelFormat = source.data.source.pixelFormat,
                    X = source.data.source.x, Y = source.data.source.y, Rotation = path.targetInfo.rotation, Scaling = path.targetInfo.scaling,
                    RefreshNumerator = path.targetInfo.refreshRate.Numerator, RefreshDenominator = path.targetInfo.refreshRate.Denominator,
                    TargetSignal = EncodeSignal(target.data.target)
                });
            }
            return result;
        }

        static string EncodeSignal(SIGNAL signal)
        {
            IntPtr memory = Marshal.AllocHGlobal(48);
            try { Marshal.StructureToPtr(signal, memory, false); byte[] bytes = new byte[48]; Marshal.Copy(memory, bytes, 0, bytes.Length); return Convert.ToBase64String(bytes); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        static SIGNAL DecodeSignal(string value)
        {
            byte[] bytes = Convert.FromBase64String(value);
            if (bytes.Length != 48) throw new InvalidOperationException("The saved display timing is invalid. Capture the layout again.");
            IntPtr memory = Marshal.AllocHGlobal(48);
            try { Marshal.Copy(bytes, 0, memory, bytes.Length); return (SIGNAL)Marshal.PtrToStructure(memory, typeof(SIGNAL)); }
            finally { Marshal.FreeHGlobal(memory); }
        }

        public static void ValidateLayout(IEnumerable<string> devices, IList<SavedDisplayLayout> layout)
        {
            if (layout == null || layout.Count == 0) return; // Existing profiles keep working.
            HashSet<string> wanted = new HashSet<string>(devices, StringComparer.OrdinalIgnoreCase);
            if (layout.Count != wanted.Count || !wanted.SetEquals(layout.Select(x => x.DevicePath)))
                throw new InvalidOperationException("The saved layout does not match the selected displays. Capture it again.");
            if (layout.Count(x => x.Primary) != 1 || layout.Any(x => x.Width == 0 || x.Height == 0 || x.RefreshDenominator == 0))
                throw new InvalidOperationException("Capture an extended layout with one primary display.");
            foreach (SavedDisplayLayout item in layout) DecodeSignal(item.TargetSignal);
        }

        // Enable available destination/control paths without releasing any outgoing path.
        // Missing destinations can appear after their physical input has been changed.
        public static void PrepareForInputs(IEnumerable<string> devices)
        {
            List<MonitorInfo> monitors = GetMonitors();
            HashSet<string> desired = new HashSet<string>(devices, StringComparer.OrdinalIgnoreCase);
            List<string> union = monitors.Where(x => x.Active || desired.Contains(x.DevicePath)).Select(x => x.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (union.Count > 0 && !new HashSet<string>(monitors.Where(x => x.Active).Select(x => x.DevicePath), StringComparer.OrdinalIgnoreCase).SetEquals(union)) ApplyDevicePaths(union);
        }

        public static void ApplyProfile(DisplayProfile profile)
        {
            ValidateLayout(profile.Devices, profile.Layout);
            ApplyDevicePaths(profile.Devices);
            if (profile.Layout != null && profile.Layout.Count > 0) ApplyLayout(profile.Layout);
        }

        public static void ApplyLayout(IList<SavedDisplayLayout> layout)
        {
            ValidateLayout(layout.Select(x => x.DevicePath), layout);
            PATH[] current; MODE[] oldModes; Query(QDC_ONLY_ACTIVE_PATHS, out current, out oldModes);
            List<PATH> selected = new List<PATH>(); List<MODE> supplied = new List<MODE>(); HashSet<string> sources = new HashSet<string>();
            foreach (SavedDisplayLayout item in layout.OrderByDescending(x => x.Primary))
            {
                PATH[] matches = current.Where(x => String.Equals(TargetName(x).monitorDevicePath, item.DevicePath, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("A saved display is not active. Check its cable and input.");
                PATH path = matches[0];
                if (!sources.Add(Key(path.sourceInfo.adapterId, path.sourceInfo.id))) throw new InvalidOperationException("Extend the displays before restoring this layout.");
                path.sourceInfo.modeInfoIdx = (uint)supplied.Count;
                supplied.Add(new MODE { infoType = 1, id = path.sourceInfo.id, adapterId = path.sourceInfo.adapterId,
                    data = new MODEUNION { source = new SOURCEMODE { width = item.Width, height = item.Height, pixelFormat = item.PixelFormat, x = item.X, y = item.Y } } });
                path.targetInfo.modeInfoIdx = (uint)supplied.Count;
                SIGNAL signal = DecodeSignal(item.TargetSignal);
                supplied.Add(new MODE { infoType = 2, id = path.targetInfo.id, adapterId = path.targetInfo.adapterId, data = new MODEUNION { target = signal } });
                path.targetInfo.rotation = item.Rotation; path.targetInfo.scaling = item.Scaling;
                path.targetInfo.refreshRate = new RATIONAL { Numerator = item.RefreshNumerator, Denominator = item.RefreshDenominator };
                path.targetInfo.scanLineOrdering = signal.scanLineOrdering; path.flags = PATH_ACTIVE; selected.Add(path);
            }
            int error = SetDisplayConfig((uint)selected.Count, selected.ToArray(), (uint)supplied.Count, supplied.ToArray(), 0x40 | SDC_USE_SUPPLIED);
            if (error != 0) throw new InvalidOperationException("The saved resolution or refresh rate is unavailable (Windows error " + error + "). Capture a supported layout.");
            error = SetDisplayConfig((uint)selected.Count, selected.ToArray(), (uint)supplied.Count, supplied.ToArray(), SDC_APPLY | SDC_USE_SUPPLIED | SDC_SAVE);
            if (error != 0) throw new InvalidOperationException("Windows could not restore the saved layout (error " + error + ").");
            Thread.Sleep(500);
            if (!LayoutMatches(layout, CaptureLayout())) throw new InvalidOperationException("Windows did not restore the saved positions, primary display, resolution and refresh rate.");
        }

        public static bool LayoutMatches(IList<SavedDisplayLayout> expected, IList<SavedDisplayLayout> actual)
        {
            if (expected.Count != actual.Count) return false;
            return expected.All(e => actual.Any(a => String.Equals(e.DevicePath, a.DevicePath, StringComparison.OrdinalIgnoreCase)
                && e.X == a.X && e.Y == a.Y && e.Width == a.Width && e.Height == a.Height && e.Rotation == a.Rotation
                && e.RefreshDenominator > 0 && a.RefreshDenominator > 0
                && Math.Abs((double)e.RefreshNumerator / e.RefreshDenominator - (double)a.RefreshNumerator / a.RefreshDenominator) < 0.1));
        }

        public static void RestoreLayout(IList<SavedDisplayLayout> previous)
        {
            if (LayoutMatches(previous, CaptureLayout())) return;
            List<string> available = GetMonitors().Select(x => x.DevicePath).ToList();
            List<SavedDisplayLayout> retained = previous.Where(x => available.Contains(x.DevicePath, StringComparer.OrdinalIgnoreCase)).ToList();
            if (retained.Count == 0) throw new InvalidOperationException("None of the previous displays are connected. Reconnect the dock or monitor.");
            ApplyDevicePaths(retained.Select(x => x.DevicePath));
            if (retained.Count == previous.Count) ApplyLayout(previous);
            // An unplugged laptop keeps its remaining panel; don't apply invalid old coordinates.
        }
    }
}
