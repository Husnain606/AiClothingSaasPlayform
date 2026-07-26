using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOrderPaymentProof : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CardLast4",
            table: "Orders");

        migrationBuilder.AddColumn<string>(
            name: "PaymentInstructions",
            table: "Tenants",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "OrderPaymentProofs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderPaymentProofs", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrderPaymentProofs_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrderPaymentProofs_OrderId",
            table: "OrderPaymentProofs",
            column: "OrderId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderPaymentProofs_TenantId",
            table: "OrderPaymentProofs",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OrderPaymentProofs");

        migrationBuilder.DropColumn(
            name: "PaymentInstructions",
            table: "Tenants");

        migrationBuilder.AddColumn<string>(
            name: "CardLast4",
            table: "Orders",
            type: "nvarchar(4)",
            maxLength: 4,
            nullable: false,
            defaultValue: "");
    }
}
