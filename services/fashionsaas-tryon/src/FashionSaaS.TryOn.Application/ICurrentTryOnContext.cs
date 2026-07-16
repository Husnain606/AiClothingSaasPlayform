namespace FashionSaaS.TryOn.Application;

public interface ICurrentTryOnContext
{
    Guid TenantId { get; }
    Guid CustomerId { get; }
    int AiUsageLimit { get; }
    bool IsAuthenticated { get; }
}
