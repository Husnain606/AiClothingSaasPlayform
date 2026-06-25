namespace FashionSaaS.Application.Configuration;

public class SmtpSettings
{
    public const string SectionName = "SmtpSettings";

    public string From { get; init; } = string.Empty;
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
