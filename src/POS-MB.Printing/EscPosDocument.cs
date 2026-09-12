using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace POS_MB.Printing;

// Builds a receipt as raw ESC/POS bytes - the standard command language almost
// every thermal receipt printer understands, regardless of brand. Each method
// appends a small, well-known byte sequence; nothing here is specific to any one
// printer model, and nothing here knows about Orders/Items/WinForms at all.
//
// Alongside the raw bytes, this also tracks a plain-text "preview" of the same
// content as it's built - lets ReceiptBuilder be checked/adjusted on screen
// (ToPreviewText) without needing a physical printer at all, from the exact same
// method calls that produce the real bytes, so the two can never drift apart.
public class EscPosDocument
{
    private const int Width = 32; // standard for an 80mm thermal roll at normal font size

    // Matches Width=32 chars/line at Font A (12x24 dots) on this printer's
    // 58mm roll. Arabic lines are rendered as an image at this pixel width
    // instead of as text (see Text()), so it has to line up with the same
    // physical paper width the plain-text lines already print at.
    private const int RasterWidthDots = 384;

    private static readonly Encoding Pc437 = GetLegacyEncoding(437);

    private static Encoding GetLegacyEncoding(int codePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch (InvalidOperationException)
        {
            // Already registered elsewhere in this process - fine, ignore.
        }
        return Encoding.GetEncoding(codePage);
    }

    private readonly List<byte> _bytes = [];
    private readonly List<string> _previewLines = [];
    private readonly StringBuilder _currentLine = new();
    private bool _bold;
    private bool _centered;
    private int _sizeMultiplier = 1;

    public EscPosDocument()
    {
        // ESC @ - reset the printer to its default state, so leftover formatting
        // from a previous receipt can never bleed into this one.
        _bytes.AddRange([0x1B, 0x40]);
    }

    public EscPosDocument Text(string text)
    {
        // Live testing (see PC720/WPC1256/PC864 diagnostic prints) proved this
        // printer's firmware has no real Arabic character ROM at all: every
        // "Arabic code page" it claims to support just displays extra bytes
        // through its one built-in Latin/CP437-style table, producing
        // different garbage per encoding rather than Arabic glyphs. No
        // ESC/POS codepage or .NET encoding can ever fix that - the printer
        // itself cannot draw those glyphs from character codes.
        //
        // The old POS avoided this entirely by never sending Arabic as
        // character codes: it used Windows GDI (Graphics.DrawString) to
        // rasterize the text into a bitmap and printed that as a picture.
        // Reproducing that here: any line containing non-ASCII text is
        // rendered as a small image (GDI shapes/reorders Arabic correctly on
        // its own, same as it did for the old POS) and sent using the
        // standard ESC/POS raster-image command, which every thermal printer
        // understands regardless of what fonts its firmware has built in.
        // Plain ASCII text keeps using the fast, tiny text path below.
        if (RequiresImageRendering(text))
        {
            _bytes.AddRange(RenderTextAsRaster(text));
        }
        else
        {
            _bytes.AddRange(Pc437.GetBytes(text));
        }

        _currentLine.Append(text);
        return this;
    }

