namespace FashionSaaS.Application.Interfaces;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendCredentialsAsync(string to, string email, string temporaryPassword);
    Task SendPasswordResetAsync(string to, string resetLink);
    Task SendSubscriptionAssignedAsync(string to, string planName, DateTime endDate, string platformBankDetails);
    Task SendPaymentReminderAsync(string to, decimal amount, DateTime dueDate);
    Task SendPaymentOverdueAsync(string to, decimal amount, DateTime dueDate);
    Task SendPaymentConfirmedAsync(string to, decimal amount);
    Task SendTenantSuspendedAsync(string to, string reason);
    Task SendBankAccountChangedAsync(string to);
    Task SendSecurityAlertAsync(string to, string ipAddress, DateTime occurredAt);
    Task SendAccountLockedAsync(string to);
}
