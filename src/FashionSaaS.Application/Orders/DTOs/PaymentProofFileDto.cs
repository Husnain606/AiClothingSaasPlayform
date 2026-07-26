namespace FashionSaaS.Application.Orders.DTOs;

/// <summary>
/// A payment proof opened for download. <see cref="Content"/> is an open stream the caller
/// must dispose (the controller hands it to <c>File(...)</c>, which disposes it).
/// </summary>
public class PaymentProofFileDto
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}
