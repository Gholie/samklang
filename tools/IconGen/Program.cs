using SkiaSharp;
using Svg.Skia;

// Usage:
//   icontool png <input.svg> <outdir> [sizes...]      -> renders <name>-<size>.png per size
//   icontool ico <output.ico> <size:svg> [size:svg..] -> packs multi-res ICO (PNG-compressed entries)
//   icontool sheet <output.png> <size> <svg...>       -> contact sheet, light row + dark row
if (args.Length < 2) { Console.Error.WriteLine("bad args"); return 1; }

static SKBitmap RenderSvg(string path, int size)
{
    using var svg = new SKSvg();
    if (svg.Load(path) is not { } picture) throw new InvalidOperationException($"failed to load {path}");
    var bounds = picture.CullRect;
    var bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);
    float scale = Math.Min(size / bounds.Width, size / bounds.Height);
    canvas.Translate((size - bounds.Width * scale) / 2f, (size - bounds.Height * scale) / 2f);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Flush();
    return bmp;
}

static byte[] PngBytes(SKBitmap bmp)
{
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

switch (args[0])
{
    case "png":
    {
        var svgPath = args[1];
        var outDir = args[2];
        var name = Path.GetFileNameWithoutExtension(svgPath);
        Directory.CreateDirectory(outDir);
        foreach (var s in args[3..])
        {
            int size = int.Parse(s);
            using var bmp = RenderSvg(svgPath, size);
            File.WriteAllBytes(Path.Combine(outDir, $"{name}-{size}.png"), PngBytes(bmp));
        }
        Console.WriteLine($"rendered {name}: {string.Join(",", args[3..])}");
        return 0;
    }
    case "ico":
    {
        var outPath = args[1];
        var entries = new List<(int size, byte[] png)>();
        foreach (var spec in args[2..])
        {
            var parts = spec.Split(':', 2);
            int size = int.Parse(parts[0]);
            using var bmp = RenderSvg(parts[1], size);
            entries.Add((size, PngBytes(bmp)));
        }
        using var fs = File.Create(outPath);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)entries.Count);
        int offset = 6 + 16 * entries.Count;
        foreach (var (size, png) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size));  // width (0 = 256)
            w.Write((byte)(size >= 256 ? 0 : size));  // height
            w.Write((byte)0); w.Write((byte)0);       // palette count, reserved
            w.Write((ushort)1); w.Write((ushort)32);  // planes, bpp
            w.Write((uint)png.Length); w.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in entries) w.Write(png);
        Console.WriteLine($"wrote {outPath} ({entries.Count} entries)");
        return 0;
    }
    case "sheet":
    {
        var outPath = args[1];
        int size = int.Parse(args[2]);
        var svgs = args[3..];
        int pad = Math.Max(8, size / 8);
        int w = svgs.Length * (size + pad) + pad, h = size + 2 * pad;
        var sheet = new SKBitmap(w, h * 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(SKColors.White);
            using var dark = new SKPaint { Color = new SKColor(0x1e, 0x1e, 0x1e) };
            canvas.DrawRect(0, h, w, h, dark);
            for (int i = 0; i < svgs.Length; i++)
            {
                using var bmp = RenderSvg(svgs[i], size);
                using var img = SKImage.FromBitmap(bmp);
                canvas.DrawImage(img, pad + i * (size + pad), pad);
                canvas.DrawImage(img, pad + i * (size + pad), h + pad);
            }
            canvas.Flush();
        }
        File.WriteAllBytes(outPath, PngBytes(sheet));
        Console.WriteLine($"wrote {outPath}");
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown mode");
        return 1;
}
