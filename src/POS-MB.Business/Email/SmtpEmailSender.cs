using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace POS_MB.Business.Email;

public class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        // StartTls (not SslOnConnect) - port 587 is Gmail's STARTTLS port,
        // the connection starts plaintext and upgrades to TLS as its first
        // action, unlike port 465's implicit-TLS-from-the-start.
        await client.ConnectAsync(options.SmtpHost, options.SmtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(options.SmtpUsername, options.SmtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
    }
}
