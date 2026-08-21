namespace POS_MB.Business.Email;

// Bound from the "Email" configuration section (user secrets locally, a
// real secrets manager once this moves to the cloud) - same reasoning as
// PaymobOptions, these are infrastructure credentials, not app behavior.
// Currently a Gmail account's own SMTP (smtp.gmail.com:587, STARTTLS) with
// an App Password - not the account's real password, a separate,
// independently-revocable credential Google issues per-app.
public class EmailOptions
{
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "POS-MB";
}
