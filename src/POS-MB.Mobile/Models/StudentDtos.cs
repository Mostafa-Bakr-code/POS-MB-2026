namespace POS_MB.Mobile.Models;

// SavedCardMaskedPan/SavedCardSubtype are null when the student has never
// saved a card - non-null is the signal CartPage uses to offer "pay with
// saved card" at all, so the app never has to guess or ask the server
// separately.
public record StudentDto(int StudentId, string Email, bool IsActive, string? SavedCardMaskedPan, string? SavedCardSubtype, DateTime CreatedAt, DateTime UpdatedAt);
public record StudentLoginResponse(string Token, string RefreshToken, StudentDto Student);
