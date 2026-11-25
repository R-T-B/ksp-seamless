// sdfgen - TrueType font -> signed distance field atlas + glyph metrics for KSP's (old,
// TMP 1.0-style) TextMeshPro, built into a TMP_FontAsset at runtime by the mod.
//
//   dotnet run --project tools/sdfgen -- <font.ttf> <out base path> [emPixels=64] [gradient=10] [preview.png]
//
// Writes <base>.atlas (int32 width, int32 height, then width*height alpha bytes, bottom row
// first = Texture2D.LoadRawTextureData order) and <base>.metrics (text: face info, glyphs,
// kerning). Distances are exact (to the flattened outline), sign from the non-zero winding
// rule; alpha = 0.5 + d / gradient (d in atlas pixels, positive inside), so the material's
// _GradientScale = gradient gives one-screen-pixel edges in TMP's distance-field shader.

using System.Globalization;
using System.IO.Compression;
using System.Text;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: sdfgen <font.ttf> <outBase> [em=64] [gradient=10] [preview.png]"); return 1; }
        var font = new TrueType(File.ReadAllBytes(args[0]));
        string outBase = args[1];
        int em = args.Length > 2 ? int.Parse(args[2]) : 64;
        int gradient = args.Length > 3 ? int.Parse(args[3]) : 10;
        string preview = args.Length > 4 ? args[4] : null;

        var chars = new List<int>();
        for (int c = 32; c < 127; c++) chars.Add(c);
        for (int c = 0xA0; c <= 0xFF; c++) chars.Add(c);
        chars.AddRange(new[] { 0x2013, 0x2014, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x2212 });

        double s = (double)em / font.UnitsPerEm;
        int margin = gradient + 2;   // TMP pads quads by up to _GradientScale texels
        var glyphs = new List<GlyphOut>();
        foreach (int c in chars)
        {
            int gid = font.GlyphIndex(c);
            if (gid == 0 && c != 32) continue;
            var contours = font.Outline(gid);
            var g = new GlyphOut { Code = c, Gid = gid, Advance = font.Advance(gid) * s };
            if (contours.Count > 0 && c != 32)
            {
                double xmin = double.MaxValue, ymin = double.MaxValue, xmax = double.MinValue, ymax = double.MinValue;
                foreach (var ct in contours) foreach (var p in ct) { xmin = Math.Min(xmin, p.X); ymin = Math.Min(ymin, p.Y); xmax = Math.Max(xmax, p.X); ymax = Math.Max(ymax, p.Y); }
                g.L = (int)Math.Floor(xmin * s); g.B = (int)Math.Floor(ymin * s);
                g.R = (int)Math.Ceiling(xmax * s); g.T = (int)Math.Ceiling(ymax * s);
                g.Segs = Flatten(contours, s);
            }
            glyphs.Add(g);
        }

        // shelf packing, tallest first
        int atlasW = 1024;
        var order = glyphs.Where(g => g.Segs != null).OrderByDescending(g => g.T - g.B).ToList();
        int x = 0, y = 0, shelf = 0;
        foreach (var g in order)
        {
            int w = g.R - g.L + 2 * margin, h = g.T - g.B + 2 * margin;
            if (x + w > atlasW) { x = 0; y += shelf; shelf = 0; }
            g.AX = x; g.AY = y;   // top-left of the padded box, y down
            x += w; shelf = Math.Max(shelf, h);
        }
        int atlasH = 64;
        while (atlasH < y + shelf) atlasH *= 2;

        var px = new byte[atlasW * atlasH];   // row 0 = top
        Parallel.ForEach(order, g =>
        {
            int w = g.R - g.L + 2 * margin, h = g.T - g.B + 2 * margin;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    // pixel centre in glyph pixel space (y up)
                    double gx = g.L - margin + i + 0.5;
                    double gy = g.T + margin - j - 0.5;
                    double d = Distance(g.Segs, gx, gy, out int winding);
                    if (winding != 0) d = -d;   // inside: positive
                    d = -d;
                    double a = 0.5 + d / gradient;
                    px[(g.AY + j) * atlasW + g.AX + i] = (byte)Math.Round(Math.Clamp(a, 0, 1) * 255);
                }
        });

        using (var fs = File.Create(outBase + ".atlas"))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(atlasW); bw.Write(atlasH);
            for (int r = atlasH - 1; r >= 0; r--) bw.Write(px, r * atlasW, atlasW);
        }

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# sdfgen output for " + Path.GetFileName(args[0]) + " - line formats: face <key> <value> | glyph id x y w h xOffset yOffset xAdvance | kern first second offset");
        void Face(string k, double v) => sb.AppendLine("face " + k + " " + v.ToString("R", inv));
        sb.AppendLine("name " + font.FamilyName);
        Face("PointSize", em);
        Face("Ascender", font.Ascender * s);
        Face("Descender", font.Descender * s);
        Face("LineHeight", (font.Ascender - font.Descender + font.LineGap) * s);
        Face("CapHeight", (font.CapHeight > 0 ? font.CapHeight : font.Ascender * 0.7) * s);
        Face("Underline", -0.12 * em);
        Face("UnderlineThickness", 0.05 * em);
        Face("Padding", gradient);
        Face("GradientScale", gradient);
        Face("AtlasWidth", atlasW);
        Face("AtlasHeight", atlasH);
        foreach (var g in glyphs.OrderBy(g => g.Code))
        {
            if (g.Segs == null)
                sb.AppendLine(string.Format(inv, "glyph {0} 0 0 0 0 0 0 {1:R}", g.Code, g.Advance));
            else
                sb.AppendLine(string.Format(inv, "glyph {0} {1} {2} {3} {4} {5} {6} {7:R}", g.Code,
                    g.AX + margin, g.AY + margin, g.R - g.L, g.T - g.B, g.L, g.T, g.Advance));
        }
        int kerns = 0;
        var byGid = glyphs.GroupBy(g => g.Gid).ToDictionary(k => k.Key, k => k.Select(q => q.Code).ToList());
        foreach (var kv in font.Kerning)
        {
            int l = (int)(kv.Key >> 16), r = (int)(kv.Key & 0xFFFF);
            if (!byGid.TryGetValue(l, out var lc) || !byGid.TryGetValue(r, out var rc)) continue;
            foreach (int a in lc) foreach (int b in rc)
                { sb.AppendLine(string.Format(inv, "kern {0} {1} {2:R}", a, b, kv.Value * s)); kerns++; }
        }
        File.WriteAllText(outBase + ".metrics", sb.ToString());
        Console.WriteLine($"{font.FamilyName}: {glyphs.Count} glyphs, {kerns} kerning pairs, atlas {atlasW}x{atlasH}, em {em}px, gradient {gradient}");

        if (preview != null) Png.WriteGray(preview, atlasW, atlasH, px);
        return 0;
    }

    sealed class GlyphOut
    {
        public int Code, Gid, L, B, R, T, AX, AY;
        public double Advance;
        public List<(double x0, double y0, double x1, double y1)> Segs;
    }

    static List<(double, double, double, double)> Flatten(List<List<Pt>> contours, double s)
    {
        var segs = new List<(double, double, double, double)>();
        foreach (var ct in contours)
        {
            // ct: points with on-curve flags, TrueType quadratic rules
            int n = ct.Count;
            if (n < 2) continue;
            // start at an on-curve point (or the midpoint of the first two off points)
            int start = ct.FindIndex(p => p.On);
            Pt first;
            List<Pt> seq = new List<Pt>();
            if (start < 0)
            {
                first = new Pt((ct[0].X + ct[1].X) / 2, (ct[0].Y + ct[1].Y) / 2, true);
                seq.Add(first);
                for (int k = 1; k <= n; k++) seq.Add(ct[k % n]);
                seq.Add(first);
            }
            else
            {
                for (int k = 0; k <= n; k++) seq.Add(ct[(start + k) % n]);
            }
            Pt cur = seq[0];
            for (int k = 1; k < seq.Count; k++)
            {
                Pt p = seq[k];
                if (p.On) { segs.Add((cur.X * s, cur.Y * s, p.X * s, p.Y * s)); cur = p; continue; }
                Pt next = seq[(k + 1) < seq.Count ? k + 1 : 0];
                Pt end = next.On ? next : new Pt((p.X + next.X) / 2, (p.Y + next.Y) / 2, true);
                const int N = 10;
                double px0 = cur.X, py0 = cur.Y;
                for (int t = 1; t <= N; t++)
                {
                    double u = t / (double)N, a = (1 - u) * (1 - u), b = 2 * u * (1 - u), c = u * u;
                    double qx = a * cur.X + b * p.X + c * end.X, qy = a * cur.Y + b * p.Y + c * end.Y;
                    segs.Add((px0 * s, py0 * s, qx * s, qy * s));
                    px0 = qx; py0 = qy;
                }
                cur = end;
                if (next.On) k++;
            }
        }
        return segs;
    }

    static double Distance(List<(double x0, double y0, double x1, double y1)> segs, double px, double py, out int winding)
    {
        double best = double.MaxValue;
        winding = 0;
        foreach (var (x0, y0, x1, y1) in segs)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Clamp(((px - x0) * dx + (py - y0) * dy) / l2, 0, 1) : 0;
            double ex = x0 + t * dx - px, ey = y0 + t * dy - py;
            double d2 = ex * ex + ey * ey;
            if (d2 < best) best = d2;
            // non-zero winding, horizontal ray to +x
            if (y0 <= py) { if (y1 > py && (dx * (py - y0) - (px - x0) * dy) > 0) winding++; }
            else { if (y1 <= py && (dx * (py - y0) - (px - x0) * dy) < 0) winding--; }
        }
        return Math.Sqrt(best);
    }
}

