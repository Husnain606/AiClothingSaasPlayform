using System.Text;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.Infrastructure.Tests.Services;

public sealed class LocalFilePaymentProofStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "proof-tests-" + Guid.NewGuid().ToString("N"));

    private LocalFilePaymentProofStorageService CreateService()
        => new(
            Options.Create(new PaymentProofStorageSettings { RootPath = _root, MaxFileSizeBytes = 10485760 }),
            NullLogger<LocalFilePaymentProofStorageService>.Instance);

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsBytesUnchanged()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.pdf";

        await service.SaveAsync(Content("proof-bytes"), key);

        await using Stream read = await service.OpenReadAsync(key);
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync()).Should().Be("proof-bytes");
    }

    [Fact]
    public async Task SaveAsync_CreatesNestedDirectories()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.png";

        await service.SaveAsync(Content("x"), key);

        File.Exists(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
    }

    [Theory]
    [InlineData("../escaped.pdf")]
    [InlineData("a/../../escaped.pdf")]
    [InlineData("/absolute/escaped.pdf")]
    public async Task SaveAsync_KeyEscapingRoot_Throws(string key)
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.SaveAsync(Content("x"), key);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("../escaped.pdf")]
    [InlineData("a/../../escaped.pdf")]
    public async Task OpenReadAsync_KeyEscapingRoot_Throws(string key)
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.OpenReadAsync(key);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ThrowsFileNotFound()
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.OpenReadAsync($"{Guid.NewGuid()}/{Guid.NewGuid()}/missing.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var key = $"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid():N}.jpg";
        await service.SaveAsync(Content("x"), key);

        await service.DeleteAsync(key);

        File.Exists(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_DoesNotThrow()
    {
        LocalFilePaymentProofStorageService service = CreateService();

        Func<Task> act = () => service.DeleteAsync($"{Guid.NewGuid()}/{Guid.NewGuid()}/missing.pdf");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_KeyEscapingRoot_DoesNotThrowAndDeletesNothing()
    {
        LocalFilePaymentProofStorageService service = CreateService();
        var outside = Path.Combine(Path.GetTempPath(), "must-survive-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "keep me");

        try
        {
            Func<Task> act = () => service.DeleteAsync("../" + Path.GetFileName(outside));

            // Delete is best-effort and must never throw, but must also never delete outside the root.
            await act.Should().NotThrowAsync();
            File.Exists(outside).Should().BeTrue();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
