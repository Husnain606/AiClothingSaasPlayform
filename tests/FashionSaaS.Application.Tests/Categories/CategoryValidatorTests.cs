using FashionSaaS.Application.Categories.DTOs;
using FashionSaaS.Application.Categories.Validators;
using FluentAssertions;
using FluentValidation.Results;

namespace FashionSaaS.Application.Tests.Categories;

public class CategoryValidatorTests
{
    private readonly CreateCategoryRequestValidator _create = new();
    private readonly MoveCategoryRequestValidator _move = new();

    [Fact]
    public void Create_BlankName_Fails()
    {
        ValidationResult result = _create.Validate(new CreateCategoryRequest { Name = "", Slug = "shoes" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateCategoryRequest.Name));
    }

    [Theory]
    [InlineData("Shoes")]       // uppercase
    [InlineData("mens shoes")]  // space
    [InlineData("-shoes")]      // leading hyphen
    [InlineData("shoes-")]      // trailing hyphen
    [InlineData("mens--shoes")] // double hyphen
    public void Create_BadSlug_Fails(string slug)
    {
        ValidationResult result = _create.Validate(new CreateCategoryRequest { Name = "Shoes", Slug = slug });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateCategoryRequest.Slug));
    }

    [Theory]
    [InlineData("shoes")]
    [InlineData("mens-shoes")]
    [InlineData("a1-b2-c3")]
    public void Create_GoodSlug_Passes(string slug)
    {
        ValidationResult result = _create.Validate(new CreateCategoryRequest { Name = "Shoes", Slug = slug });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_NegativeSortOrder_Fails()
    {
        ValidationResult result = _create.Validate(new CreateCategoryRequest { Name = "S", Slug = "s", SortOrder = -1 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Move_NewParentEqualsId_Fails()
    {
        var id = Guid.NewGuid();
        ValidationResult result = _move.Validate(new MoveCategoryRequest { Id = id, NewParentId = id });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Move_NullNewParent_Passes()
    {
        ValidationResult result = _move.Validate(new MoveCategoryRequest { Id = Guid.NewGuid(), NewParentId = null });
        result.IsValid.Should().BeTrue();
    }
}
