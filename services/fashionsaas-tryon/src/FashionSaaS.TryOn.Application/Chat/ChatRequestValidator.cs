using FashionSaaS.TryOn.Application.Gemini;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Application.Chat;

public class ChatRequestValidator : AbstractValidator<ChatRequestDto>
{
    private const int MaxMessages = 20;

    public ChatRequestValidator(IOptions<GeminiSettings> geminiOptions)
    {
        var maxTotalChars = geminiOptions.Value.ChatHistoryMaxTotalChars;

        // Cascade.Stop: FluentValidation does not null-guard Must predicates, so the count/length
        // checks must never run when Messages is null (NotEmpty covers null and empty).
        RuleFor(x => x.Messages)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one message is required.")
            .Must(m => m.Count <= MaxMessages)
            .WithMessage($"No more than {MaxMessages} messages may be sent.")
            .Must(m => m.Sum(msg => msg.Content?.Length ?? 0) <= maxTotalChars)
            .WithMessage($"Total message content must not exceed {maxTotalChars} characters.");

        RuleForEach(x => x.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Role).Must(r => r is "user" or "model").WithMessage("Role must be 'user' or 'model'.");
            message.RuleFor(m => m.Content).NotEmpty();
        });
    }
}
