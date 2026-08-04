namespace POS_MB.Printing;

// How (if at all) VAT shows up on the customer receipt. Comments and the kitchen
// ticket are unaffected either way - this only ever changes the customer copy.
public enum TaxDisplayMode
{
    None,       // just item totals and a grand Total - no VAT mentioned anywhere
    TotalOnly,  // item lines show plain totals; VAT only appears once, in the order summary
    PerItem     // every item line shows its own Price/VAT/Total breakdown, plus the order summary
}
