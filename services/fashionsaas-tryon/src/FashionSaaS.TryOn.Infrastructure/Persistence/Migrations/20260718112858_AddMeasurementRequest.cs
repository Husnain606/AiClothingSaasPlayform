using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMeasurementRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MeasurementRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                HeightCmProvided = table.Column<bool>(type: "bit", nullable: false),
                ChestCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                WaistCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                HipsCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                ShoulderWidthCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                InseamCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                RecommendedSize = table.Column<int>(type: "int", nullable: true),
                ConfidenceScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MeasurementRequests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MeasurementRequests_TenantId_Status_CreatedAt",
            table: "MeasurementRequests",
            columns: new[] { "TenantId", "Status", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MeasurementRequests");
    }
}
