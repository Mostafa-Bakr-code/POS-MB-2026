namespace POS_MB.Cashier.Models;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Permissions { get; set; }
    public bool IsActive { get; set; }
}

public record VerifyCredentialsRequest(string UserName, string Password);
public record LoginResponse(string Token, string RefreshToken, UserDto User);
