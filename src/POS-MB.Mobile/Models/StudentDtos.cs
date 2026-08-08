namespace POS_MB.Mobile.Models;

public record StudentDto(int StudentId, string Email, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public record StudentLoginResponse(string Token, string RefreshToken, StudentDto Student);
