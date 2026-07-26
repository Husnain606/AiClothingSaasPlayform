using FashionSaaS.Application.Tenants.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Tenants.Validators;

public class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.PaymentInstructions)
            .MaximumLength(2000).WithMessage("PaymentInstructions must not exceed 2000 characters.")
            .When(x => x.PaymentInstructions is not null);
    }
}
