using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTryOnProcessingState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalJobId",
            table: "TryOnRequests",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResultImageUrl",
            table: "TryOnRequests",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ExternalJobId",
            table: "TryOnRequests");

        migrationBuilder.DropColumn(
            name: "ResultImageUrl",
            table: "TryOnRequests");
    }
}
