using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Application.Orders.Validators;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Orders;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    private static CreateOrderRequest ValidRequest() => new()
    {
        ShippingAddress = new ShippingAddressDto
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            Phone = "1234567890",
            Street = "1 Main St",
            City = "Doha",
            State = "DA",
            ZipCode = "00000",
            Country = "QA"
        },
        PaymentInfo = new CreateOrderPaymentDto
        {
            CardholderName = "Jane Doe",
            CardNumber = "****1111"
        },
        Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };

    [Fact]
    public async Task MaskedCardNumber_IsAccepted()
    {
        var request = ValidRequest();
        request.PaymentInfo.CardNumber = "****1111";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExactlyFourDigitCardNumber_IsAccepted()
    {
        var request = ValidRequest();
        request.PaymentInfo.CardNumber = "1111";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task FullSixteenDigitPan_IsRejected()
    {
        var request = ValidRequest();
        request.PaymentInfo.CardNumber = "4111111111111111";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Full card numbers must not be sent; provide the masked form.");
    }

    [Fact]
    public async Task EmptyItems_IsRejected()
    {
        var request = ValidRequest();
        request.Items = [];

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ItemQuantityLessThanOne_IsRejected()
    {
        var request = ValidRequest();
        request.Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 0 }];

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task BadEmail_IsRejected()
    {
        var request = ValidRequest();
        request.ShippingAddress.Email = "not-an-email";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyShippingField_IsRejected()
    {
        var request = ValidRequest();
        request.ShippingAddress.Street = "";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task CountryNotTwoLetters_IsRejected()
    {
        var request = ValidRequest();
        request.ShippingAddress.Country = "QAT";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyCardholderName_IsRejected()
    {
        var request = ValidRequest();
        request.PaymentInfo.CardholderName = "";

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }
}
