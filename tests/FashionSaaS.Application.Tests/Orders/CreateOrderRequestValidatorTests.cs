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
        Items = [new CreateOrderItemRequest { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };

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

}
