using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MonitorHotkeys
{
    public static class PeerKeyStore
    {
        static readonly string PathName = Path.Combine(ConfigStore.ConfigDirectory, "peer.key");
        [StructLayout(LayoutKind.Sequential)] struct DATA_BLOB { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CryptProtectData(ref DATA_BLOB input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DATA_BLOB output);
        [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DATA_BLOB output);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);

        public static string Get()
        {
            if (!File.Exists(PathName)) return ""; byte[] encrypted = File.ReadAllBytes(PathName); DATA_BLOB input = Blob(encrypted), output;
            try { if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)) throw new InvalidOperationException("Windows could not unlock the pairing key (error " + Marshal.GetLastWin32Error() + ")."); try { byte[] clear = new byte[output.Size]; Marshal.Copy(output.Data, clear, 0, clear.Length); return Encoding.UTF8.GetString(clear); } finally { LocalFree(output.Data); } }
            finally { Marshal.FreeHGlobal(input.Data); }
        }
        public static void Set(string value)
        {
            byte[] clear = Encoding.UTF8.GetBytes(value ?? ""); DATA_BLOB input = Blob(clear), output;
            try { if (!CryptProtectData(ref input, "DisplayCue peer pairing key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)) throw new InvalidOperationException("Windows could not protect the pairing key (error " + Marshal.GetLastWin32Error() + ")."); try { byte[] encrypted = new byte[output.Size]; Marshal.Copy(output.Data, encrypted, 0, encrypted.Length); Directory.CreateDirectory(ConfigStore.ConfigDirectory); File.WriteAllBytes(PathName, encrypted); } finally { LocalFree(output.Data); } }
            finally { Marshal.FreeHGlobal(input.Data); }
        }
        public static string Generate()
        {
            byte[] bytes = new byte[32]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
        static DATA_BLOB Blob(byte[] bytes) { DATA_BLOB blob = new DATA_BLOB { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob.Data, bytes.Length); return blob; }
        public static void Clear() { if (File.Exists(PathName)) File.Delete(PathName); }
    }

    public sealed class PeerService : IDisposable
    {
        readonly AppConfig config; readonly Func<string, string> commandHandler; readonly HashSet<string> seen = new HashSet<string>(); readonly SemaphoreSlim connectionSlots = new SemaphoreSlim(4, 4);
        TcpListener listener; Thread thread; volatile bool stopping;
        public PeerService(AppConfig config, Func<string, string> commandHandler) { this.config = config; this.commandHandler = commandHandler; }
        byte[] Key()
        {
            string text = PeerKeyStore.Get(); if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No peer pairing key is configured.");
            try { byte[] key = Convert.FromBase64String(text); if (key.Length < 32) throw new FormatException(); return key; } catch { throw new InvalidOperationException("The peer pairing key is invalid."); }
        }
        public void Start()
        {
            Stop(); if (!config.PeerEnabled) return; Key(); stopping = false; listener = new TcpListener(IPAddress.Any, config.ListenPort); listener.Start(); TcpListener active = listener;
            thread = new Thread(new ThreadStart(delegate { Listen(active); })) { IsBackground = true, Name = "DisplayCue peer listener" }; thread.Start();
        }
        public void Stop() { stopping = true; try { if (listener != null) listener.Stop(); } catch { } try { if (thread != null && thread != Thread.CurrentThread) thread.Join(1000); } catch { } listener = null; thread = null; }
        void Listen(TcpListener active)
        {
            while (!stopping) try { TcpClient client = active.AcceptTcpClient(); if (!connectionSlots.Wait(0)) { client.Dispose(); continue; } ThreadPool.QueueUserWorkItem(delegate { try { Handle(client); } finally { connectionSlots.Release(); } }); } catch (SocketException) { if (!stopping) Thread.Sleep(250); } catch { if (!stopping) Thread.Sleep(250); }
        }
        void Handle(TcpClient client)
        {
            using (client) try
            {
                client.ReceiveTimeout = 35000; client.SendTimeout = 5000; using (NetworkStream stream = client.GetStream()) using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true)) using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    string line = reader.ReadLine(); if (line == null || line.Length > 8192) throw new InvalidOperationException("Malformed request."); string[] parts = line.Split(new[] { '|' }, 4); if (parts.Length != 4) throw new InvalidOperationException("Malformed request.");
                    long timestamp; if (!Int64.TryParse(parts[0], out timestamp) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 30) throw new InvalidOperationException("Expired request.");
                    lock (seen) { if (!seen.Add(parts[1])) throw new InvalidOperationException("Repeated request."); if (seen.Count > 1000) seen.Clear(); }
                    string signed = parts[0] + "|" + parts[1] + "|" + parts[2]; if (!FixedEquals(Sign(signed), parts[3])) throw new InvalidOperationException("Authentication failed.");
                    string command = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])); string result = commandHandler(command); writer.WriteLine("OK|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(result ?? "OK")));
                }
            }
            catch (Exception ex) { try { using (StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true }) writer.WriteLine("ERR|" + ex.Message.Replace('|', '/')); } catch { } }
        }
        string Sign(string value) { using (HMACSHA256 hmac = new HMACSHA256(Key())) return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static bool FixedEquals(string a, string b)
        {
            byte[] x, y; try { x = Convert.FromBase64String(a); y = Convert.FromBase64String(b); } catch { return false; } if (x.Length != y.Length) return false;
            int difference = 0; for (int i = 0; i < x.Length; i++) difference |= x[i] ^ y[i]; return difference == 0;
        }
        public string Send(string command)
        {
            if (String.IsNullOrWhiteSpace(config.PeerHost)) throw new InvalidOperationException("No paired computer address is configured.");
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); string nonce = Guid.NewGuid().ToString("N"); string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(command)); string signed = timestamp + "|" + nonce + "|" + payload;
            using (TcpClient client = new TcpClient())
            {
                if (!client.ConnectAsync(config.PeerHost, config.PeerPort).Wait(4000)) throw new InvalidOperationException("The paired computer did not respond.");
                client.ReceiveTimeout = 35000; client.SendTimeout = 5000; using (NetworkStream stream = client.GetStream()) using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true)) using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    writer.WriteLine(signed + "|" + Sign(signed)); string response = reader.ReadLine() ?? "";
                    if (response.StartsWith("OK|")) return Encoding.UTF8.GetString(Convert.FromBase64String(response.Substring(3)));
                    throw new InvalidOperationException(response.StartsWith("ERR|") ? response.Substring(4) : "The paired computer returned an invalid response.");
                }
            }
        }
        public void Dispose() { Stop(); }
    }
}
