using FashionSaaS.Application.Orders.DTOs;
using FashionSaaS.Application.Orders.Validators;
using FluentAssertions;
using FluentValidation.Results;

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
        CreateOrderRequest request = ValidRequest();
        request.PaymentInfo.CardNumber = "****1111";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExactlyFourDigitCardNumber_IsAccepted()
    {
        CreateOrderRequest request = ValidRequest();
        request.PaymentInfo.CardNumber = "1111";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task FullSixteenDigitPan_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.PaymentInfo.CardNumber = "4111111111111111";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Full card numbers must not be sent; provide the masked form.");
    }

    [Fact]
    public async Task EmptyItems_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.Items = [];

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ItemQuantityLessThanOne_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 0 }];

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task BadEmail_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.ShippingAddress.Email = "not-an-email";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyShippingField_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.ShippingAddress.Street = "";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task CountryNotTwoLetters_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.ShippingAddress.Country = "QAT";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyCardholderName_IsRejected()
    {
        CreateOrderRequest request = ValidRequest();
        request.PaymentInfo.CardholderName = "";

        ValidationResult result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }
}
