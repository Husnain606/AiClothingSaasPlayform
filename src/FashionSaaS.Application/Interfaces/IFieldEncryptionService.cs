namespace FashionSaaS.Application.Interfaces;

public interface IFieldEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string MaskAccountNumber(string plainAccountNumber);
}
