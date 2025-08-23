using System.Threading.Tasks;

namespace ClientLedgerApp;

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string otp);
}
