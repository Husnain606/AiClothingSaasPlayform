namespace FashionSaaS.TryOn.Application.Chat;

public record ChatProductContext(string Name, string Description, IReadOnlyList<string> Sizes);
