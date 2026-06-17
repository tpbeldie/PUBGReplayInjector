using System;using System.Collections.Generic;using System.IO;using System.Text;
var path = @"C:\Users\0x2b\Desktop\PUBG-2604\TslGame\Binaries\Win64\TslGame.exe";
var search16 = Encoding.Unicode.GetBytes("DemoPlay");
var searchAsc = Encoding.ASCII.GetBytes("DemoPlay");
var hits = new List<(long offset, string enc)>();
using var fs = File.OpenRead(path);
var buf = new byte[4 * 1024 * 1024];
long filePos = 0; int overlap = 20;
byte[] tail = Array.Empty<byte>(); 
while (true) {
    int start = tail.Length;
    if (start > 0) Buffer.BlockCopy(tail, 0, buf, 0, start);
    int read = fs.Read(buf, start, buf.Length - start);
    int total = start + read;
    if (total < 10) break;
    for (int i = 0; i <= total - search16.Length; i++) {
        bool ok = true;
        for (int j = 0; j < search16.Length; j++) if (buf[i+j] != search16[j]) { ok=false; break; }
        if (ok) hits.Add((filePos - start + i, "UTF16"));
    }
    for (int i = 0; i <= total - searchAsc.Length; i++) {
        bool ok = true;
        for (int j = 0; j < searchAsc.Length; j++) if (buf[i+j] != searchAsc[j]) { ok=false; break; }
        if (ok) hits.Add((filePos - start + i, "ASCII"));
    }
    if (read == 0) break;
    tail = buf[(total - overlap)..total];
    filePos += read;
}
Console.WriteLine($"Found {hits.Count} hits:");
foreach (var h in hits) Console.WriteLine($"  0x{h.offset:X8}  [{h.enc}]");
