namespace POS_MB.DataAccess.Models;

public class Student
{
    public int StudentId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // One saved card at a time, not a multi-card wallet. Token is the
    // opaque Paymob token needed to charge the card again - see
    // clsOrderBusiness.StartPaymobCheckoutAsync. MaskedPan/Subtype are
    // display-only, never a real card number.
    public string? SavedCardToken { get; set; }
    public string? SavedCardMaskedPan { get; set; }
    public string? SavedCardSubtype { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
