using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.Business.Email;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Forgot-password (email a code, redeem it for a new password) and
// change-password (already logged in, knows the current one) - see
// clsStudentBusiness.RequestPasswordResetAsync/ResetPasswordAsync/ChangePasswordAsync.
// Never touches real SMTP - a fake IEmailSender captures what would have
// been sent instead, same reasoning as every fake PaymobClient in this
// project (a test typo must never risk a real side effect).
public class PasswordResetTests : DatabaseTestBase
{
    private class FakeEmailSender : IEmailSender
    {
        public int CallCount { get; private set; }
        public string? LastToEmail { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastBody { get; private set; }

        public Task SendAsync(string toEmail, string subject, string body)
        {
            CallCount++;
            LastToEmail = toEmail;
            LastSubject = subject;
            LastBody = body;
            return Task.CompletedTask;
        }

        // The email body is free text meant for a human ("Your password
        // reset code is: 123456") - pulling the code back out via the same
        // 6-digit pattern a student would read by eye, rather than the test
        // relying on any internal formatting detail beyond "6 digits appear
        // somewhere in the body".
        public string ExtractCode() =>
            System.Text.RegularExpressions.Regex.Match(LastBody ?? "", @"\d{6}").Value;
    }

    private clsStudentBusiness CreateStudentBusinessWith(FakeEmailSender emailSender) =>
        new(new clsStudentDataAccess(ConnectionFactory), RefreshTokenBusiness, emailSender);

    [Fact]
    public async Task RequestPasswordReset_SendsAnEmailWithA6DigitCode()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        await CreateStudentAsync(email);
        var fake = new FakeEmailSender();
        var studentBusiness = CreateStudentBusinessWith(fake);

        await studentBusiness.RequestPasswordResetAsync(email);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(email, fake.LastToEmail);
        Assert.Matches(@"\d{6}", fake.ExtractCode());
    }

    [Fact]
    public async Task RequestPasswordReset_IsSilent_ForAnUnknownEmail()
    {
        // Must never reveal whether an email is registered - same
        // reasoning as VerifyCredentialsAsync's identical failure shape
        // for "wrong password" and "no such account".
        var fake = new FakeEmailSender();
        var studentBusiness = CreateStudentBusinessWith(fake);

        await studentBusiness.RequestPasswordResetAsync("nobody-here@example.com");

        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task ResetPassword_SucceedsWithTheEmailedCode_AndAllowsLoginWithTheNewPassword()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        await CreateStudentAsync(email);
        var fake = new FakeEmailSender();
        var studentBusiness = CreateStudentBusinessWith(fake);
        await studentBusiness.RequestPasswordResetAsync(email);
        var code = fake.ExtractCode();

        await studentBusiness.ResetPasswordAsync(email, code, "NewPassword123!");

        var verified = await StudentBusiness.VerifyCredentialsAsync(email, "NewPassword123!");
        Assert.NotNull(verified);
    }

    [Fact]
    public async Task ResetPassword_RevokesExistingSessions()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        var refreshToken = await RefreshTokenBusiness.IssueAsync(studentId, AccountType.Student);
        var fake = new FakeEmailSender();
        var studentBusiness = CreateStudentBusinessWith(fake);
        await studentBusiness.RequestPasswordResetAsync(email);
        var code = fake.ExtractCode();

        await studentBusiness.ResetPasswordAsync(email, code, "NewPassword123!");

        // A refresh token issued before the reset must no longer work - a
        // stolen or already-compromised session shouldn't survive the
        // student locking it out via a password reset.
        var result = await RefreshTokenBusiness.ValidateAndRotateAsync(refreshToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResetPassword_Throws_ForAWrongCode()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        await CreateStudentAsync(email);
        var fake = new FakeEmailSender();
        var studentBusiness = CreateStudentBusinessWith(fake);
        await studentBusiness.RequestPasswordResetAsync(email);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            studentBusiness.ResetPasswordAsync(email, "000000", "NewPassword123!"));
    }

    [Fact]
    public async Task ResetPassword_Throws_WhenNoResetWasEverRequested()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        await CreateStudentAsync(email);
        var studentBusiness = CreateStudentBusinessWith(new FakeEmailSender());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            studentBusiness.ResetPasswordAsync(email, "123456", "NewPassword123!"));
    }

    [Fact]
    public async Task ChangePassword_SucceedsWithTheCorrectCurrentPassword()
    {
        var email = $"change-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email); // signed up with "password123", see CreateStudentAsync

        await StudentBusiness.ChangePasswordAsync(studentId, "password123", "NewPassword123!");

        var verified = await StudentBusiness.VerifyCredentialsAsync(email, "NewPassword123!");
        Assert.NotNull(verified);
    }

    [Fact]
    public async Task ChangePassword_Throws_ForTheWrongCurrentPassword()
    {
        var email = $"change-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            StudentBusiness.ChangePasswordAsync(studentId, "totally-wrong", "NewPassword123!"));

        // The password must be genuinely untouched by a failed attempt.
        var verified = await StudentBusiness.VerifyCredentialsAsync(email, "password123");
        Assert.NotNull(verified);
    }

    [Fact]
    public async Task ChangePassword_RevokesExistingSessions()
    {
        var email = $"change-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        var refreshToken = await RefreshTokenBusiness.IssueAsync(studentId, AccountType.Student);

        await StudentBusiness.ChangePasswordAsync(studentId, "password123", "NewPassword123!");

        var result = await RefreshTokenBusiness.ValidateAndRotateAsync(refreshToken);
        Assert.Null(result);
    }
}
