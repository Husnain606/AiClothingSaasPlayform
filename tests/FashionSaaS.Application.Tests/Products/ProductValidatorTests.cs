using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Application.Products.Validators;
using FluentAssertions;
using FluentValidation.Results;

namespace FashionSaaS.Application.Tests.Products;

public class ProductValidatorTests
{
    private readonly CreateProductRequestValidator _create = new();
    private readonly UpdateProductRequestValidator _update = new();

    private static CreateProductRequest Valid() => new()
    {
        Name = "Tee",
        Slug = "tee",
        CategoryId = Guid.NewGuid(),
        BasePrice = 10m
    };

    [Fact]
    public void Create_BlankName_Fails()
    {
        CreateProductRequest req = Valid();
        req.Name = "";
        ValidationResult result = _create.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductRequest.Name));
    }

    [Fact]
    public void Create_NegativePrice_Fails()
    {
        CreateProductRequest req = Valid();
        req.BasePrice = -1m;
        ValidationResult result = _create.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductRequest.BasePrice));
    }

    [Fact]
    public void Create_EmptyCategoryId_Fails()
    {
        CreateProductRequest req = Valid();
        req.CategoryId = Guid.Empty;
        ValidationResult result = _create.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductRequest.CategoryId));
    }

    [Theory]
    [InlineData("Tee")]        // uppercase
    [InlineData("blue tee")]   // space
    [InlineData("-tee")]       // leading hyphen
    [InlineData("tee-")]       // trailing hyphen
    [InlineData("blue--tee")]  // double hyphen
    public void Create_BadSlug_Fails(string slug)
    {
        CreateProductRequest req = Valid();
        req.Slug = slug;
        ValidationResult result = _create.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateProductRequest.Slug));
    }

    [Theory]
    [InlineData("tee")]
    [InlineData("blue-tee")]
    [InlineData("a1-b2-c3")]
    public void Create_GoodSlug_Passes(string slug)
    {
        CreateProductRequest req = Valid();
        req.Slug = slug;
        ValidationResult result = _create.Validate(req);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_BlankSlug_Fails()
    {
        ValidationResult result = _update.Validate(new UpdateProductRequest
        {
            Name = "Tee",
            Slug = "",
            CategoryId = Guid.NewGuid(),
            BasePrice = 1m
        });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProductRequest.Slug));
    }

    [Fact]
    public void Create_ValidRequest_Passes()
    {
        _create.Validate(Valid()).IsValid.Should().BeTrue();
    }
}
