using FashionSaaS.Application.Wishlists.DTOs;
using FluentValidation;

namespace FashionSaaS.Application.Wishlists.Validators;

public class AddWishlistItemRequestValidator : AbstractValidator<AddWishlistItemRequest>
{
    public AddWishlistItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.");
    }
}
