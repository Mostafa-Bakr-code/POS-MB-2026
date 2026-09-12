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
    Pc864,   // Arabic (DOS) - Epson table index 37
    Wpc1256, // Arabic (Windows) - Epson table index 50
    Pc720    // Arabic (DOS, alternate) - Epson table index 32
}
