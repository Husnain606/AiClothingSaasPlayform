namespace FashionSaaS.TryOn.Domain;

public class MeasurementRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public MeasurementStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public bool HeightCmProvided { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipsCm { get; set; }
    public decimal? ShoulderWidthCm { get; set; }
    public decimal? InseamCm { get; set; }
    public SizeCode? RecommendedSize { get; set; }
    public decimal? ConfidenceScore { get; set; }
}
