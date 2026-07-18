namespace FashionSaaS.TryOn.Application.Chat;

public record ChatRequestDto(IReadOnlyList<ChatMessage> Messages, ChatProductContext? ProductContext);
