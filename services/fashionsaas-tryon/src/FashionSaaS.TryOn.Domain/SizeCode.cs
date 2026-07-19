using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SizeCode
{
    Xs,
    S,
    M,
    L,
    Xl,
    Xxl
}
