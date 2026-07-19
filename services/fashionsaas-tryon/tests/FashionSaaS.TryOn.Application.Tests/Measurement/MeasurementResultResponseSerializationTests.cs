using System.Text.Json;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Domain;
using FluentAssertions;

namespace FashionSaaS.TryOn.Application.Tests.Measurement;

public class MeasurementResultResponseSerializationTests
{
    [Fact]
    public void MeasurementResultResponse_SerializesRecommendedSizeAsString()
    {
        var response = new MeasurementResultResponse(
            ChestCm: 96.5m,
            WaistCm: 82.0m,
            HipsCm: 98.0m,
            ShoulderWidthCm: 44.0m,
            InseamCm: 78.0m,
            RecommendedSize: SizeCode.M,
            Confidence: 0.87m);

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.Should().Contain("\"recommendedSize\":\"M\"");
    }

    [Theory]
    [InlineData(SizeCode.Xs, "XS")]
    [InlineData(SizeCode.Xl, "XL")]
    [InlineData(SizeCode.Xxl, "XXL")]
    public void MeasurementResultResponse_SerializesCanonicalUppercaseSizeCodes(SizeCode sizeCode, string expectedWireValue)
    {
        var response = new MeasurementResultResponse(
            ChestCm: 96.5m,
            WaistCm: 82.0m,
            HipsCm: 98.0m,
            ShoulderWidthCm: 44.0m,
            InseamCm: 78.0m,
            RecommendedSize: sizeCode,
            Confidence: 0.87m);

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

        json.Should().Contain($"\"recommendedSize\":\"{expectedWireValue}\"");
    }
}
