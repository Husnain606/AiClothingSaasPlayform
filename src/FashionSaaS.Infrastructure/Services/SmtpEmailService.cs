using FashionSaaS.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace FashionSaaS.Infrastructure.Services;

public class SmtpEmailService(IConfiguration configuration) : IEmailService
{
    private async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration["SmtpSettings:From"]
            ?? throw new InvalidOperationException("SmtpSettings:From not configured.")));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            configuration["SmtpSettings:Host"] ?? "smtp.gmail.com",
            int.Parse(configuration["SmtpSettings:Port"] ?? "587"),
            SecureSocketOptions.StartTls);

        var username = configuration["SmtpSettings:Username"]
            ?? throw new InvalidOperationException("SmtpSettings:Username not configured.");
        var password = configuration["SmtpSettings:Password"]
            ?? throw new InvalidOperationException("SmtpSettings:Password not configured.");

        await client.AuthenticateAsync(username, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public Task SendAsync(string to, string subject, string htmlBody)
        => SendEmailAsync(to, subject, htmlBody);

    public Task SendCredentialsAsync(string to, string email, string temporaryPassword)
        => SendEmailAsync(to, "Your FashionSaaS Account",
            $"<h2>Welcome!</h2><p>Email: {email}</p><p>Temporary Password: {temporaryPassword}</p><p>Please change your password on first login.</p>");

    public Task SendPasswordResetAsync(string to, string resetLink)
        => SendEmailAsync(to, "Password Reset Request",
            $"<h2>Reset Your Password</h2><p>Click the link below to reset your password (expires in 1 hour):</p><p><a href='{resetLink}'>{resetLink}</a></p>");

    public Task SendSubscriptionAssignedAsync(string to, string planName, DateTime endDate, string platformBankDetails)
        => SendEmailAsync(to, "Subscription Activated",
            $"<h2>Subscription Activated</h2><p>Plan: {planName}</p><p>Expires: {endDate:yyyy-MM-dd}</p><p>Bank Details:<br/>{platformBankDetails}</p>");

    public Task SendPaymentReminderAsync(string to, decimal amount, DateTime dueDate)
        => SendEmailAsync(to, "Payment Reminder",
            $"<h2>Payment Due</h2><p>Amount: PKR {amount:N2}</p><p>Due Date: {dueDate:yyyy-MM-dd}</p>");

    public Task SendPaymentOverdueAsync(string to, decimal amount, DateTime dueDate)
        => SendEmailAsync(to, "Payment Overdue",
            $"<h2>Payment Overdue</h2><p>Amount: PKR {amount:N2} was due on {dueDate:yyyy-MM-dd}. Please pay to avoid suspension.</p>");

    public Task SendPaymentConfirmedAsync(string to, decimal amount)
        => SendEmailAsync(to, "Payment Confirmed",
            $"<h2>Payment Confirmed</h2><p>PKR {amount:N2} received. Your store is active.</p>");

    public Task SendTenantSuspendedAsync(string to, string reason)
        => SendEmailAsync(to, "Store Suspended",
            $"<h2>Your Store Has Been Suspended</h2><p>Reason: {reason}</p>");

    public Task SendBankAccountChangedAsync(string to)
        => SendEmailAsync(to, "Bank Account Updated",
            "<h2>Bank Account Changed</h2><p>Your bank account details were recently updated. Contact support if this was not you.</p>");

    public Task SendSecurityAlertAsync(string to, string ipAddress, DateTime occurredAt)
        => SendEmailAsync(to, "Security Alert: New Login IP",
            $"<h2>New Login Detected</h2><p>IP: {ipAddress}</p><p>Time: {occurredAt:yyyy-MM-dd HH:mm:ss} UTC</p><p>If this was not you, secure your account immediately.</p>");

    public Task SendAccountLockedAsync(string to)
        => SendEmailAsync(to, "Account Locked",
            "<h2>Account Locked</h2><p>Your account has been locked due to multiple failed login attempts. Contact your administrator.</p>");
}
