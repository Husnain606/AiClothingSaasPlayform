namespace FashionSaaS.Application.Interfaces;

public interface ITotpService
{
    (string SecretBase32, string QrCodeUrl) GenerateSetup(string email, string issuer);
    bool Verify(string secretBase32, string code);
    IReadOnlyList<string> GenerateBackupCodes();
}
