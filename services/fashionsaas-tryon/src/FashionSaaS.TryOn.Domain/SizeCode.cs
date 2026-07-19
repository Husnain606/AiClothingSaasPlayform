using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SizeCode
{
    [JsonStringEnumMemberName("XS")]
    Xs,
    S,
    M,
    L,
    [JsonStringEnumMemberName("XL")]
    Xl,
    [JsonStringEnumMemberName("XXL")]
    Xxl
}
