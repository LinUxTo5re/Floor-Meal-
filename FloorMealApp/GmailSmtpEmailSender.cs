using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace ClientLedgerApp;

public class GmailSmtpEmailSender : IEmailSender
{
    public async Task SendOtpAsync(string toEmail, string otp)
    {
        var subject = "Your verification code";
        var plainText = $"Your verification code is {otp}. It expires in 10 minutes.";

        // Use an interpolated verbatim string (note doubled quotes inside)
        var html = $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>{subject}</title>
  <style>
    body {{ background:#f6f7fb; margin:0; padding:0; font-family:-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Helvetica Neue', Arial, sans-serif; color:#1f2937; }}
    .container {{ max-width:560px; margin:24px auto; background:#ffffff; border-radius:12px; box-shadow:0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.1); overflow:hidden; }}
    .header {{ background:#4f46e5; color:#ffffff; padding:18px 24px; font-weight:600; font-size:18px; }}
    .content {{ padding:24px; }}
    .greeting {{ margin:0 0 12px; font-size:18px; font-weight:600; color:#111827; }}
    .lead {{ margin:0 0 16px; color:#374151; }}
    .code-box {{ margin:18px 0; padding:16px; text-align:center; border:1px dashed #94a3b8; border-radius:10px; background:#f8fafc; }}
    .code {{ display:inline-block; letter-spacing:6px; font-size:28px; font-weight:700; font-family: 'SFMono-Regular', Menlo, Consolas, 'Liberation Mono', monospace; color:#111827; }}
    .muted {{ color:#6b7280; font-size:13px; line-height:1.35; }}
    .footer {{ padding:16px 24px; text-align:center; color:#9ca3af; font-size:12px; }}
    @media (prefers-color-scheme: dark) {{
      body {{ background:#0b1220; color:#e5e7eb; }}
      .container {{ background:#0f172a; box-shadow:0 1px 2px rgba(0,0,0,0.5); }}
      .header {{ background:#3730a3; }}
      .lead {{ color:#cbd5e1; }}
      .code-box {{ background:#0b1220; border-color:#334155; }}
      .code {{ color:#e5e7eb; }}
      .muted {{ color:#94a3b8; }}
      .footer {{ color:#64748b; }}
    }}
  </style>
</head>
<body>
  <div class=""container"">
    <div class=""header"">Floor Meal</div>
    <div class=""content"">
      <p class=""greeting"">Verify your email</p>
      <p class=""lead"">Use the following verification code to sign in to your account:</p>
      <div class=""code-box"">
        <span class=""code"">{otp}</span>
      </div>
      <p class=""muted"">This code will expire in 10 minutes. If you did not request this, you can safely ignore this email.</p>
    </div>
    <div class=""footer"">© {DateTime.Now:yyyy} Floor Meal. All rights reserved.</div>
  </div>
</body>
</html>";

        try
        {
            var aws = ServiceHelper.GetService<IAwsSyncService>();
            await aws.EnsureGlobalCredentialsAsync();
        }
        catch { }

        var db = ServiceHelper.Services.GetService(typeof(IDatabaseService)) as IDatabaseService
                 ?? throw new InvalidOperationException("Database service unavailable");
        await db.InitializeAsync();

        // Load global SMTP from local credentials with Email = "GLOBAL"
        var globalCred = await db.GetCredentialsByEmailAsync("GLOBAL");
        if (globalCred == null || string.IsNullOrWhiteSpace(globalCred.SmtpUser) || string.IsNullOrWhiteSpace(globalCred.SmtpAppPassword))
            throw new InvalidOperationException("Missing SMTP credentials. Please configure global credentials in AWS.");

        // Optional: get display name from settings if present
        var settingsJson = await db.GetSettingAsync("app.settings.json") ?? string.Empty;
        var settings = string.IsNullOrWhiteSpace(settingsJson)
            ? new SettingsData() : (System.Text.Json.JsonSerializer.Deserialize<SettingsData>(settingsJson) ?? new SettingsData());
        var fromAddress = new MailAddress(globalCred.SmtpUser.Trim(), string.IsNullOrWhiteSpace(settings.FloorMealName) ? "Floor Meal" : settings.FloorMealName.Trim());

        using var message = new MailMessage();
        message.From = fromAddress;
        message.To.Add(toEmail);
        message.Subject = subject;
        message.BodyEncoding = Encoding.UTF8;
        message.SubjectEncoding = Encoding.UTF8;
        message.HeadersEncoding = Encoding.UTF8;
        // Provide both Plain Text and HTML bodies
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainText, Encoding.UTF8, "text/plain"));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
        // Set HTML as primary
        message.Body = html;
        message.IsBodyHtml = true;

        using var smtp = new SmtpClient("smtp.gmail.com", 587)
        {
            Credentials = new NetworkCredential(globalCred.SmtpUser.Trim(), globalCred.SmtpAppPassword.Trim()),
            EnableSsl = true
        };

        await smtp.SendMailAsync(message);
    }
}
