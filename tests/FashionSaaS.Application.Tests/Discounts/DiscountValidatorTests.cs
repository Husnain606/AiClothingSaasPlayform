using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Application.Discounts.Validators;
using FashionSaaS.Domain.Enums;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Discounts;

public class DiscountValidatorTests
{
    private readonly CreateDiscountRequestValidator _create = new();

    private static CreateDiscountRequest Valid() => new()
    {
        Code = "SAVE10", Type = DiscountType.Percentage, Value = 10,
        StartsAt = new DateTime(2026, 1, 1), EndsAt = new DateTime(2026, 2, 1)
    };

    [Fact]
    public void Valid_Passes() => _create.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void BlankCode_Fails()
    {
        var req = Valid(); req.Code = "";
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.Code));
    }

    [Fact]
    public void ValueZero_Fails()
    {
        var req = Valid(); req.Value = 0;
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.Value));
    }

    [Fact]
    public void PercentageOver100_Fails()
    {
        var req = Valid(); req.Type = DiscountType.Percentage; req.Value = 150;
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.Value));
    }

    [Fact]
    public void FixedAmountOver100_Passes()
    {
        // The ≤100 cap is percentage-only; a fixed-amount discount may exceed 100.
        var req = Valid(); req.Type = DiscountType.FixedAmount; req.Value = 150;
        _create.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void StartsAtAfterEndsAt_Fails()
    {
        var req = Valid();
        req.StartsAt = new DateTime(2026, 3, 1);
        req.EndsAt = new DateTime(2026, 2, 1);
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.StartsAt));
    }

    [Fact]
    public void NegativeMinOrderAmount_Fails()
    {
        var req = Valid(); req.MinOrderAmount = -1;
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.MinOrderAmount));
    }

    [Fact]
    public void MaxRedemptionsZero_Fails()
    {
        var req = Valid(); req.MaxRedemptions = 0;
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateDiscountRequest.MaxRedemptions));
    }
}
