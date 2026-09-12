namespace POS_MB.Printing;

// Which Arabic character table the printer switches to for non-ASCII text.
// Made user-selectable (Settings screen) rather than a single hardcoded
// choice - found live: different ESC/POS clones implement different Arabic
// tables under the same nominal names, so the only reliable way to know
// which one a given printer actually supports is to try each one with a
// real Test Print. Pc864 is the default since it's the standard used in
// Egypt specifically.
public enum ArabicCodePage
{
    // Default and recommended - verified live that .NET's own codepage-720
    // table round-trips properly shaped Arabic text (see EscPosDocument's
    // BidiReshape usage) with zero unmapped characters.
    Pc720,   // Arabic (DOS, alternate) - Epson table index 32
    Wpc1256, // Arabic (Windows) - Epson table index 50
    Pc864    // Arabic (DOS) - Epson table index 37; .NET's table for this
             // one is incomplete and drops several shaped characters
}
