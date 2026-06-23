using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace FashionSaaS.Infrastructure.Tests.Security;

public class FieldEncryptionServiceTests
{
    private readonly FieldEncryptionService _service;

    public FieldEncryptionServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionSettings:BankFieldKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();
        _service = new FieldEncryptionService(config);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginal()
    {
        const string plain = "PK36ALFH0110079123456789";
        _service.Decrypt(_service.Encrypt(plain)).Should().Be(plain);
    }

    [Fact]
    public void Encrypt_SameValue_ProducesDifferentCiphertext()
    {
        const string plain = "PK36ALFH0110079123456789";
        _service.Encrypt(plain).Should().NotBe(_service.Encrypt(plain));
    }

    [Theory]
    [InlineData("PK36ALFH0110079123456789", "****6789")]
    [InlineData("1234", "****1234")]
    public void MaskAccountNumber_ReturnsMasked(string number, string expected)
        => _service.MaskAccountNumber(number).Should().Be(expected);
}
