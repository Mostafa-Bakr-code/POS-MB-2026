namespace POS_MB.DataAccess.Models;

public class RefreshToken
{
    public int RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool RevokedViaLogout { get; set; }
    public DateTime CreatedAt { get; set; }
}
