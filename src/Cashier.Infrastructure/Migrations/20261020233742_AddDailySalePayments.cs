using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cashier.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailySalePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "DailyProduct",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "DailyProduct",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostPrice",
                table: "DailyProduct",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSellingPrice",
                table: "DailyProduct",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE DailyProduct
                SET UnitSellingPrice = Products.SellingPrice,
                    UnitCostPrice = Products.CostPrice,
                    AmountPaid = ROUND(DailyProduct.Quantity * Products.SellingPrice, 2)
                FROM DailyProduct
                INNER JOIN Products ON Products.Id = DailyProduct.ProductId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "DailyProduct");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "DailyProduct");

            migrationBuilder.DropColumn(
                name: "UnitCostPrice",
                table: "DailyProduct");

            migrationBuilder.DropColumn(
                name: "UnitSellingPrice",
                table: "DailyProduct");
        }
    }
}
