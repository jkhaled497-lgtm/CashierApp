using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class PurchaseInvoiceTests
{
    [Fact]
    public async Task CreateInvoice_StoresPartialPaymentAndUpdatesStockAndSupplierBalance()
    {
        await using var factory = await CreateDatabaseAsync();
        var (supplier, product, user) = await SeedAsync(factory, UserRole.Manager);
        var service = new PurchaseInvoiceService(factory);

        var invoice = await service.CreateAsync(new CreatePurchaseInvoiceRequest(
            supplier.Id,
            [new PurchaseLineRequest(product.Id, 10m, 3m, 3m)],
            10.5m,
            PaymentMethod.CreditCard,
            user.Id));

        Assert.Equal(27m, invoice.GrandTotal);
        Assert.Equal(10.5m, invoice.TotalPaid);
        Assert.Equal(PurchaseInvoiceStatus.Partial, invoice.Status);
        var line = Assert.Single(invoice.Items);
        Assert.Equal(27m, line.LineTotal);
        Assert.Equal(3m, line.DiscountAmount);

        await using var db = await factory.CreateDbContextAsync();
        var savedProduct = await db.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(5m, savedProduct.CurrentStock);
        Assert.Equal(7.8m, savedProduct.CostPrice);
        var savedSupplier = await db.Suppliers.SingleAsync(item => item.Id == supplier.Id);
        Assert.Equal(21.5m, savedSupplier.CurrentBalance);
        Assert.Equal(1, await db.StockAdjustments.CountAsync(item =>
            item.ProductId == product.Id && item.Type == StockAdjustmentType.StockIn));

        var balanceEntries = await db.SupplierBalanceEntries.OrderBy(entry => entry.Id).ToListAsync();
        Assert.Equal(
            [SupplierBalanceEntryType.Charge, SupplierBalanceEntryType.Payment],
            balanceEntries.Select(entry => entry.Type));
        Assert.Equal([32m, 21.5m], balanceEntries.Select(entry => entry.BalanceAfter));
        Assert.Equal(PaymentMethod.CreditCard, balanceEntries[1].PaymentMethod);

        var page = await service.GetPageAsync(supplier.Id, 1, 20);
        Assert.Equal(invoice.InvoiceNumber, Assert.Single(page.Items).InvoiceNumber);
        var statistics = await service.GetStatisticsAsync(supplier.Id);
        Assert.Equal(1, statistics.InvoiceCount);
        Assert.Equal(27m, statistics.TotalPurchases);
        Assert.Equal(10.5m, statistics.TotalPaid);
        Assert.Equal(16.5m, statistics.OutstandingAmount);
        Assert.Equal(21.5m, statistics.SupplierBalance);
    }

    [Fact]
    public async Task CreateInvoice_RejectsExcessPaymentWithoutChangingStockOrBalance()
    {
        await using var factory = await CreateDatabaseAsync();
        var (supplier, product, user) = await SeedAsync(factory, UserRole.Admin);
        var service = new PurchaseInvoiceService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreatePurchaseInvoiceRequest(
                supplier.Id,
                [new PurchaseLineRequest(product.Id, 10m, 1m, 0m)],
                11m,
                PaymentMethod.Cash,
                user.Id)));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2m, (await db.Products.SingleAsync(item => item.Id == product.Id)).CurrentStock);
        Assert.Equal(5m, (await db.Suppliers.SingleAsync(item => item.Id == supplier.Id)).CurrentBalance);
        Assert.Empty(await db.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task CreateInvoice_RequiresManagerRole()
    {
        await using var factory = await CreateDatabaseAsync();
        var (supplier, product, user) = await SeedAsync(factory, UserRole.Cashier);
        var service = new PurchaseInvoiceService(factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            new CreatePurchaseInvoiceRequest(
                supplier.Id,
                [new PurchaseLineRequest(product.Id, 10m, 1m, 0m)],
                0m,
                PaymentMethod.Cash,
                user.Id)));
    }

    private static async Task<TestContextFactory> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options, connection);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        return factory;
    }

    private static async Task<(Supplier Supplier, Product Product, UserAccount User)> SeedAsync(
        TestContextFactory factory, UserRole role)
    {
        var supplier = new Supplier { Name = "مورد شراء", CurrentBalance = 5m };
        var product = new Product
        {
            Sku = $"PUR-{Guid.NewGuid():N}"[..14],
            Name = "منتج شراء",
            CostPrice = 6m,
            SellingPrice = 12m,
            CurrentStock = 2m
        };
        var user = new UserAccount
        {
            Username = $"user-{Guid.NewGuid():N}"[..12],
            PasswordHash = "hash",
            Salt = "salt",
            Role = role
        };
        await using var db = await factory.CreateDbContextAsync();
        db.AddRange(supplier, product, user);
        await db.SaveChangesAsync();
        return (supplier, product, user);
    }

    private sealed class TestContextFactory(
        DbContextOptions<CashierDbContext> options,
        SqliteConnection connection) : IDbContextFactory<CashierDbContext>, IAsyncDisposable
    {
        public CashierDbContext CreateDbContext() => new(options);
        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
