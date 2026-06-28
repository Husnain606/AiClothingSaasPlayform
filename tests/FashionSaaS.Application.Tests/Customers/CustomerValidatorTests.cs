using FashionSaaS.Application.Customers.DTOs;
using FashionSaaS.Application.Customers.Validators;
using FluentAssertions;

namespace FashionSaaS.Application.Tests.Customers;

public class CustomerValidatorTests
{
    private readonly CreateCustomerRequestValidator _create = new();
    private readonly UpdateCustomerRequestValidator _update = new();

    private static CreateCustomerRequest Valid() => new()
    {
        FirstName = "Ann", LastName = "Lee", Email = "ann@example.com", Phone = "12345"
    };

    [Fact]
    public void Create_Valid_Passes() => _create.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Create_BlankFirstName_Fails()
    {
        var req = Valid(); req.FirstName = "";
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateCustomerRequest.FirstName));
    }

    [Fact]
    public void Create_InvalidEmail_Fails()
    {
        var req = Valid(); req.Email = "not-an-email";
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateCustomerRequest.Email));
    }

    [Fact]
    public void Create_LongFirstName_Fails()
    {
        var req = Valid(); req.FirstName = new string('a', 101);
        _create.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_LongPhone_Fails()
    {
        var req = Valid(); req.Phone = new string('1', 51);
        _create.Validate(req).Errors.Should().Contain(e => e.PropertyName == nameof(CreateCustomerRequest.Phone));
    }

    [Fact]
    public void Update_Valid_Passes()
    {
        _update.Validate(new UpdateCustomerRequest { FirstName = "Ann", LastName = "Lee", Email = "a@b.com" })
            .IsValid.Should().BeTrue();
    }
}
