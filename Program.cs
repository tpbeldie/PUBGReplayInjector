using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace PUBGReplayInjector
{
  public static class Program
  {

    const string DEMOS_PATH = @"C:\Users\0x2b\AppData\Local\TslGame\Saved\Demos";
    const string PUBG_2604_PATH = @"C:\Users\0x2b\Desktop\PUBG-2604";
    const string STEAM_PUBG_PATH = @"C:\Program Files (x86)\Steam\steamapps\common\PUBG";
    const string STEAM_PUBG_BAK = @"C:\Program Files (x86)\Steam\steamapps\common\PUBG-2605bak";
    const string LOCAL_CONFIG_VDF = @"C:\Program Files (x86)\Steam\userdata\453224846\config\localconfig.vdf";
    const string BASE_LAUNCH_OPTS = "-koreanrating";
    const int STEAM_APP_ID = 578080;
    const string HOSTS_FILE = @"C:\Windows\System32\drivers\etc\hosts";
    const string HOSTS_MARKER = " # pubg-2604-launcher";

    static TcpListener? m_proxyHttp = null, m_proxyHttps = null;
    static X509Certificate2? m_proxyCert = null;
    static string? m_proxyCertThumb = null;

    static string[] VERSION_CHECK_HOSTS = {
       "prod-live.playbattlegrounds.com",
       "prod-live-xenuine.playbattlegrounds.com",
       "pctest-live-xenuine.playbattlegrounds.com",
       "session.pubg.com",
       "prod.dh.pubg.com",
       "patchr.pubg.com","zku-pcprod.acs.pubg.com",
    };

    public static void Main(string[] args)
    {

      Console.OutputEncoding = Encoding.UTF8;
      var replays = new List<ReplayEntry>();

      foreach (var dir in Directory.GetDirectories(DEMOS_PATH)) {
        var riPath = Path.Combine(dir, "PUBG.replayinfo");
        if (!File.Exists(riPath)) {
          continue;
        }
        try {
          var raw = File.ReadAllBytes(riPath);
          var json = Encoding.UTF8.GetString(raw, Math.Min(4, raw.Length), raw.Length - Math.Min(4, raw.Length));
          string version = Regex.Match(json, @"""GameVersion""\s*:\s*""([^""]+)""").Groups[1].Value;
          string mode = Regex.Match(json, @"""Mode""\s*:\s*""([^""]+)""").Groups[1].Value;
          long ms = long.TryParse(Regex.Match(json, @"""Timestamp""\s*:\s*(\d+)").Groups[1].Value, out var t) ? t : 0;
          var dt = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime : DateTime.MinValue;
          int lenMs = int.TryParse(Regex.Match(json, @"""LengthInMS""\s*:\s*(\d+)").Groups[1].Value, out var l) ? l : 0;
          string originalVersion = version;
          string bakPath = riPath + ".bak";
          if (File.Exists(bakPath)) {
            try {
              var br = File.ReadAllBytes(bakPath);
              var bj = Encoding.UTF8.GetString(br, Math.Min(4, br.Length), br.Length - Math.Min(4, br.Length));
              var bv = Regex.Match(bj, @"""GameVersion""\s*:\s*""([^""]+)""").Groups[1].Value;
              if (!string.IsNullOrEmpty(bv)) {
                originalVersion = bv;
              }
            }
            catch { }
          }
          var entry = new ReplayEntry {
            FolderName = Path.GetFileName(dir),
            FullPath = dir,
            Version = version,
            OriginalVersion = originalVersion,
            Mode = mode,
            Date = dt,
            LengthMs = lenMs
          };
          entry.NeedsOldBinary = !string.IsNullOrEmpty(originalVersion) && originalVersion.StartsWith("2604", StringComparison.Ordinal);
          replays.Add(entry);
        }
        catch { }
      }

      if (replays.Count == 0) {
        Console.WriteLine("No replays found: " + DEMOS_PATH); Console.ReadKey();
        return;
      }

      replays.Sort((a, b) => b.Date.CompareTo(a.Date));
      Console.Clear(); Console.ForegroundColor = ConsoleColor.Cyan;
      Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
      Console.WriteLine("║              PUBG Replay Launcher  -  pick a replay                 ║");
      Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
      Console.ResetColor(); Console.WriteLine();

      for (int i = 0; i < replays.Count; i++) {
        var r = replays[i];
        string ago = r.Date == DateTime.MinValue ? "?" : FormatAge(r.Date);
        string length = r.LengthMs > 0 ? $"{r.LengthMs / 60000}m{(r.LengthMs % 60000) / 1000:D2}s" : "?";
        ConsoleColor col = r.NeedsOldBinary ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.Write($"  [{i + 1,2}] ");
        Console.ForegroundColor = col;
        Console.Write((r.NeedsOldBinary ? " [2604]" : " [2605]").PadRight(8)); Console.ResetColor();
        Console.Write($" {r.Mode,-12} {length,6} {ago,-14}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        var parts = r.FolderName.Split('.');
        var seg = parts.Length > 5 ? parts[^3] : r.FolderName;
        Console.WriteLine(seg[..Math.Min(8, seg.Length)]); Console.ResetColor();
      }

      Console.WriteLine(); Console.Write("Enter number (or Q to quit): ");
      string? input = Console.ReadLine();
      if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) {
        return;
      }
      if (!int.TryParse(input.Trim(), out int pick) || pick < 1 || pick > replays.Count) {
        Console.WriteLine("Invalid.");
        Console.ReadKey();
        return;
      }

      LaunchReplay(replays[pick - 1]);
    }

    static void LaunchReplay(ReplayEntry r)
    {
      string riPath = Path.Combine(r.FullPath, "PUBG.replayinfo");
      string bakPath = riPath + ".bak";
      bool riSwapped = false;
      if (r.NeedsOldBinary && File.Exists(bakPath)) {
        try {
          File.Copy(riPath, riPath + ".2605tmp", true);
          File.Copy(bakPath, riPath, true);
          riSwapped = true;
          Console.ForegroundColor = ConsoleColor.DarkYellow;
          Console.WriteLine("Restored original .replayinfo for 2604 binary.");
          Console.ResetColor();
        }
        catch (Exception ex) {
          Console.WriteLine("Warn: " + ex.Message);
        }
      }
      if (r.NeedsOldBinary) {
        LaunchViaJunction(r, riPath, riSwapped);
      }
      else {
        Console.WriteLine("\nLaunching PUBG 2605 via Steam...");
        Process.Start(new ProcessStartInfo($"steam://rungameid/{STEAM_APP_ID}") {
          UseShellExecute = true
        });
        Console.WriteLine("Go to Career > Replays in-game.");
        Console.ReadKey();
      }
    }

    static void LaunchViaJunction(ReplayEntry r, string riPath, bool riSwapped)
    {
      foreach (var p in Process.GetProcessesByName("TslGame"))

        try {
          p.Kill();
          p.WaitForExit(5000);
        }
        catch { }

      if (Directory.Exists(STEAM_PUBG_BAK) && !IsJunction(STEAM_PUBG_BAK)) {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("Auto-recovering leftover state...");
        Console.ResetColor();
        try {
          if (IsJunction(STEAM_PUBG_PATH)) {
            Directory.Delete(STEAM_PUBG_PATH, false);
          }
          else if (Directory.Exists(STEAM_PUBG_PATH)) {
            Directory.Delete(STEAM_PUBG_PATH, true);
          }
          Directory.Move(STEAM_PUBG_BAK, STEAM_PUBG_PATH);
          Console.WriteLine("Recovered.");
        }
        catch (Exception ex) {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("Recovery failed: " + ex.Message);
          Console.ResetColor();
          RestoreRi(riPath, riSwapped); Console.ReadKey();
          return;
        }
      }

      bool dirSwapped = false, launchOptSwapped = false, hostsBlocked = false, proxyStarted = false;

      try {
        string replayArgs = $"{BASE_LAUNCH_OPTS} -nosplash -nomovie";
        InjectLaunchOptions(replayArgs);
        launchOptSwapped = true;

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  Launch options: {replayArgs}");
        Console.ResetColor();
        Console.WriteLine("\nSwapping Steam PUBG directory -> 2604 build...");
        Directory.Move(STEAM_PUBG_PATH, STEAM_PUBG_BAK);

        var mklink = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{STEAM_PUBG_PATH}\" \"{PUBG_2604_PATH}\"") {
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardOutput = true
        })!;

        mklink.WaitForExit(5000);
        dirSwapped = true;
        if (!Directory.Exists(STEAM_PUBG_PATH)) {
          throw new Exception("Junction failed.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Junction created.");
        Console.ResetColor();

        BlockVersionCheckHosts();
        hostsBlocked = true;
        StartVersionCheckProxy(r.OriginalVersion);
        proxyStarted = true;
        Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") {
          UseShellExecute = false,
          CreateNoWindow = true
        })?.WaitForExit(3000);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("[PROXY] Version check spoofed via hosts + HTTPS proxy.");
        Console.ResetColor();
        Process.Start(new ProcessStartInfo($"steam://rungameid/{STEAM_APP_ID}") {
          UseShellExecute = true
        });
        Console.WriteLine("\nWaiting for TslGame...");

        int waited = 0; while (Process.GetProcessesByName("TslGame").Length == 0 && waited < 60) {
          Thread.Sleep(1000);
          waited++;
        }

        if (Process.GetProcessesByName("TslGame").Length > 0) {
          Console.ForegroundColor = ConsoleColor.Green;
          Console.WriteLine("  TslGame is running!");
          Console.ResetColor();
          Console.WriteLine("Version check spoofed - game should reach the main lobby.");
          Console.WriteLine("Go to Career -> Replays -> click Play on your replay.");
        }
        else {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("Timed out.");
          Console.ResetColor();
        }

        Console.WriteLine("\nPress any key AFTER done to restore everything.");
        Console.ReadKey();

      }
      catch (Exception ex) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\nFailed: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine("Press any key...");
        Console.ReadKey();
      }
      finally {
        if (proxyStarted) try {
            StopVersionCheckProxy();
            Console.WriteLine("Proxy stopped.");
          }
          catch { }
        if (hostsBlocked) {
          try {
            UnblockVersionCheckHosts();
            Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") {
              UseShellExecute = false,
              CreateNoWindow = true
            })?.WaitForExit(3000);
            Console.WriteLine("Hosts unblocked.");
          }
          catch { }
        }
        if (launchOptSwapped) {
          try {
            InjectLaunchOptions(BASE_LAUNCH_OPTS);
            Console.WriteLine("Launch options restored.");
          }
          catch { }
        }
        if (dirSwapped) {
          try {
            foreach (var p in Process.GetProcessesByName("TslGame")) {
              try {
                p.Kill();
                p.WaitForExit(5000);
              }
              catch { }
            }
            if (IsJunction(STEAM_PUBG_PATH)) {
              Directory.Delete(STEAM_PUBG_PATH, false);
            }
            else if (Directory.Exists(STEAM_PUBG_PATH)) {
              Directory.Delete(STEAM_PUBG_PATH, true);
            }
            Directory.Move(STEAM_PUBG_BAK, STEAM_PUBG_PATH);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("PUBG directory restored to 2605.");
            Console.ResetColor();
          }
          catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"CRITICAL: {ex.Message}\nManually rename '{STEAM_PUBG_BAK}' -> '{STEAM_PUBG_PATH}'");
            Console.ResetColor();
          }
        }
        RestoreRi(riPath, riSwapped);
      }
    }

    static void InjectLaunchOptions(string opts)
    {
      string vdf = File.ReadAllText(LOCAL_CONFIG_VDF, Encoding.UTF8);
      string patched = Regex.Replace(vdf, @"(""578080""\s*\{[^}]*?""LaunchOptions""\s*"")[^""]*("")", m => m.Groups[1].Value + opts + m.Groups[2].Value, RegexOptions.Singleline);
      File.WriteAllText(LOCAL_CONFIG_VDF, patched, Encoding.UTF8);
    }

    static void StartVersionCheckProxy(string gameVersion)
    {
      // Change version depending on latest update
      if (string.IsNullOrEmpty(gameVersion)) gameVersion = "2604.1.1.63";
      using var rsa = RSA.Create(2048);
      var req = new CertificateRequest("CN=pubg-version-proxy", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      var san = new SubjectAlternativeNameBuilder();
      foreach (var h in VERSION_CHECK_HOSTS) san.AddDnsName(h);
      san.AddIpAddress(IPAddress.Loopback); req.CertificateExtensions.Add(san.Build());
      req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
      req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
      req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
      var tmp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
      m_proxyCert = new X509Certificate2(tmp.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
      m_proxyCertThumb = m_proxyCert.Thumbprint;
      using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) { store.Open(OpenFlags.ReadWrite); store.Add(m_proxyCert); store.Close(); }
      Console.WriteLine($"Cert installed ({m_proxyCertThumb[..8]}...)");
      StartProxyListener(ref m_proxyHttp, 80, false, gameVersion); StartProxyListener(ref m_proxyHttps, 443, true, gameVersion);
    }

    static void StartProxyListener(ref TcpListener? lf, int port, bool useTls, string gv)
    {
      try {
        lf = new TcpListener(IPAddress.Any, port);
        lf.Start();
        var cap = lf;
        Task.Run(() => AcceptProxyLoop(cap, useTls, gv));
      }
      catch (Exception ex) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Port {port} unavailable: {ex.Message}");
        Console.ResetColor();
        lf = null;
      }
    }

    static async Task AcceptProxyLoop(TcpListener l, bool useTls, string gv)
    {
      while (true) { try { var c = await l.AcceptTcpClientAsync(); _ = Task.Run(() => HandleProxyClient(c, useTls, gv)); } catch { break; } }
    }

    static void HandleProxyClient(TcpClient client, bool useTls, string gv)
    {
      try {
        using var _ = client;
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;
        var ep = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Stream stream = client.GetStream();
        SslStream? ssl = null;

        if (useTls) {
          try {
            ssl = new SslStream(stream, false);
            ssl.AuthenticateAsServer(m_proxyCert!, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
            stream = ssl;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[proxy] TLS hit from {ep}");
            Console.ResetColor();
          }
          catch {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[proxy] TLS fail from {ep}");
            Console.ResetColor();
            ssl?.Dispose();
            return;
          }
        }
        else {
          Console.ForegroundColor = ConsoleColor.Cyan;
          Console.WriteLine($"[proxy] HTTP hit from {ep}");
          Console.ResetColor();
        }
        var buf = new byte[16384];
        try {
          stream.Read(buf, 0, buf.Length);
        }
        catch { }

        // fuck... could be better? yeah... it works? I guess?
        string json = $"{{\"result\":\"ok\",\"status\":\"ok\",\"version\":\"{gv}\",\"current_version\":\"{gv}\",\"clientVersion\":\"{gv}\",\"minimumVersion\":\"{gv}\",\"latestVersion\":\"{gv}\",\"needsUpdate\":false,\"updateRequired\":false}}";

        var body = Encoding.UTF8.GetBytes(json);
        var hdr = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(hdr, 0, hdr.Length); stream.Write(body, 0, body.Length); stream.Flush(); ssl?.Dispose();

      }
      catch { }
    }

    static void StopVersionCheckProxy()
    {
      try { m_proxyHttp?.Stop(); } catch { }
      try { m_proxyHttps?.Stop(); } catch { }
      m_proxyHttp = null; m_proxyHttps = null;
      if (m_proxyCertThumb != null) {
        try {
          using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
          store.Open(OpenFlags.ReadWrite);
          foreach (var c in store.Certificates.Find(X509FindType.FindByThumbprint, m_proxyCertThumb, false))
            store.Remove(c);
          store.Close();
        }
        catch { }
        m_proxyCertThumb = null;
      }
      m_proxyCert?.Dispose();
      m_proxyCert = null;
    }

    static void BlockVersionCheckHosts()
    {
      var lines = new List<string>(File.ReadAllLines(HOSTS_FILE, Encoding.UTF8));
      bool changed = false;
      foreach (var h in VERSION_CHECK_HOSTS) {
        if (!lines.Any(l => l.Contains(h) && l.Contains(HOSTS_MARKER))) {
          lines.Add($"127.0.0.1 {h}{HOSTS_MARKER}");
          changed = true;
        }
      }
      if (changed) {
        File.WriteAllLines(HOSTS_FILE, lines, Encoding.UTF8);
      }
    }

    static void UnblockVersionCheckHosts()
    {
      File.WriteAllLines(HOSTS_FILE, File.ReadAllLines(HOSTS_FILE, Encoding.UTF8).Where(l => !l.Contains(HOSTS_MARKER)).ToList(), Encoding.UTF8);
    }

    static void RestoreRi(string riPath, bool riSwapped)
    {
      if (!riSwapped) return;
      try {
        string tmp = riPath + ".2605tmp";
        if (File.Exists(tmp)) {
          File.Copy(tmp, riPath, true);
          File.Delete(tmp);
          Console.ForegroundColor = ConsoleColor.DarkGray;
          Console.WriteLine("  .replayinfo restored.");
          Console.ResetColor();
        }
      }
      catch { }
    }

    static void AddFirewallBlock(string exePath)
    {
      const string rn = "pubg-2604-versionblock";
      Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall delete rule name=\"{rn}\"") {
        UseShellExecute = false,
        CreateNoWindow = true
      })?.WaitForExit(3000);

      Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall add rule name=\"{rn}\" dir=out action=block program=\"{exePath}\" protocol=tcp remoteport=80,443") {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true
      })!.WaitForExit(5000);
    }

    static void RemoveFirewallBlock()
    {
      Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall delete rule name=\"pubg-2604-versionblock\"") {
        UseShellExecute = false,
        CreateNoWindow = true
      })?.WaitForExit(3000);
    }

    static bool IsJunction(string path)
    {
      try {
        var i = new DirectoryInfo(path);
        return i.Exists && i.Attributes.HasFlag(FileAttributes.ReparsePoint);
      }
      catch {
        return false;
      }
    }

    static string FormatAge(DateTime dt)
    {
      var s = DateTime.Now - dt;
      if (s.TotalDays >= 1) {
        return $"{(int)s.TotalDays}d ago";
      }
      if (s.TotalHours >= 1) {
        return $"{(int)s.TotalHours}h ago";
      }
      return $"{(int)s.TotalMinutes}m ago";
    }

    class ReplayEntry
    {
      public string FolderName { get; set; } = "";
      public string FullPath { get; set; } = "";
      public string Version { get; set; } = "";
      public string OriginalVersion { get; set; } = "";
      public string Mode { get; set; } = "";
      public DateTime Date { get; set; } = DateTime.MinValue;
      public int LengthMs { get; set; } = 0;
      public bool NeedsOldBinary { get; set; } = false;
    }
  }
}
