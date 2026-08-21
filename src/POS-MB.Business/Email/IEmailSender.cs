namespace POS_MB.Business.Email;

// Interface (not just a concrete class, unlike PaymobClient's virtual-method
// approach) specifically so tests never need real SMTP credentials or
// network access to exercise password-reset logic - a fake implementation
// is a one-line class, no HttpClient/SmtpClient machinery to fake around.
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body);
}
