using System.Text;

namespace PUBGReplayInjector
{
  public static class Scanner
  {
    public static void Scan()
    {
      var exePath = @"C:\Users\0x2b\Desktop\PUBG-2604\TslGame\Binaries\Win64\TslGame.exe";
      using var fs = File.OpenRead(exePath);
      var data = new byte[fs.Length];
      fs.Read(data, 0, data.Length);

      // Search ASCII URLs containing pubg/battle
      var patterns = new[] {
        "pubg.com",
        "playbattle",
        "patchr",
        "version/check",
        "clientcheck",
        "client-check",
        "needsUpdate",
        "needs_update",
        "versioncheck",
        "/api/v1",
        "/version",
        "updateCheck"
      };

      Console.WriteLine("=== ASCII URL/field hits ===");
      foreach (var pat in patterns) {
        var ab = Encoding.ASCII.GetBytes(pat);
        for (int i = 0; i <= data.Length - ab.Length; i++) {
          bool ok = true;
          for (int j = 0; j < ab.Length; j++) { if (data[i + j] != ab[j]) { ok = false; break; } }
          if (!ok) { continue; }
          int s = Math.Max(0, i - 60);
          int e = Math.Min(data.Length, i + 60);
          // extract printable context
          var sb = new System.Text.StringBuilder();
          for (int k = s; k < e; k++) {
            byte b = data[k]; sb.Append((b >= 0x20 && b < 0x7F) ? (char)b : '.');
          }
          Console.WriteLine($"  [0x{i:X8}] {sb}");
        }
      }

      // Search UTF-16LE for same patterns
      Console.WriteLine("\n=== UTF-16 URL/field hits ===");
      foreach (var pat in patterns) {
        var ub = Encoding.Unicode.GetBytes(pat);
        for (int i = 0; i <= data.Length - ub.Length; i++) {
          bool ok = true;
          for (int j = 0; j < ub.Length; j++) { if (data[i + j] != ub[j]) { ok = false; break; } }
          if (!ok) { continue; }
          // read UTF-16 string around hit
          int s = i; while (s >= 2 && data[s - 2] >= 0x20 && data[s - 2] < 0x7F && data[s - 1] == 0) { s -= 2; }
          int e = i + ub.Length;
          while (e + 1 < data.Length && data[e] >= 0x20 && data[e] < 0x7F && data[e + 1] == 0) { e += 2; }
          Console.WriteLine($"  [0x{i:X8}] {Encoding.Unicode.GetString(data, s, e - s)}");
        }
      }
    }
  }
}
