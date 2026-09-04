using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MonitorHotkeys
{
    public sealed class DdcInputOption
    {
        public uint Value;
        public string Name;
        public override string ToString() { return Name; }
    }

    public sealed class DdcMonitorInfo
    {
        public string Name, DevicePath, PhysicalId, GdiName, Capabilities, Error;
        public uint? CurrentInput;
        public List<DdcInputOption> Inputs = new List<DdcInputOption>();
        internal IntPtr Handle;
        public bool SupportsInputSwitching { get { return Handle != IntPtr.Zero && Inputs.Count > 0; } }
    }

    public static class DdcEngine
    {
        const byte InputSourceVcp = 0x60;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public int left, top, right, bottom;
            public int workLeft, workTop, workRight, workBottom;
            public uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string deviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR
        {
            public IntPtr handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string description;
        }

        delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] physicalMonitors);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitor(IntPtr monitor);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)] static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

        public static List<DdcMonitorInfo> Discover()
        {
            List<MonitorInfo> displays = DisplayEngine.GetMonitors();
            List<DdcMonitorInfo> result = new List<DdcMonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                MONITORINFOEX logical = new MONITORINFOEX(); logical.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfo(hMonitor, ref logical)) return true;
                MonitorInfo display = displays.FirstOrDefault(x => x.GdiName.Equals(logical.deviceName, StringComparison.OrdinalIgnoreCase));
                uint count; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) return true;
                PHYSICAL_MONITOR[] physical = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return true;
                foreach (PHYSICAL_MONITOR item in physical)
                {
                    DdcMonitorInfo monitor = new DdcMonitorInfo
                    {
                        Name = display == null ? item.description : display.Name,
                        DevicePath = display == null ? logical.deviceName : display.DevicePath,
                        PhysicalId = display == null ? "" : PhysicalFingerprint(display.DevicePath),
                        GdiName = logical.deviceName,
                        Handle = item.handle
                    };
                    try
                    {
                        uint length;
                        if (GetCapabilitiesStringLength(item.handle, out length) && length > 1 && length < 65536)
                        {
                            StringBuilder text = new StringBuilder((int)length);
                            if (CapabilitiesRequestAndCapabilitiesReply(item.handle, text, length)) monitor.Capabilities = text.ToString();
                        }
                        monitor.Inputs = ParseInputOptions(monitor.Capabilities);
                        uint type, current, maximum;
                        if (GetVCPFeatureAndVCPFeatureReply(item.handle, InputSourceVcp, out type, out current, out maximum)) monitor.CurrentInput = current;
                        if (monitor.Inputs.Count == 0 && monitor.CurrentInput.HasValue)
                            monitor.Inputs.Add(new DdcInputOption { Value = monitor.CurrentInput.Value, Name = InputName(monitor.CurrentInput.Value) });
                    }
                    catch (Exception ex) { monitor.Error = ex.Message; }
                    result.Add(monitor);
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static void Release(IEnumerable<DdcMonitorInfo> monitors)
        {
            foreach (DdcMonitorInfo monitor in monitors)
            {
                if (monitor.Handle != IntPtr.Zero) { DestroyPhysicalMonitor(monitor.Handle); monitor.Handle = IntPtr.Zero; }
            }
        }

        public static void SetInput(DdcMonitorInfo monitor, uint value)
        {
            if (monitor == null || monitor.Handle == IntPtr.Zero) throw new InvalidOperationException("The monitor is no longer available for DDC/CI control.");
            if (!SetVCPFeature(monitor.Handle, InputSourceVcp, value)) throw new InvalidOperationException("The monitor rejected input " + InputName(value) + " (Windows error " + Marshal.GetLastWin32Error() + ").");
            monitor.CurrentInput = value;
        }

        public static string InputName(uint value)
        {
            switch (value)
            {
                case 0x01: return "VGA 1";
                case 0x03: return "DVI 1";
                case 0x04: return "DVI 2";
                case 0x0F: return "DisplayPort 1";
                case 0x10: return "DisplayPort 2";
                case 0x11: return "HDMI 1";
                case 0x12: return "HDMI 2";
                case 0x1B: return "USB-C";
                default: return "Input 0x" + value.ToString("X2");
            }
        }

        static string PhysicalFingerprint(string devicePath)
        {
            try
            {
                string[] parts = (devicePath ?? "").Split('#'); if (parts.Length < 3) return "";
                string keyPath = "SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\" + parts[1] + "\\" + parts[2] + "\\Device Parameters";
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    byte[] edid = key == null ? null : key.GetValue("EDID") as byte[]; if (edid == null || edid.Length < 128) return "";
                    List<byte> identity = new List<byte>(); identity.AddRange(edid.Skip(8).Take(8));
                    for (int offset = 54; offset + 18 <= 126; offset += 18) if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xff) identity.AddRange(edid.Skip(offset + 5).Take(13));
                    using (SHA256 sha = SHA256.Create()) return "edid1-" + Convert.ToBase64String(sha.ComputeHash(identity.ToArray())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                }
            }
            catch { return ""; }
        }

        static List<DdcInputOption> ParseInputOptions(string capabilities)
        {
            List<DdcInputOption> result = new List<DdcInputOption>();
            if (String.IsNullOrWhiteSpace(capabilities)) return result;
            int vcp = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
            if (vcp < 0) return result;
            int end = MatchingParen(capabilities, vcp + 3); if (end < 0) end = capabilities.Length;
            string body = capabilities.Substring(vcp + 4, end - vcp - 4);
            for (int i = 0; i + 2 <= body.Length; i++)
            {
                if (!IsHex(body[i]) || !IsHex(body[i + 1])) continue;
                string code = body.Substring(i, 2);
                if (!code.Equals("60", StringComparison.OrdinalIgnoreCase)) continue;
                int open = i + 2; while (open < body.Length && Char.IsWhiteSpace(body[open])) open++;
                if (open >= body.Length || body[open] != '(') continue;
                int close = MatchingParen(body, open); if (close < 0) continue;
                string[] values = body.Substring(open + 1, close - open - 1).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string raw in values)
                {
                    uint value; if (UInt32.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out value) && result.All(x => x.Value != value))
                        result.Add(new DdcInputOption { Value = value, Name = InputName(value) + " (0x" + value.ToString("X2") + ")" });
                }
                break;
            }
            return result;
        }

        static int MatchingParen(string value, int open)
        {
            int depth = 0;
            for (int i = open; i < value.Length; i++) { if (value[i] == '(') depth++; else if (value[i] == ')' && --depth == 0) return i; }
            return -1;
        }
        static bool IsHex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }
    }
}
