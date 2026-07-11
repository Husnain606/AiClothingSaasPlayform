using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TryOnRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TryOnRequests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TryOnRequests_TenantId_Status_CreatedAt",
            table: "TryOnRequests",
            columns: new[] { "TenantId", "Status", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TryOnRequests");
    }
}
