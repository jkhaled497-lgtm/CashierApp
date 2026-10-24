using Microsoft.Data.Sqlite;
using Cashier.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cashier.Tests;

public sealed class SqliteSchemaTests
{
    [Fact]
    public void Model_GeneratesSqliteCreateScript()
    {
        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new CashierDbContext(options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE \"Users\"", script);
        Assert.Contains("CREATE TABLE \"CustomerLedgerEntries\"", script);
        Assert.Contains("CREATE TABLE \"Suppliers\"", script);
        Assert.Contains("CREATE TABLE \"SupplierBalanceEntries\"", script);
        Assert.Contains("CREATE TABLE \"Expenses\"", script);
        Assert.Contains("\"WarehouseStock\" TEXT NOT NULL", script);
        Assert.Contains("CREATE UNIQUE INDEX", script);
    }

    [Fact]
    public void Migration_CreatesDatabaseSchema()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new CashierDbContext(options);

        db.Database.Migrate();

        Assert.Contains("20261023211408_InitialSqlite", db.Database.GetAppliedMigrations());
        Assert.Contains("20261024155000_AddDailyProductDiscount", db.Database.GetAppliedMigrations());
        Assert.Contains("20261024160000_ChangeDailyProductDiscountToTotal", db.Database.GetAppliedMigrations());
        var userTableCount = db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Users'")
            .Single();
        Assert.Equal(1, userTableCount);
        var discountColumnCount = db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM pragma_table_info('DailyProduct') WHERE name = 'DiscountAmount'")
            .Single();
        Assert.Equal(1, discountColumnCount);
    }

    [Fact]
    public async Task DiscountMigration_ConvertsExistingPerUnitDiscountsToSaleTotal()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new CashierDbContext(options);

        await db.GetService<IMigrator>().MigrateAsync("20261024155000_AddDailyProductDiscount");
        var product = new Cashier.Domain.Product
        {
            Sku = "MIGRATION-DISCOUNT",
            Name = "منتج ترحيل",
            SellingPrice = 100,
            CostPrice = 50
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"DailyProduct\" (\"ProductId\", \"ProductName\", \"Sku\", \"Quantity\", \"UnitSellingPrice\", \"UnitCostPrice\", \"DiscountPerUnit\", \"AmountPaid\", \"PaymentMethod\", \"CreatedAt\") VALUES ({product.Id}, {product.Name}, {product.Sku}, {4m}, {100m}, {50m}, {10m}, {360m}, {0}, {DateTimeOffset.UtcNow})");

        await db.Database.MigrateAsync();

        var migratedDiscount = await db.Database
            .SqlQueryRaw<decimal>("SELECT \"DiscountAmount\" AS Value FROM \"DailyProduct\"")
            .SingleAsync();
        Assert.Equal(40m, migratedDiscount);
    }
}
