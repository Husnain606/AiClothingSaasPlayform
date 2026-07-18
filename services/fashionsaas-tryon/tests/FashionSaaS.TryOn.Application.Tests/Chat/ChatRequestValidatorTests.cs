using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Application.Gemini;
using FluentAssertions;
using FluentValidation.Results;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Application.Tests.Chat;

public class ChatRequestValidatorTests
{
    private readonly ChatRequestValidator _validator =
        new(Options.Create(new GeminiSettings { ApiKey = "test-key" }));

    [Fact]
    public async Task ChatRequestValidator_MoreThanTwentyMessages_FailsValidation()
    {
        List<ChatMessage> messages = [.. Enumerable.Range(0, 21).Select(i => new ChatMessage("user", $"message {i}"))];
        ChatRequestDto dto = new(messages, null);

        ValidationResult result = await _validator.ValidateAsync(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChatRequestDto.Messages));
    }

    [Fact]
    public async Task ChatRequestValidator_TotalCharsOverCap_FailsValidation()
    {
        // Default ChatHistoryMaxTotalChars is 8,000 — two 4,001-char messages exceed it.
        var longContent = new string('x', 4_001);
        ChatRequestDto dto = new([new ChatMessage("user", longContent), new ChatMessage("model", longContent)], null);

        ValidationResult result = await _validator.ValidateAsync(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChatRequestDto.Messages));
    }

    [Fact]
    public async Task ChatRequestValidator_EmptyMessages_FailsValidation()
    {
        ChatRequestDto dto = new([], null);

        ValidationResult result = await _validator.ValidateAsync(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChatRequestDto.Messages));
    }
}
