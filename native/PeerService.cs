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
    public static class PeerDiagnostics
    {
        static readonly object Sync = new object(); public static readonly string PathName = Path.Combine(ConfigStore.ConfigDirectory, "peer.log");
        public static void Write(string message)
        {
            try { lock (Sync) { Directory.CreateDirectory(ConfigStore.ConfigDirectory); if (File.Exists(PathName) && new FileInfo(PathName).Length > 262144) File.Move(PathName, PathName + ".old", true); File.AppendAllText(PathName, DateTimeOffset.Now.ToString("O") + " " + message.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine); } } catch { }
        }
    }

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
            using (client)
            {
                string remote = client.Client.RemoteEndPoint == null ? "unknown" : client.Client.RemoteEndPoint.ToString(); NetworkStream stream = null;
                try
                {
                    client.ReceiveTimeout = 55000; client.SendTimeout = 5000; stream = client.GetStream(); using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true)) using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    {
                        string line = reader.ReadLine(); if (line == null || line.Length > 8192) throw new InvalidOperationException("Malformed request."); string[] parts = line.Split(new[] { '|' }, 4); if (parts.Length != 4) throw new InvalidOperationException("Malformed request.");
                        long timestamp; if (!Int64.TryParse(parts[0], out timestamp) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 30) throw new InvalidOperationException("Request expired. Check that both PCs have the correct time.");
                        lock (seen) { if (!seen.Add(parts[1])) throw new InvalidOperationException("Repeated request rejected."); if (seen.Count > 1000) seen.Clear(); }
                        string signed = parts[0] + "|" + parts[1] + "|" + parts[2]; if (!FixedEquals(Sign(signed), parts[3])) throw new InvalidOperationException("Authentication failed. The pairing keys do not match.");
                        string command = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])); PeerDiagnostics.Write("IN " + remote + " " + CommandName(command)); string result = commandHandler(command); writer.WriteLine("OK|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(result ?? "OK"))); PeerDiagnostics.Write("IN " + remote + " OK");
                    }
                }
                catch (Exception ex)
                {
                    PeerDiagnostics.Write("IN " + remote + " ERROR " + ex.Message); try { if (stream != null && stream.CanWrite) using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true }) writer.WriteLine("ERR|" + ex.Message.Replace('|', '/')); } catch (Exception replyError) { PeerDiagnostics.Write("IN " + remote + " REPLY_ERROR " + replyError.Message); }
                }
                finally { if (stream != null) stream.Dispose(); }
            }
        }
        static string CommandName(string command) { if (command.StartsWith("PROFILE:")) return "PROFILE"; if (command.StartsWith("TX|")) { string[] p = command.Split('|'); return p.Length > 1 ? "TX_" + p[1] : "TX"; } return command; }
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
                try { if (!client.ConnectAsync(config.PeerHost, config.PeerPort).Wait(5000)) throw new InvalidOperationException("Connection timed out."); } catch (Exception ex) { PeerDiagnostics.Write("OUT " + config.PeerHost + ":" + config.PeerPort + " CONNECT_ERROR " + ex.GetBaseException().Message); throw new InvalidOperationException("Cannot reach " + config.PeerHost + ":" + config.PeerPort + ". Ensure DisplayCue is running there and allowed through Windows Firewall."); }
                client.ReceiveTimeout = 55000; client.SendTimeout = 5000; using (NetworkStream stream = client.GetStream()) using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true)) using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    PeerDiagnostics.Write("OUT " + config.PeerHost + ":" + config.PeerPort + " " + CommandName(command)); writer.WriteLine(signed + "|" + Sign(signed)); string response; try { response = reader.ReadLine(); } catch (IOException) { throw new InvalidOperationException(config.PeerHost + " did not finish the request within 55 seconds. See " + PeerDiagnostics.PathName); }
                    if (response == null) throw new InvalidOperationException(config.PeerHost + " closed the connection before replying. Update DisplayCue on both PCs, then check " + PeerDiagnostics.PathName);
                    if (response.StartsWith("OK|")) { PeerDiagnostics.Write("OUT " + config.PeerHost + " OK"); try { return Encoding.UTF8.GetString(Convert.FromBase64String(response.Substring(3))); } catch { throw new InvalidOperationException(config.PeerHost + " returned a damaged success response. See " + PeerDiagnostics.PathName); } }
                    if (response.StartsWith("ERR|")) { PeerDiagnostics.Write("OUT " + config.PeerHost + " ERROR " + response.Substring(4)); throw new InvalidOperationException(config.PeerHost + " reported: " + response.Substring(4)); }
                    PeerDiagnostics.Write("OUT " + config.PeerHost + " INVALID " + response.Substring(0, Math.Min(120, response.Length))); throw new InvalidOperationException(config.PeerHost + " returned an unsupported response. Ensure both PCs run the same DisplayCue version. See " + PeerDiagnostics.PathName);
                }
            }
        }
        public void Dispose() { Stop(); }
    }
}
