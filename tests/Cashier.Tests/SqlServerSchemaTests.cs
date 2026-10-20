using Cashier.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class SqlServerSchemaTests
{
    [Fact]
    public void Model_GeneratesSqlServerSchema()
    {
        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlServer("Server=Eng-Khaled;Database=Cashier;Integrated Security=True")
            .Options;
        using var db = new CashierDbContext(options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE [Users]", script);
        Assert.Contains("CREATE TABLE [CustomerLedgerEntries]", script);
        Assert.Contains("CREATE TABLE [Suppliers]", script);
        Assert.Contains("CREATE TABLE [SupplierBalanceEntries]", script);
        Assert.Contains("CREATE TABLE [Expenses]", script);
        Assert.Contains("[WarehouseStock] decimal(18,3)", script);
        Assert.Contains("CREATE UNIQUE INDEX", script);
    }
}
