using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class ProductPaginationTests
{
    [Fact]
    public async Task SaveAsync_RejectsSellingPriceBelowCostPrice()
    {
        var product = new Product
        {
            Sku = "LOW-MARGIN",
            Name = "منتج",
            CostPrice = 10m,
            SellingPrice = 9m
        };

        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite("Data Source=:memory:").Options;
        await Assert.ThrowsAsync<ArgumentException>(() => new ProductService(new TestContextFactory(options)).SaveAsync(product));
    }

    [Fact]
    public async Task SearchPageAsync_ReturnsOnlyRequestedActivePageAndTotalCount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Products.AddRange(
                Product("A", "Apple"),
                Product("B", "Bread"),
                Product("C", "Coffee"),
                Product("D", "Draft", isActive: false));
            await db.SaveChangesAsync();
        }

        var result = await new ProductService(factory)
            .SearchPageAsync(new ProductSearch(), pageNumber: 2, pageSize: 2);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Single(result.Items);
        Assert.Equal("Coffee", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchPageAsync_FindsMatchingProductOutsideUnfilteredFirstPage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Products.AddRange(Enumerable.Range(1, 12)
                .Select(index => Product($"SKU-{index:00}", index == 12 ? "Unique target" : $"Product {index:00}")));
            await db.SaveChangesAsync();
        }

        var result = await new ProductService(factory)
            .SearchPageAsync(new ProductSearch("target"), pageNumber: 1, pageSize: 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Unique target", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchPageAsync_FindsProductByArabicNameOnFirstPage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Products.AddRange(
                Product("SKU-01", "منتج عادي"),
                Product("SKU-02", "قهوة عربية"));
            await db.SaveChangesAsync();
        }

        var result = await new ProductService(factory)
            .SearchPageAsync(new ProductSearch("قهوة"), pageNumber: 1, pageSize: 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("قهوة عربية", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAllMatchingProductsBeyondPreviousLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Products.AddRange(Enumerable.Range(1, 305)
                .Select(index => Product($"SKU-{index:000}", $"Product {index:000}")));
            await db.SaveChangesAsync();
        }

        var result = await new ProductService(factory).SearchAsync(new ProductSearch());

        Assert.Equal(305, result.Count);
        Assert.Equal("Product 305", result[^1].Name);
    }

    private static Product Product(string sku, string name, bool isActive = true) =>
        new() { Sku = sku, Name = name, IsActive = isActive, SellingPrice = 1m, CostPrice = 1m };

    private sealed class TestContextFactory(DbContextOptions<CashierDbContext> options) : IDbContextFactory<CashierDbContext>
    {
        public CashierDbContext CreateDbContext() => new(options);
        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
    }
}
