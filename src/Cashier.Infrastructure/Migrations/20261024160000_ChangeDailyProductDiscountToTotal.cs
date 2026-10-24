using Cashier.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cashier.Infrastructure.Migrations;

[DbContext(typeof(CashierDbContext))]
[Migration("20261024160000_ChangeDailyProductDiscountToTotal")]
public partial class ChangeDailyProductDiscountToTotal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "DiscountPerUnit",
            table: "DailyProduct",
            newName: "DiscountAmount");

        migrationBuilder.Sql(
            "UPDATE \"DailyProduct\" " +
            "SET \"DiscountAmount\" = CAST(ROUND(CAST(\"DiscountAmount\" AS REAL) * CAST(\"Quantity\" AS REAL), 2) AS TEXT)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE \"DailyProduct\" " +
            "SET \"DiscountAmount\" = CASE WHEN \"Quantity\" = 0 THEN 0 " +
            "ELSE CAST(ROUND(CAST(\"DiscountAmount\" AS REAL) / CAST(\"Quantity\" AS REAL), 2) AS TEXT) END");

        migrationBuilder.RenameColumn(
            name: "DiscountAmount",
            table: "DailyProduct",
            newName: "DiscountPerUnit");
    }
}
