namespace POS_MB.Printing;

// Whether (and how much of) the order's date/time shows up on the customer
// receipt. The kitchen ticket is unaffected either way - it always shows
// the full date and time regardless (the kitchen genuinely needs it),
// same reasoning as TaxDisplayMode only ever touching the customer copy.
public enum ReceiptDateTimeDisplayMode
{
    None,     // neither date nor time shown - the original "off" default
    TimeOnly, // just the time (e.g. "14:32")
    DateOnly, // just the date (e.g. "2026-10-11")
    Both      // date and time together (e.g. "2026-10-11 14:32")
}
