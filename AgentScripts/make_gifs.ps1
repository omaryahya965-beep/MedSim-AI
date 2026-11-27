# Assembles the evidence frame folders (Screenshots/Embodied/<clip>/f000.png …) into animated GIFs (10 fps).
# Minimal GIF89a encoder (6x7x6 colour cube + greys, LZW) — no external tools needed.
param([string]$Root = "D:\Medical-Test\My project\Screenshots\Embodied", [int]$Width = 480)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.IO; using System.Drawing; using System.Drawing.Imaging; using System.Collections.Generic;
public static class GifMaker {
  static byte[] Pal() { var p = new List<byte>(); for (int r = 0; r < 6; r++) for (int g = 0; g < 7; g++) for (int b = 0; b < 6; b++) { p.Add((byte)(r*51)); p.Add((byte)(g*42)); p.Add((byte)(b*51)); }
    for (int i = 0; i < 4; i++) { byte v = (byte)(32 + i*64); p.Add(v); p.Add(v); p.Add(v); } return p.ToArray(); } // 252 + 4 = 256
  static int Idx(int r, int g, int b) { int ri = Math.Min(5, (r*6+128)/256), gi = Math.Min(6, (g*7+128)/256), bi = Math.Min(5, (b*6+128)/256); return ri*42 + gi*6 + bi; }
  public static void Make(string[] files, string outPath, int width, int delayCs) {
    using (var fs = new FileStream(outPath, FileMode.Create)) using (var w = new BinaryWriter(fs)) {
      Bitmap first = new Bitmap(files[0]); int h = first.Height * width / first.Width; first.Dispose();
      w.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }); w.Write((ushort)width); w.Write((ushort)h); w.Write((byte)0xF7); w.Write((byte)0); w.Write((byte)0);
      var pal = Pal(); w.Write(pal); for (int i = pal.Length; i < 768; i++) w.Write((byte)0);
      w.Write(new byte[] { 0x21, 0xFF, 0x0B }); w.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); w.Write(new byte[] { 3, 1, 0, 0, 0 });
      foreach (var f in files) {
        using (var src = new Bitmap(f)) using (var bmp = new Bitmap(width, h, PixelFormat.Format24bppRgb)) {
          using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, width, h); }
          var px = new byte[width*h]; var data = bmp.LockBits(new Rectangle(0, 0, width, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
          var row = new byte[data.Stride]; float[] err = new float[(width+2)*3], nerr = new float[(width+2)*3];
          for (int y = 0; y < h; y++) { System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y*data.Stride, row, 0, data.Stride);
            for (int x = 0; x < width; x++) { int r = Clamp(row[x*3+2] + err[(x+1)*3]), gg = Clamp(row[x*3+1] + err[(x+1)*3+1]), b = Clamp(row[x*3] + err[(x+1)*3+2]);
              int k = Idx(r, gg, b); px[y*width+x] = (byte)k; float er = r - pal[k*3], eg = gg - pal[k*3+1], eb = b - pal[k*3+2];
              err[(x+2)*3] += er*7/16f; err[(x+2)*3+1] += eg*7/16f; err[(x+2)*3+2] += eb*7/16f;
              nerr[x*3] += er*3/16f; nerr[x*3+1] += eg*3/16f; nerr[x*3+2] += eb*3/16f; nerr[(x+1)*3] += er*5/16f; nerr[(x+1)*3+1] += eg*5/16f; nerr[(x+1)*3+2] += eb*5/16f;
              nerr[(x+2)*3] += er/16f; nerr[(x+2)*3+1] += eg/16f; nerr[(x+2)*3+2] += eb/16f; }
            var t = err; err = nerr; nerr = t; Array.Clear(nerr, 0, nerr.Length); }
          bmp.UnlockBits(data);
          w.Write(new byte[] { 0x21, 0xF9, 4, 0 }); w.Write((ushort)delayCs); w.Write((byte)0); w.Write((byte)0);
          w.Write((byte)0x2C); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)width); w.Write((ushort)h); w.Write((byte)0);
          Lzw(w, px, 8);
        }
      }
      w.Write((byte)0x3B);
    }
  }
  static int Clamp(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }
  static void Lzw(BinaryWriter w, byte[] px, int minCode) {
    w.Write((byte)minCode); int clear = 1 << minCode, eoi = clear + 1, next = eoi + 1, size = minCode + 1;
    var dict = new Dictionary<int, int>(); var outb = new List<byte>(); int acc = 0, bits = 0;
    Action<int> emit = code => { acc |= code << bits; bits += size; while (bits >= 8) { outb.Add((byte)(acc & 255)); acc >>= 8; bits -= 8; } };
    emit(clear); int prefix = px[0];
    for (int i = 1; i < px.Length; i++) { int c = px[i], key = (prefix << 8) | c; int v;
      if (dict.TryGetValue(key, out v)) { prefix = v; continue; }
      emit(prefix);
      if (next < 4096) { dict[key] = next++; if (next > (1 << size) && size < 12) size++; } else { emit(clear); dict.Clear(); next = eoi + 1; size = minCode + 1; }
      prefix = c; }
    emit(prefix); emit(eoi); if (bits > 0) outb.Add((byte)(acc & 255));
    for (int i = 0; i < outb.Count; i += 255) { int n = Math.Min(255, outb.Count - i); w.Write((byte)n); w.Write(outb.GetRange(i, n).ToArray()); }
    w.Write((byte)0);
  }
}
"@
Get-ChildItem $Root -Directory | ForEach-Object {
  $frames = Get-ChildItem $_.FullName -Filter "f*.png" | Sort-Object Name | ForEach-Object { $_.FullName }
  if ($frames.Count -gt 1) { $out = Join-Path $Root ($_.Name + ".gif"); [GifMaker]::Make($frames, $out, $Width, 10); "{0}: {1} frames → {2:N0} KB" -f $_.Name, $frames.Count, ((Get-Item $out).Length / 1KB) }
}
