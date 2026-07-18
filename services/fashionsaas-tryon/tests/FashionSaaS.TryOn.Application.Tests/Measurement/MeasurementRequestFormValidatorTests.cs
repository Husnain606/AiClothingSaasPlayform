using FashionSaaS.TryOn.Application.Measurement;
using FluentAssertions;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Application.Tests.Measurement;

public class MeasurementRequestFormValidatorTests
{
    private readonly MeasurementRequestFormValidator _validator = new();

    private static FormFile CreateFakePhoto(string contentType = "image/jpeg")
    {
        byte[] bytes = [9, 9, 9];
        MemoryStream stream = new(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Theory]
    [InlineData(49)]
    [InlineData(251)]
    public async Task MeasurementRequestFormValidator_HeightOutOfRange_FailsValidation(int heightCm)
    {
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto(), HeightCm = heightCm };

        ValidationResult result = await _validator.ValidateAsync(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(MeasurementRequestForm.HeightCm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(50)]
    [InlineData(175)]
    [InlineData(250)]
    public async Task MeasurementRequestFormValidator_ValidHeightOrNone_PassesValidation(int? heightCm)
    {
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto(), HeightCm = heightCm };

        ValidationResult result = await _validator.ValidateAsync(form);

        result.IsValid.Should().BeTrue();
    }
}