readonly struct Pt
{
    public readonly double X, Y; public readonly bool On;
    public Pt(double x, double y, bool on) { X = x; Y = y; On = on; }
}

sealed class TrueType
{
    readonly byte[] d;
    readonly Dictionary<string, (int off, int len)> tables = new();
    public int UnitsPerEm, Ascender, Descender, LineGap, CapHeight, NumGlyphs, NumHMetrics, LocFormat;
    public string FamilyName = "font";
    public readonly Dictionary<uint, int> Kerning = new();
    readonly Dictionary<int, int> cmap = new();

    public TrueType(byte[] data)
    {
        d = data;
        int n = U16(4);
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            tables[Encoding.ASCII.GetString(d, r, 4)] = ((int)U32(r + 8), (int)U32(r + 12));
        }
        if (!tables.ContainsKey("glyf")) throw new Exception("no glyf table (CFF/OTF fonts are not supported - use the .ttf)");
        int head = tables["head"].off;
        UnitsPerEm = U16(head + 18);
        LocFormat = S16(head + 50);
        NumGlyphs = U16(tables["maxp"].off + 4);
        int hhea = tables["hhea"].off;
        Ascender = S16(hhea + 4); Descender = S16(hhea + 6); LineGap = S16(hhea + 8);
        NumHMetrics = U16(hhea + 34);
        if (tables.TryGetValue("OS/2", out var os2) && U16(os2.off) >= 2) CapHeight = S16(os2.off + 88);
        ReadCmap();
        ReadKern();
        ReadName();
    }

    int U16(int o) => (d[o] << 8) | d[o + 1];
    int S16(int o) => (short)U16(o);
    uint U32(int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

    void ReadCmap()
    {
        int c = tables["cmap"].off;
        int n = U16(c + 2);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            int pid = U16(c + 4 + 8 * i), eid = U16(c + 6 + 8 * i);
            int off = (int)U32(c + 8 + 8 * i);
            if (pid == 3 && eid == 1 && U16(c + off) == 4) best = c + off;
            else if (best < 0 && pid == 0 && U16(c + off) == 4) best = c + off;
        }
        if (best < 0) throw new Exception("no format-4 cmap");
        int segX2 = U16(best + 6);
        int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
        for (int s = 0; s < segX2 / 2; s++)
        {
            int end = U16(ends + 2 * s), start = U16(starts + 2 * s), delta = S16(deltas + 2 * s), ro = U16(ranges + 2 * s);
            for (int ch = start; ch <= end && ch != 0xFFFF; ch++)
            {
                int g;
                if (ro == 0) g = (ch + delta) & 0xFFFF;
                else
                {
                    int ga = ranges + 2 * s + ro + 2 * (ch - start);
                    g = U16(ga);
                    if (g != 0) g = (g + delta) & 0xFFFF;
                }
                cmap[ch] = g;
            }
        }
    }

    void ReadKern()
    {
        if (!tables.TryGetValue("kern", out var k)) return;
        int o = k.off;
        int n = U16(o + 2);
        o += 4;
        for (int t = 0; t < n; t++)
        {
            int len = U16(o + 2), cov = U16(o + 4);
            if ((cov >> 8) == 0 && (cov & 1) == 1)
            {
                int pairs = U16(o + 6);
                for (int p = 0; p < pairs; p++)
                {
                    int r = o + 14 + 6 * p;
                    Kerning[((uint)U16(r) << 16) | (uint)U16(r + 2)] = S16(r + 4);
                }
            }
            o += len;
        }
    }

    void ReadName()
    {
        if (!tables.TryGetValue("name", out var t)) return;
        int o = t.off, n = U16(o + 2), so = o + U16(o + 4);
        for (int i = 0; i < n; i++)
        {
            int r = o + 6 + 12 * i;
            int pid = U16(r), nid = U16(r + 6), len = U16(r + 8), off = U16(r + 10);
            if (nid != 4) continue;
            FamilyName = pid == 3 || pid == 0
                ? Encoding.BigEndianUnicode.GetString(d, so + off, len)
                : Encoding.ASCII.GetString(d, so + off, len);
            if (pid == 3) break;
        }
    }

    public int GlyphIndex(int ch) => cmap.TryGetValue(ch, out int g) ? g : 0;

    public double Advance(int gid)
    {
        int h = tables["hmtx"].off;
        int i = Math.Min(gid, NumHMetrics - 1);
        return U16(h + 4 * i);
    }

    int GlyphOffset(int gid, out int len)
    {
        int loca = tables["loca"].off;
        int a, b;
        if (LocFormat == 0) { a = U16(loca + 2 * gid) * 2; b = U16(loca + 2 * gid + 2) * 2; }
        else { a = (int)U32(loca + 4 * gid); b = (int)U32(loca + 4 * gid + 4); }
        len = b - a;
        return tables["glyf"].off + a;
    }

    public List<List<Pt>> Outline(int gid, int depth = 0)
    {
        var result = new List<List<Pt>>();
        int o = GlyphOffset(gid, out int len);
        if (len <= 0 || depth > 8) return result;
        int nc = S16(o);
        if (nc >= 0) return Simple(o, nc);
        // composite
        int p = o + 10;
        while (true)
        {
            int flags = U16(p), sub = U16(p + 2);
            p += 4;
            double dx, dy;
            if ((flags & 1) != 0) { dx = S16(p); dy = S16(p + 2); p += 4; }
            else { dx = (sbyte)d[p]; dy = (sbyte)d[p + 1]; p += 2; }
            double a = 1, b = 0, c = 0, e = 1;
            if ((flags & 8) != 0) { a = e = F2(p); p += 2; }
            else if ((flags & 0x40) != 0) { a = F2(p); e = F2(p + 2); p += 4; }
            else if ((flags & 0x80) != 0) { a = F2(p); b = F2(p + 2); c = F2(p + 4); e = F2(p + 6); p += 8; }
            if ((flags & 2) == 0) { dx = 0; dy = 0; }   // point-matching offsets not supported
            foreach (var ct in Outline(sub, depth + 1))
                result.Add(ct.Select(q => new Pt(a * q.X + c * q.Y + dx, b * q.X + e * q.Y + dy, q.On)).ToList());
            if ((flags & 0x20) == 0) break;
        }
        return result;
    }

    double F2(int o) => S16(o) / 16384.0;

    List<List<Pt>> Simple(int o, int nc)
    {
        var ends = new int[nc];
        for (int i = 0; i < nc; i++) ends[i] = U16(o + 10 + 2 * i);
        int np = nc > 0 ? ends[nc - 1] + 1 : 0;
        int p = o + 10 + 2 * nc;
        int il = U16(p); p += 2 + il;
        var flags = new byte[np];
        for (int i = 0; i < np;)
        {
            byte f = d[p++];
            flags[i++] = f;
            if ((f & 8) != 0) { int r = d[p++]; while (r-- > 0 && i < np) flags[i++] = f; }
        }
        var xs = new int[np]; var ys = new int[np];
        int v = 0;
        for (int i = 0; i < np; i++)
        {
            byte f = flags[i];
            if ((f & 2) != 0) { int dv = d[p++]; v += (f & 16) != 0 ? dv : -dv; }
            else if ((f & 16) == 0) { v += S16(p); p += 2; }
            xs[i] = v;
        }
        v = 0;
        for (int i = 0; i < np; i++)
        {
            byte f = flags[i];
            if ((f & 4) != 0) { int dv = d[p++]; v += (f & 32) != 0 ? dv : -dv; }
            else if ((f & 32) == 0) { v += S16(p); p += 2; }
            ys[i] = v;
        }
        var res = new List<List<Pt>>();
        int s0 = 0;
        for (int c = 0; c < nc; c++)
        {
            var ct = new List<Pt>();
            for (int i = s0; i <= ends[c]; i++) ct.Add(new Pt(xs[i], ys[i], (flags[i] & 1) != 0));
            res.Add(ct);
            s0 = ends[c] + 1;
        }
        return res;
    }
}

static class Png
{
    public static void WriteGray(string path, int w, int h, byte[] px)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 0;
        Chunk(fs, "IHDR", ihdr);
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(px, y * w, w); }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(zs); }
        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, data.Length); s.Write(len);
        var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        var crc = new byte[4]; Be(crc, 0, (int)Crc(td)); s.Write(crc);
    }

    static uint Crc(byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in b)
        {
            c ^= x;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
