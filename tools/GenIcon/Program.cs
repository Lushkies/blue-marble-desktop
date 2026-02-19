using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

int size = 256;
using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
using var g = Graphics.FromImage(bmp);
g.SmoothingMode = SmoothingMode.AntiAlias;
g.Clear(Color.Transparent);

// Ocean
using var oceanBrush = new LinearGradientBrush(
    new Rectangle(0, 0, size, size),
    Color.FromArgb(25, 70, 150),
    Color.FromArgb(10, 35, 85),
    LinearGradientMode.ForwardDiagonal);
g.FillEllipse(oceanBrush, 10, 10, size - 20, size - 20);

// Clip to globe circle
using var clipPath = new GraphicsPath();
clipPath.AddEllipse(12, 12, size - 24, size - 24);
g.SetClip(clipPath);

// Land masses
using var landBrush = new SolidBrush(Color.FromArgb(220, 50, 130, 45));
g.FillEllipse(landBrush, 40, 50, 70, 55);  // North America
g.FillEllipse(landBrush, 55, 40, 40, 30);
g.FillEllipse(landBrush, 65, 120, 40, 75); // South America
g.FillEllipse(landBrush, 130, 45, 40, 30); // Europe
g.FillEllipse(landBrush, 130, 80, 45, 85); // Africa
g.FillEllipse(landBrush, 165, 40, 55, 55); // Asia

g.ResetClip();

// Atmosphere glow
using var atmosPen = new Pen(Color.FromArgb(90, 100, 180, 255), 3);
g.DrawEllipse(atmosPen, 9, 9, size - 18, size - 18);

// Specular highlight
using var highlightBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
g.FillEllipse(highlightBrush, 55, 35, 90, 65);

// Save ICO
string outPath = args.Length > 0 ? args[0] :
    Path.Combine(AppContext.BaseDirectory, "bluemarbledesktop.ico");

int[] sizes = [16, 32, 48, 256];
using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

writer.Write((short)0);
writer.Write((short)1);
writer.Write((short)sizes.Length);

int headerSize = 6 + sizes.Length * 16;
var imageData = new List<byte[]>();

foreach (int sz in sizes)
{
    using var resized = new Bitmap(bmp, sz, sz);
    using var pngStream = new MemoryStream();
    resized.Save(pngStream, ImageFormat.Png);
    imageData.Add(pngStream.ToArray());
}

int offset = headerSize;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    writer.Write((byte)(s < 256 ? s : 0));
    writer.Write((byte)(s < 256 ? s : 0));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((short)1);
    writer.Write((short)32);
    writer.Write(imageData[i].Length);
    writer.Write(offset);
    offset += imageData[i].Length;
}

foreach (var data in imageData)
    writer.Write(data);

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllBytes(outPath, ms.ToArray());
Console.WriteLine($"Icon saved to: {Path.GetFullPath(outPath)}");
