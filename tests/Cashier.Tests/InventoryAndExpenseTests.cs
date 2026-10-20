using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class InventoryAndExpenseTests
{
    [Fact]
    public async Task TransferToShop_MovesWarehouseQuantityWithoutChangingTotalStock()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = $"INV-{Guid.NewGuid():N}"[..14],
            Name = "منتج مخزون",
            CostPrice = 3m,
            SellingPrice = 5m,
            CurrentStock = 4m,
            WarehouseStock = 6m
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new StockAdjustmentService(factory);
        await service.AdjustAsync(product.Id, StockAdjustmentType.TransferToShop, 2m, null, "جرد يومي");

        await using var verify = await factory.CreateDbContextAsync();
        var saved = await verify.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(6m, saved.CurrentStock);
        Assert.Equal(4m, saved.WarehouseStock);
        var movement = await verify.StockAdjustments.SingleAsync();
        Assert.Equal(StockAdjustmentType.TransferToShop, movement.Type);
        Assert.Equal(2m, movement.Quantity);

        var history = await service.GetRecentPageAsync(1, 10);
        Assert.Equal(4m, Assert.Single(history.Items).Product.WarehouseStock);
    }

    [Fact]
    public async Task InventoryMovementHistory_ContainsWarehouseMovementsOnly()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = $"INV-{Guid.NewGuid():N}"[..14],
            Name = "منتج مخزون",
            CostPrice = 3m,
            SellingPrice = 5m,
            CurrentStock = 4m,
            WarehouseStock = 6m
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.StockAdjustments.AddRange(
                new StockAdjustment
                {
                    ProductId = product.Id,
                    Type = StockAdjustmentType.StockIn,
                    Quantity = 4m
                },
                new StockAdjustment
                {
                    ProductId = product.Id,
                    Type = StockAdjustmentType.WarehouseIn,
                    Quantity = 6m
                });
            await db.SaveChangesAsync();
        }

        var page = await new StockAdjustmentService(factory).GetRecentPageAsync(1, 10);

        var movement = Assert.Single(page.Items);
        Assert.Equal(StockAdjustmentType.WarehouseIn, movement.Type);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task TransferToShop_RejectsQuantityBeyondWarehouseBalance()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = $"INV-{Guid.NewGuid():N}"[..14],
            Name = "منتج مخزون",
            CostPrice = 3m,
            SellingPrice = 5m,
            CurrentStock = 4m,
            WarehouseStock = 1m
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new StockAdjustmentService(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(
            product.Id, StockAdjustmentType.TransferToShop, 2m, null, null));

        await using var verify = await factory.CreateDbContextAsync();
        var saved = await verify.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(4m, saved.CurrentStock);
        Assert.Equal(1m, saved.WarehouseStock);
        Assert.Empty(await verify.StockAdjustments.ToListAsync());
    }

    [Fact]
    public async Task Adjust_RejectsMovementTypesOtherThanWarehouseInAndTransferToShop()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = $"INV-{Guid.NewGuid():N}"[..14],
            Name = "منتج مخزون",
            CostPrice = 3m,
            SellingPrice = 5m,
            CurrentStock = 4m,
            WarehouseStock = 6m
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new StockAdjustmentService(factory);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AdjustAsync(
            product.Id, StockAdjustmentType.StockOut, 1m, null, null));

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(4m, (await verify.Products.SingleAsync(item => item.Id == product.Id)).CurrentStock);
        Assert.Empty(await verify.StockAdjustments.ToListAsync());
    }

    [Fact]
    public async Task ProductService_SavesInitialShopAndWarehouseBalancesWithAuditMovements()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = $"INV-{Guid.NewGuid():N}"[..14],
            Name = "منتج برصيد افتتاحي",
            CostPrice = 3m,
            SellingPrice = 5m,
            CurrentStock = 2m,
            WarehouseStock = 5m
        };

        await new ProductService(factory).SaveAsync(product);

        await using var verify = await factory.CreateDbContextAsync();
        var saved = await verify.Products.SingleAsync();
        Assert.Equal(2m, saved.CurrentStock);
        Assert.Equal(5m, saved.WarehouseStock);
        var movements = await verify.StockAdjustments.OrderBy(x => x.Type).ToListAsync();
        Assert.Collection(movements,
            movement =>
            {
                Assert.Equal(StockAdjustmentType.StockIn, movement.Type);
                Assert.Equal(2m, movement.Quantity);
                Assert.Equal("رصيد افتتاحي للمحل", movement.Reason);
            },
            movement =>
            {
                Assert.Equal(StockAdjustmentType.WarehouseIn, movement.Type);
                Assert.Equal(5m, movement.Quantity);
                Assert.Equal("رصيد افتتاحي للمخزن", movement.Reason);
            });
    }

    [Fact]
    public async Task ExpenseService_RecordsExpenseAndRestrictsCashierRole()
    {
        await using var factory = await CreateDatabaseAsync();
        var manager = NewUser(UserRole.Manager);
        var cashier = NewUser(UserRole.Cashier);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.AddRange(manager, cashier);
            await db.SaveChangesAsync();
        }

        var service = new ExpenseService(factory);
        var date = DateTime.Today;
        var expense = await service.CreateAsync(new CreateExpenseRequest(
            "فاتورة كهرباء", 825.5m, date, "إيصال الشهر", manager.Id));
        Assert.Equal(825.5m, expense.Amount);
        Assert.Equal("فاتورة كهرباء", expense.Title);

        await using (var verify = await factory.CreateDbContextAsync())
        {
            var savedExpense = await verify.Expenses.SingleAsync(item => item.Id == expense.Id);
            Assert.Equal(BusinessTime.ToUtc(date), savedExpense.CreatedAt);
            Assert.Equal(manager.Id, savedExpense.UserId);
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            new CreateExpenseRequest("مصروف غير مصرح", 20m, date, null, cashier.Id)));
    }

    private static UserAccount NewUser(UserRole role) => new()
    {
        Username = $"user-{Guid.NewGuid():N}"[..12],
        PasswordHash = "hash",
        Salt = "salt",
        Role = role
    };

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
