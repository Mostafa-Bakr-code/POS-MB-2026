namespace POS_MB.DataAccess.Models;

// Distinguishes which table a RefreshTokens.UserId actually refers to - that
// column has no FK constraint since it points at either Users or Students
// depending on this value (see schema.sql for why: refresh token rotation and
// theft-detection are identical mechanics regardless of account type).
public enum AccountType : byte
{
    Staff = 0,
    Student = 1
}