    private static bool RequiresImageRendering(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F) return true;
        }
        return false;
    }

    private byte[] RenderTextAsRaster(string text)
    {
        // Font size in pixels, scaled the same way plain-text lines scale
        // with Size()/DoubleHeight() (see FlushPreviewLine's own use of
        // _sizeMultiplier), so an Arabic line looks the same size as an
        // English line at the same point in the receipt.
        var fontSize = 20f * _sizeMultiplier;
        var height = (int)Math.Ceiling(fontSize * 1.5);

        using var bitmap = new Bitmap(RasterWidthDots, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Arial, same as the old POS - Windows' own font-linking fills in
            // the actual Arabic glyphs, and GDI+ shapes/reorders them
            // automatically (contextual letter forms + right-to-left) since
            // that's what a Windows renderer always does for a Unicode
            // string, without needing any manual shaping in this code.
            using var font = new Font("Arial", fontSize, _bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = _centered ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(text, font, Brushes.Black, new RectangleF(0, 0, RasterWidthDots, height), format);
        }

        return BuildRasterCommand(bitmap);
    }

    // GS v 0 m xL xH yL yH d1...dk - prints a monochrome bitmap directly,
    // one bit per pixel (1 = black), packed 8 pixels per byte, row by row.
    // This is the one ESC/POS command that doesn't depend on the printer's
    // built-in character set at all - it's just pixels.
    private static byte[] BuildRasterCommand(Bitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;
        var data = new byte[widthBytes * bitmap.Height];

        var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var row = (byte*)bits.Scan0 + y * bits.Stride;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = row + x * 4; // B, G, R, A
                        var luminance = (pixel[2] * 299 + pixel[1] * 587 + pixel[0] * 114) / 1000;
                        if (luminance < 128)
                        {
                            data[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        List<byte> command =
        [
            0x1D, 0x76, 0x30, 0x00,
            (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
            (byte)(bitmap.Height & 0xFF), (byte)((bitmap.Height >> 8) & 0xFF)
        ];
        command.AddRange(data);
        return [.. command];
    }

    public EscPosDocument Line(string text = "") => Text(text).NewLine();

    public EscPosDocument NewLine()
    {
        _bytes.Add(0x0A);
        FlushPreviewLine();
        return this;
    }

    public EscPosDocument Bold(bool on)
    {
        // ESC E n - n=1 turns bold on, n=0 turns it off.
        _bytes.AddRange([0x1B, 0x45, (byte)(on ? 1 : 0)]);
        _bold = on;
        return this;
    }

    public EscPosDocument DoubleHeight(bool on) => Size(1, on ? 2 : 1);

    // GS ! n - n packs width (bits 0-2) and height (bits 4-6) magnification, each
    // as (multiplier - 1), so 1 = normal size, up to 8 = 8x. Used for the
    // configurable kitchen-ticket font size, and internally by DoubleHeight.
    public EscPosDocument Size(int width, int height)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        _bytes.AddRange([0x1D, 0x21, (byte)(((height - 1) << 4) | (width - 1))]);
        _sizeMultiplier = Math.Max(width, height);
        return this;
    }

    public EscPosDocument Center()
    {
        // ESC a n - n=1 centers, n=0 left-aligns.
        _bytes.AddRange([0x1B, 0x61, 1]);
        _centered = true;
        return this;
    }

    public EscPosDocument Left()
    {
        _bytes.AddRange([0x1B, 0x61, 0]);
        _centered = false;
        return this;
    }

    public EscPosDocument Divider(char character = '-', int width = Width) =>
        Line(new string(character, width));

    public EscPosDocument Feed(int lines = 3)
    {
        for (var i = 0; i < lines; i++) NewLine();
        return this;
    }

    public EscPosDocument Cut()
    {
        // GS V 0 - full paper cut. Comes after Feed() so the cut lands below
        // the last printed line instead of through it.
        _bytes.AddRange([0x1D, 0x56, 0x00]);
        _previewLines.Add(new string('=', Width) + " CUT " + new string('=', Width));
        return this;
    }

    public byte[] ToBytes() => [.. _bytes];

    public string ToPreviewText() => string.Join(Environment.NewLine, _previewLines);

    private void FlushPreviewLine()
    {
        var text = _currentLine.ToString();
        _currentLine.Clear();

        if (_bold) text = $"**{text}**";
        if (_sizeMultiplier > 1) text = $"[{_sizeMultiplier}x] {text}";
        if (_centered && text.Length < Width)
        {
            var padding = (Width - text.Length) / 2;
            text = new string(' ', padding) + text;
        }

        _previewLines.Add(text);
    }
}
