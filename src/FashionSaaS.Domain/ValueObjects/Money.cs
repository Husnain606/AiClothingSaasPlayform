namespace FashionSaaS.Domain.ValueObjects;

public record Money(decimal Amount, string Currency = "PKR")
{
    public static Money Zero => new(0);
}
