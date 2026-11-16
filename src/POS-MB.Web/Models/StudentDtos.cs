namespace POS_MB.Web.Models;

// Mirrors POS-MB.Mobile's Models/StudentDtos.cs exactly - same API contract,
// duplicated rather than shared since Web and Mobile are separate projects
// with no shared reference (same convention as every other client-side DTO
// mirror in this codebase).
public record StudentDto(int StudentId, string Email, bool IsActive, string? SavedCardMaskedPan, string? SavedCardSubtype, DateTime CreatedAt, DateTime UpdatedAt);
public record StudentLoginResponse(string Token, string RefreshToken, StudentDto Student);
