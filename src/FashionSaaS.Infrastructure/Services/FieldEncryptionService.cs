using System.Security.Cryptography;
using System.Text;
using FashionSaaS.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FashionSaaS.Infrastructure.Services;

public class FieldEncryptionService : IFieldEncryptionService
{
    private readonly byte[] _key;

    public FieldEncryptionService(IConfiguration configuration)
    {
        var keyBase64 = configuration["EncryptionSettings:BankFieldKey"]
            ?? throw new InvalidOperationException("EncryptionSettings:BankFieldKey environment variable not set.");
        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException("BankFieldKey must be exactly 32 bytes (256-bit AES key).");
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack: nonce(12) + tag(16) + ciphertext
        var packed = new byte[28 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, 12);
        Buffer.BlockCopy(tag, 0, packed, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, packed, 28, ciphertext.Length);

        return Convert.ToBase64String(packed);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;

        var packed = Convert.FromBase64String(ciphertext);
        var nonce = packed[..12];
        var tag = packed[12..28];
        var encrypted = packed[28..];

        var plaintext = new byte[encrypted.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, encrypted, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    public string MaskAccountNumber(string plainAccountNumber)
    {
        if (string.IsNullOrEmpty(plainAccountNumber) || plainAccountNumber.Length <= 4)
            return $"****{plainAccountNumber}";
        return $"****{plainAccountNumber[^4..]}";
    }
}
