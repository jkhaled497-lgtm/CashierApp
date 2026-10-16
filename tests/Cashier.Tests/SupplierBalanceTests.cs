using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class SupplierBalanceTests
{
    [Fact]
    public async Task SupplierBalance_TracksOpeningChargeAndPaymentWithoutAllowingOverpayment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var service = new SupplierService(factory);
        var supplier = new Supplier { Name = "مورد اختبار" };
        await service.SaveAsync(supplier, 100m, userId: null);
        await service.ChangeBalanceAsync(
            supplier.Id, 25m, SupplierBalanceEntryType.Charge, null, PaymentMethod.CreditCard, "فاتورة شراء");
        await service.ChangeBalanceAsync(
            supplier.Id, 40m, SupplierBalanceEntryType.Payment, null, PaymentMethod.Cash, "دفعة نقدية");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeBalanceAsync(
            supplier.Id, 100m, SupplierBalanceEntryType.Payment, null, PaymentMethod.Cash, null));

        await using var verify = await factory.CreateDbContextAsync();
        var savedSupplier = await verify.Suppliers.SingleAsync(x => x.Id == supplier.Id);
        Assert.Equal(85m, savedSupplier.CurrentBalance);
        var ledger = await verify.SupplierBalanceEntries.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(
            [SupplierBalanceEntryType.OpeningBalance, SupplierBalanceEntryType.Charge, SupplierBalanceEntryType.Payment],
            ledger.Select(x => x.Type));
        Assert.Equal([100m, 125m, 85m], ledger.Select(x => x.BalanceAfter));
        Assert.Equal(PaymentMethod.CreditCard, ledger[1].PaymentMethod);
        Assert.Equal(PaymentMethod.Cash, ledger[2].PaymentMethod);
    }

    [Fact]
    public async Task CreateCustomer_AllowsMissingNameAndUsesGeneratedDisplayName()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var customer = await new CustomerService(factory).CreateAsync(new Customer
        {
            CustomerCode = string.Empty,
            FullName = string.Empty
        });

        Assert.Equal("عميل جديد", customer.FullName);
        Assert.StartsWith("CUST-", customer.CustomerCode);
    }

    [Fact]
    public async Task AccountReports_SummarizeCurrentBalancesAndMovementsForSelectedPeriod()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);
        var customer = new Customer
        {
            CustomerCode = "CUST-REPORT",
            FullName = "عميل التقرير",
            CurrentBalance = 20m
        };
        customer.LedgerEntries.Add(new CustomerLedgerEntry
        {
            Type = CustomerLedgerEntryType.InvoiceCharge,
            Amount = 30m,
            BalanceAfter = 30m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        customer.LedgerEntries.Add(new CustomerLedgerEntry
        {
            Type = CustomerLedgerEntryType.InvoicePayment,
            Amount = 50m,
            BalanceAfter = 30m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        customer.LedgerEntries.Add(new CustomerLedgerEntry
        {
            Type = CustomerLedgerEntryType.BalancePayment,
            Amount = 10m,
            BalanceAfter = 20m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        var supplierService = new SupplierService(factory);
        var supplier = new Supplier { Name = "مورد التقرير", CurrentBalance = 15m };
        supplier.BalanceEntries.Add(new SupplierBalanceEntry
        {
            Type = SupplierBalanceEntryType.Charge,
            Amount = 25m,
            BalanceAfter = 25m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        supplier.BalanceEntries.Add(new SupplierBalanceEntry
        {
            Type = SupplierBalanceEntryType.Payment,
            Amount = 10m,
            BalanceAfter = 15m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
        }

        var customerReport = Assert.Single(await new CustomerService(factory).GetAccountReportsAsync(start, end));
        var supplierReport = Assert.Single(await supplierService.GetAccountReportsAsync(start, end));

        Assert.Equal(20m, customerReport.CurrentBalance);
        Assert.Equal(30m, customerReport.PeriodCharges);
        Assert.Equal(10m, customerReport.PeriodPayments);
        Assert.Equal(15m, supplierReport.CurrentBalance);
        Assert.Equal(25m, supplierReport.PeriodCharges);
        Assert.Equal(10m, supplierReport.PeriodPayments);
    }

    private sealed class TestContextFactory(DbContextOptions<CashierDbContext> options) : IDbContextFactory<CashierDbContext>
    {
        public CashierDbContext CreateDbContext() => new(options);
        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
    }
}
