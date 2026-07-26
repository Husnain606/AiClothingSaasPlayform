using System.Text;
using FashionSaaS.Application.Orders;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Orders;

public class PaymentProofContentTypesTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7");

    private static byte[] WebpHeader()
    {
        var header = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(header, 8);
        return header;
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    [InlineData("IMAGE/JPEG")]
    public void IsAllowed_AllowlistedType_ReturnsTrue(string contentType)
        => PaymentProofContentTypes.IsAllowed(contentType).Should().BeTrue();

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAllowed_NonAllowlistedType_ReturnsFalse(string? contentType)
        => PaymentProofContentTypes.IsAllowed(contentType).Should().BeFalse();

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    public void ExtensionFor_AllowlistedType_ReturnsExpectedExtension(string contentType, string expected)
        => PaymentProofContentTypes.ExtensionFor(contentType).Should().Be(expected);

    [Fact]
    public void ExtensionFor_NonAllowlistedType_Throws()
    {
        Action act = () => PaymentProofContentTypes.ExtensionFor("text/html");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HeaderMatches_JpegHeaderWithJpegType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(JpegHeader, "image/jpeg").Should().BeTrue();

    [Fact]
    public void HeaderMatches_PngHeaderWithPngType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(PngHeader, "image/png").Should().BeTrue();

    [Fact]
    public void HeaderMatches_WebpHeaderWithWebpType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(WebpHeader(), "image/webp").Should().BeTrue();

    [Fact]
    public void HeaderMatches_PdfHeaderWithPdfType_ReturnsTrue()
        => PaymentProofContentTypes.HeaderMatches(PdfHeader, "application/pdf").Should().BeTrue();

    [Fact]
    public void HeaderMatches_ExecutableRenamedAsPdf_ReturnsFalse()
    {
        // "MZ" — a Windows PE executable claiming to be a PDF. This is the attack the check exists for.
        byte[] mzHeader = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];
        PaymentProofContentTypes.HeaderMatches(mzHeader, "application/pdf").Should().BeFalse();
    }

    [Fact]
    public void HeaderMatches_PngBytesClaimingJpeg_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches(PngHeader, "image/jpeg").Should().BeFalse();

    [Fact]
    public void HeaderMatches_RiffWithoutWebpMarker_ReturnsFalse()
    {
        var riffOnly = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(riffOnly, 0);
        Encoding.ASCII.GetBytes("AVI ").CopyTo(riffOnly, 8);
        PaymentProofContentTypes.HeaderMatches(riffOnly, "image/webp").Should().BeFalse();
    }

    [Fact]
    public void HeaderMatches_HeaderTooShort_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches([0xFF], "image/jpeg").Should().BeFalse();

    [Fact]
    public void HeaderMatches_EmptyHeader_ReturnsFalse()
        => PaymentProofContentTypes.HeaderMatches([], "application/pdf").Should().BeFalse();

    [Fact]
    public void MaxFileSizeBytes_IsTenMegabytes()
        => PaymentProofContentTypes.MaxFileSizeBytes.Should().Be(10485760);
}
