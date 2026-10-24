using Cashier.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cashier.Infrastructure.Migrations;

[DbContext(typeof(CashierDbContext))]
[Migration("20261024155000_AddDailyProductDiscount")]
public partial class AddDailyProductDiscount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "DiscountPerUnit",
            table: "DailyProduct",
            type: "TEXT",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DiscountPerUnit",
            table: "DailyProduct");
    }
}
