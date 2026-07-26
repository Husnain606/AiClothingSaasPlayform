using FashionSaaS.Application.Orders.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Orders.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one item is required.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity)
                .GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
        });

        RuleFor(x => x.ShippingAddress.FirstName).NotEmpty().WithMessage("FirstName is required.");
        RuleFor(x => x.ShippingAddress.LastName).NotEmpty().WithMessage("LastName is required.");
        RuleFor(x => x.ShippingAddress.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");
        RuleFor(x => x.ShippingAddress.Phone).NotEmpty().WithMessage("Phone is required.");
        RuleFor(x => x.ShippingAddress.Street).NotEmpty().WithMessage("Street is required.");
        RuleFor(x => x.ShippingAddress.City).NotEmpty().WithMessage("City is required.");
        RuleFor(x => x.ShippingAddress.State).NotEmpty().WithMessage("State is required.");
        RuleFor(x => x.ShippingAddress.ZipCode).NotEmpty().WithMessage("ZipCode is required.");
        RuleFor(x => x.ShippingAddress.Country)
            .NotEmpty().WithMessage("Country is required.")
            .Length(2).WithMessage("Country must be a 2-letter code.");
    }
}
