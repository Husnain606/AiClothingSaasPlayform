using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionSaaS.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOrderDiscountFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "DiscountAmount",
            table: "Orders",
            type: "decimal(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "DiscountCode",
            table: "Orders",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DiscountId",
            table: "Orders",
            type: "uniqueidentifier",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DiscountAmount",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "DiscountCode",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "DiscountId",
            table: "Orders");
    }
}
