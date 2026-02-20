using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Load the user's custom logo PNG (transparent background)
string sourcePng = args.Length > 1 ? args[1] :
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "logo", "bluemarbledesktop.png");

if (!File.Exists(sourcePng))
{
    Console.WriteLine($"Error: Source PNG not found at: {Path.GetFullPath(sourcePng)}");
    return;
}

using var source = new Bitmap(sourcePng);
Console.WriteLine($"Loaded source: {source.Width}x{source.Height} from {Path.GetFullPath(sourcePng)}");

// Output path
string outPath = args.Length > 0 ? args[0] :
    Path.Combine(AppContext.BaseDirectory, "bluemarbledesktop.ico");

// Create multi-size ICO from the source PNG
int[] sizes = [16, 32, 48, 256];
using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

// ICO header
writer.Write((short)0);    // reserved
writer.Write((short)1);    // type: icon
writer.Write((short)sizes.Length);  // image count

int headerSize = 6 + sizes.Length * 16;
var imageData = new List<byte[]>();

foreach (int sz in sizes)
{
    using var resized = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(resized);
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.Clear(Color.Transparent);
    g.DrawImage(source, 0, 0, sz, sz);

    // Add a white circular outline around the globe edge for tray visibility
    float strokeWidth = sz switch
    {
        16 => 1.2f,
        32 => 2.0f,
        48 => 2.5f,
        _ => 6.0f   // 256px
    };
    float inset = strokeWidth / 2f;
    using var pen = new Pen(Color.White, strokeWidth);
    pen.Alignment = PenAlignment.Inset;
    g.DrawEllipse(pen, inset, inset, sz - strokeWidth, sz - strokeWidth);

    using var pngStream = new MemoryStream();
    resized.Save(pngStream, ImageFormat.Png);
    imageData.Add(pngStream.ToArray());
}

// Write ICO directory entries
int offset = headerSize;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    writer.Write((byte)(s < 256 ? s : 0));  // width (0 = 256)
    writer.Write((byte)(s < 256 ? s : 0));  // height (0 = 256)
    writer.Write((byte)0);   // color palette
    writer.Write((byte)0);   // reserved
    writer.Write((short)1);  // color planes
    writer.Write((short)32); // bits per pixel
    writer.Write(imageData[i].Length);  // image data size
    writer.Write(offset);               // offset from start
    offset += imageData[i].Length;
}

// Write image data
foreach (var data in imageData)
    writer.Write(data);

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllBytes(outPath, ms.ToArray());
Console.WriteLine($"Icon saved to: {Path.GetFullPath(outPath)}");
Console.WriteLine($"Sizes: {string.Join(", ", sizes.Select(s => $"{s}x{s}"))}");
