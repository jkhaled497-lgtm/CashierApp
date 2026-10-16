using Cashier.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cashier.Infrastructure.Migrations;

[DbContext(typeof(CashierDbContext))]
[Migration("20261012040000_AddInvoiceCostSnapshot")]
public partial class AddInvoiceCostSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "CostPrice",
            table: "InvoiceLines",
            type: "decimal(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql(
            "UPDATE [InvoiceLines] SET [CostPrice] = [Products].[CostPrice] " +
            "FROM [InvoiceLines] INNER JOIN [Products] ON [InvoiceLines].[ProductId] = [Products].[Id];");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CostPrice", table: "InvoiceLines");
    }
}
