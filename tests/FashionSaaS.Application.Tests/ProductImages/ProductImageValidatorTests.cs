using FashionSaaS.Application.ProductImages.DTOs;
using FashionSaaS.Application.ProductImages.Validators;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.ProductImages;

public class ProductImageValidatorTests
{
    private readonly UploadImageRequestValidator _upload = new();
    private readonly ReorderImagesRequestValidator _reorder = new();

    [Fact]
    public void Upload_Valid_Passes()
        => _upload.Validate(new UploadImageRequest { ProductId = Guid.NewGuid(), AltText = "alt" })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Upload_EmptyProductId_Fails()
    {
        var result = _upload.Validate(new UploadImageRequest { ProductId = Guid.Empty });
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UploadImageRequest.ProductId));
    }

    [Fact]
    public void Upload_NullAltText_Passes()
        => _upload.Validate(new UploadImageRequest { ProductId = Guid.NewGuid(), AltText = null })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Upload_AltTextOver500_Fails()
    {
        var result = _upload.Validate(new UploadImageRequest
        {
            ProductId = Guid.NewGuid(),
            AltText = new string('a', 501)
        });
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UploadImageRequest.AltText));
    }

    [Fact]
    public void Reorder_NonEmptyIds_Passes()
        => _reorder.Validate(new ReorderImagesRequest { Ids = new[] { Guid.NewGuid() } })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Reorder_EmptyIds_Fails()
    {
        var result = _reorder.Validate(new ReorderImagesRequest { Ids = [] });
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReorderImagesRequest.Ids));
    }
}
