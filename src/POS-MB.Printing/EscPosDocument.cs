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

    // .NET no longer ships legacy codepages (like 437/1256) built in - this
    // package + registration call makes Encoding.GetEncoding(...) work for
    // them. Registering a provider twice throws, so this only ever runs once
    // per process.
    private static readonly Encoding Pc437 = GetLegacyEncoding(437);

    // Windows-1256 (Arabic) - found live: a comment containing Arabic text
    // printed as a row of "?" characters, since PC437 has no Arabic glyphs at
    // all and .NET's default encoder fallback silently replaces anything it
    // can't represent. Switched to per-call instead of a single fixed
    // encoding so an English-only receipt (the common case) keeps using the
    // printer's normal startup codepage unchanged.
    private static readonly Encoding Windows1256 = GetLegacyEncoding(1256);

    // Tracks which codepage the printer was last told to use, so consecutive
    // calls in the same script (or same language) don't re-emit the
    // codepage-switch command for every single line.
    private int? _activeCodePage;

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
        // PC437 (the classic default codepage nearly every ESC/POS printer
        // starts up in) has no Arabic glyphs at all - anything outside plain
        // ASCII switches the printer to WPC1256 (Arabic) instead. This is a
        // per-call check rather than a whole-document setting since a single
        // receipt can freely mix English (item names, prices) with an
        // Arabic customer comment.
        var needsArabic = RequiresArabicCodePage(text);
        SelectCodePage(needsArabic ? 50 : 0);

        var encoding = needsArabic ? Windows1256 : Pc437;
        _bytes.AddRange(encoding.GetBytes(text));
        _currentLine.Append(text);
        return this;
    }

    private static bool RequiresArabicCodePage(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F) return true;
        }
        return false;
    }

    // ESC t n - selects the printer's active character code table. 0 is
    // PC437, 50 is WPC1256 (Arabic) - both standard Epson-compatible table
    // indices that Xprinter (and virtually every ESC/POS clone) follows.
    private void SelectCodePage(int codePage)
    {
        if (_activeCodePage == codePage) return;

        _bytes.AddRange([0x1B, 0x74, (byte)codePage]);
        _activeCodePage = codePage;
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
