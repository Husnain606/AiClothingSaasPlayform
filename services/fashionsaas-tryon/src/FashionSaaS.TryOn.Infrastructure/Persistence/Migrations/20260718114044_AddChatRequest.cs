using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddChatRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChatRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                MessageLength = table.Column<int>(type: "int", nullable: false),
                ReplyLength = table.Column<int>(type: "int", nullable: false),
                HadProductContext = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatRequests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChatRequests_TenantId_Status_CreatedAt",
            table: "ChatRequests",
            columns: new[] { "TenantId", "Status", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChatRequests");
    }
}
