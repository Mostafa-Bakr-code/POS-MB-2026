using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

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
    // Float, not int: found live that jumping straight from 1x to 2x felt
    // like too big a step for the kitchen ticket, so Size() now accepts
    // half-steps (1.5x etc.) for the raster (Arabic) rendering path, even
    // though the printer's own hardware text scaling (GS ! n) is still
    // integer-only and gets rounded/clamped separately in Size().
    private float _sizeMultiplier = 1;

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

        // Same reasoning as the old GDI+ path relying on Windows' own font
        // linking: rather than bundling one specific Arabic font and hoping
        // its glyph coverage is right, MatchCharacter finds whatever font
        // the OS itself already has that can draw this text's own first
        // non-ASCII character - Windows resolves this to Tahoma/Arial's
        // Arabic fallback, Android to its built-in Noto Sans Arabic -
        // without this code needing to know or bundle either one.
        using var typeface = ResolveTypeface(text, _bold);
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        // Skia itself is just a raster library with no complex-script
        // shaping built in - unlike GDI+, which got Arabic's contextual
        // letter forms and right-to-left reordering for free from Windows'
        // own text engine. SKShaper (HarfBuzz) is what actually does that
        // shaping/reordering here.
        using var shaper = new SKShaper(typeface);

        // Wrapping manually - measuring and drawing one already-complete
        // line at a time, each shaped as its own self-contained run - keeps
        // every line's Arabic shaping independent and correct, the same way
        // a normal multi-line RTL label wraps in a real UI (found live:
        // letting a whole multi-line string get wrapped/reshaped together
        // produced overlapping garbage instead of clean stacked lines).
        var lines = WrapToLines(text, font, RasterWidthDots);

        font.GetFontMetrics(out var metrics);
        var lineHeight = metrics.Descent - metrics.Ascent;
        var height = Math.Max(1, (int)Math.Ceiling(lineHeight * lines.Count));

        using var bitmap = new SKBitmap(new SKImageInfo(RasterWidthDots, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);

            for (var i = 0; i < lines.Count; i++)
            {
                var lineWidth = font.MeasureText(lines[i]);
                var x = _centered ? Math.Max(0, (RasterWidthDots - lineWidth) / 2) : 0;
                var baselineY = i * lineHeight - metrics.Ascent;
                canvas.DrawShapedText(shaper, lines[i], x, baselineY, font, paint);
            }
        }

        return BuildRasterCommand(bitmap);
    }

    // Finds whatever typeface the OS already has installed that can
    // actually draw this text's first non-ASCII character - see
    // RenderTextAsRaster. Falls back to the platform default font if
    // nothing more specific matches (shouldn't normally happen for Arabic
    // on either Windows or Android).
    private static SKTypeface ResolveTypeface(string text, bool bold)
    {
        var style = bold ? SKFontStyle.Bold : SKFontStyle.Normal;

        foreach (var c in text)
        {
            if (c > 0x7F)
                return SKFontManager.Default.MatchCharacter(null, style, null, c) ?? SKTypeface.Default;
        }

        return SKTypeface.Default;
    }

    // Greedily packs space-separated words onto as few lines as fit within
    // maxWidthPx, measuring each candidate line as a whole (unbounded, single
    // line) rather than relying on any built-in word-wrap - see
    // RenderTextAsRaster. Uses the font's own (unshaped) glyph advances for
    // this width check, not the shaper - shaping affects individual glyph
    // forms far more than total line width, so this stays a close enough
    // estimate for deciding where to break, without shaping every candidate
    // line just to measure it.
    private static List<string> WrapToLines(string text, SKFont font, int maxWidthPx)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var current = "";

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            var width = font.MeasureText(candidate);
            if (width > maxWidthPx && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0 || lines.Count == 0) lines.Add(current);

        return lines;
    }

    // GS v 0 m xL xH yL yH d1...dk - prints a monochrome bitmap directly,
    // one bit per pixel (1 = black), packed 8 pixels per byte, row by row.
    // This is the one ESC/POS command that doesn't depend on the printer's
    // built-in character set at all - it's just pixels.
    private static byte[] BuildRasterCommand(SKBitmap bitmap)
    {
        var widthBytes = (bitmap.Width + 7) / 8;
        var data = new byte[widthBytes * bitmap.Height];

        var pixels = bitmap.Bytes; // Rgba8888, 4 bytes/pixel, row-major
        var rowBytes = bitmap.RowBytes;

        for (var y = 0; y < bitmap.Height; y++)
        {
            var rowStart = y * rowBytes;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = rowStart + x * 4; // R, G, B, A
                var luminance = (pixels[offset] * 299 + pixels[offset + 1] * 587 + pixels[offset + 2] * 114) / 1000;
                if (luminance < 128)
                {
                    data[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }
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
        // Found live: at 2x+ height (GS ! n), a single line feed doesn't
        // fully clear the taller glyphs on this printer - the next line (or
        // a divider right after it) starts before the enlarged text has
        // finished, slicing off its bottom. This printer doesn't auto-adjust
        // its line pitch to the tallest character on the line the way real
        // Epson firmware does, so the extra height is fed manually here.
        // Rounded up, since even a 1.5x line needs the full extra line feed.
        var feedLines = (int)Math.Ceiling(_sizeMultiplier);
        for (var i = 0; i < feedLines; i++) _bytes.Add(0x0A);
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
    // as (multiplier - 1), so 1 = normal size, up to 8 = 8x. This is the
    // printer's own hardware text scaling and only understands whole steps,
    // so a fractional size (e.g. 1.5x, for finer control - see
    // _sizeMultiplier) is rounded to the nearest whole step for the plain-
    // ASCII text path; the exact fractional value still drives the Arabic
    // raster image's font size, which has no such hardware limitation.
    public EscPosDocument Size(float width, float height)
    {
        _sizeMultiplier = Math.Max(width, height);

        var hardwareWidth = Math.Clamp((int)Math.Round(width), 1, 8);
        var hardwareHeight = Math.Clamp((int)Math.Round(height), 1, 8);
        _bytes.AddRange([0x1D, 0x21, (byte)(((hardwareHeight - 1) << 4) | (hardwareWidth - 1))]);
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
        if (_sizeMultiplier > 1) text = $"[{_sizeMultiplier:0.#}x] {text}";
        if (_centered && text.Length < Width)
        {
            var padding = (Width - text.Length) / 2;
            text = new string(' ', padding) + text;
        }

        _previewLines.Add(text);
    }
}
