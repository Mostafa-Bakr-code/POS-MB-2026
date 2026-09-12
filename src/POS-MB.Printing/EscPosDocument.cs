using System.Text;
using BidiReshapeSharp;

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

    // .NET no longer ships legacy codepages built in - this package +
    // registration call makes Encoding.GetEncoding(...) work for them.
    // Registering a provider twice throws, so this only ever runs once per
    // process.
    private static readonly Encoding Pc437 = GetLegacyEncoding(437);

    // Different ESC/POS clones implement different Arabic tables under the
    // same nominal Epson table indices, so the exact (dotnet encoding,
    // ESC/POS table index) pair is chosen per-document from
    // PrinterSettings.ArabicVariant - a different candidate can be tried
    // from the Settings screen with just a Test Print, no rebuild needed.
    // Pc720 is the default: verified live (see arabictest scratch project)
    // that .NET's own codepage-720 table round-trips shaped Arabic text with
    // zero unmapped characters, unlike codepage 864 (which drops several
    // even after shaping - its .NET table is incomplete for this text).
    private static readonly Dictionary<ArabicCodePage, (Encoding Encoding, int TableIndex)> ArabicVariants = new()
    {
        [ArabicCodePage.Pc720] = (GetLegacyEncoding(720), 32),
        [ArabicCodePage.Wpc1256] = (GetLegacyEncoding(1256), 50),
        [ArabicCodePage.Pc864] = (GetLegacyEncoding(864), 37)
    };

    // Tracks which codepage the printer was last told to use, so consecutive
    // calls in the same script (or same language) don't re-emit the
    // codepage-switch command for every single line.
    private int? _activeCodePage;
    private readonly Encoding _arabicEncoding;
    private readonly int _arabicTableIndex;

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

    public EscPosDocument(ArabicCodePage arabicCodePage = ArabicCodePage.Pc720)
    {
        (_arabicEncoding, _arabicTableIndex) = ArabicVariants[arabicCodePage];

        // ESC @ - reset the printer to its default state, so leftover formatting
        // from a previous receipt can never bleed into this one.
        _bytes.AddRange([0x1B, 0x40]);
    }

    public EscPosDocument Text(string text)
    {
        // PC437 (the classic default codepage nearly every ESC/POS printer
        // starts up in) has no Arabic glyphs at all - anything outside plain
        // ASCII switches the printer to the configured Arabic table instead.
        // This is a per-call check rather than a whole-document setting
        // since a single receipt can freely mix English (item names,
        // prices) with an Arabic customer comment.
        var needsArabic = RequiresArabicCodePage(text);
        SelectCodePage(needsArabic ? _arabicTableIndex : 0);

        // This printer's Arabic table has no built-in shaping or right-to-left
        // support at all - it just prints whatever byte it's given, left to
        // right, one fixed glyph per byte. Arabic typed into the app is
        // stored in logical (typing) order and uses generic, unshaped letter
        // forms - printed as-is, that's exactly what "garbled Arabic" looks
        // like: correct letters, wrong order, wrong (unjoined) shapes.
        // BidiReshape.ProcessString does what a proper Arabic-aware renderer
        // would normally do at display time - determines each letter's
        // correct contextual form (isolated/initial/medial/final) and
        // reorders right-to-left runs into the correct visual sequence -
        // producing text a "dumb" byte-per-glyph device can print correctly
        // simply by dumping it in the order given. Left untouched for
        // ASCII-only text, which needs neither.
        var textToEncode = needsArabic ? BidiReshape.ProcessString(text) : text;

        var encoding = needsArabic ? _arabicEncoding : Pc437;
        _bytes.AddRange(encoding.GetBytes(textToEncode));
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
    // PC437; the Arabic table index depends on which variant this document
    // was constructed with (see ArabicVariants above).
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
