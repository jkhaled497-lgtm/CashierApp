using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class InvoiceSettlementTests
{
    [Fact]
    public async Task PartialPayment_SavesInvoiceUserAndCustomerLedgerOnSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserAccount
            {
                Username = "manager",
                PasswordHash = "test-hash",
                Salt = "test-salt",
                Role = UserRole.Manager
            });
            db.Customers.Add(new Customer
            {
                CustomerCode = "CUST-0001",
                FullName = "عميل اختبار",
                CreditLimit = 100
            });
            db.Products.Add(new Product
            {
                Sku = "SKU-001",
                Name = "منتج اختبار",
                SellingPrice = 10,
                CurrentStock = 5,
                TaxRate = 10
            });
            db.InvoiceSequences.Add(new InvoiceSequence { Year = DateTimeOffset.UtcNow.Year, LastNumber = 0 });
            await db.SaveChangesAsync();
        }
        int customerId;
        int productId;
        int userId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            customerId = await db.Customers.Select(x => x.Id).SingleAsync();
            productId = await db.Products.Select(x => x.Id).SingleAsync();
            userId = await db.Users.Select(x => x.Id).SingleAsync();
        }

        var service = new InvoiceService(factory);
        var invoice = await service.CompleteSaleAsync(new CheckoutRequest(
            customerId,
            [new CheckoutLine(productId, 1)],
            0,
            0,
            [new PaymentTender(PaymentMethod.Cash, 5)],
            userId,
            null));

        Assert.Equal(10m, invoice.GrandTotal);
        Assert.Equal(0m, invoice.TaxTotal);
        Assert.Equal(5m, invoice.TotalPaid);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.Equal(userId, invoice.UserId);
        var invoiceSearch = await service.SearchPageAsync(invoice.InvoiceNumber, 1, 10);
        Assert.Single(invoiceSearch.Items);
        Assert.Equal(invoice.InvoiceNumber, invoiceSearch.Items[0].InvoiceNumber);
        Assert.Equal("عميل اختبار", invoiceSearch.Items[0].CustomerName);
        var customerSearch = await service.SearchPageAsync("عميل اختبار", 1, 10);
        Assert.Single(customerSearch.Items);
        Assert.Equal(invoice.InvoiceNumber, customerSearch.Items[0].InvoiceNumber);
        await using var verify = await factory.CreateDbContextAsync();
        var customer = await verify.Customers.SingleAsync(x => x.Id == customerId);
        Assert.Equal(5m, customer.CurrentBalance);
        Assert.Equal(4m, (await verify.Products.SingleAsync(x => x.Id == productId)).CurrentStock);
        var entries = await verify.CustomerLedgerEntries.Include(x => x.Invoice).OrderBy(x => x.Type).ToListAsync();
        Assert.Single(entries);
        Assert.All(entries, entry => Assert.Equal(invoice.InvoiceNumber, entry.Invoice!.InvoiceNumber));
        Assert.Contains(entries, entry => entry.Type == CustomerLedgerEntryType.InvoiceCharge && entry.Amount == 5m);

        var customerService = new CustomerService(factory);
        await customerService.RecordBalancePaymentAsync(
            customerId, 5m, userId, PaymentMethod.Cash, "سداد الفاتورة");
        verify.ChangeTracker.Clear();
        var settledInvoice = await verify.Invoices.SingleAsync(x => x.InvoiceNumber == invoice.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Paid, settledInvoice.Status);
        Assert.Equal(10m, settledInvoice.TotalPaid);
        Assert.Equal(0m, (await verify.Customers.SingleAsync(x => x.Id == customerId)).CurrentBalance);
        Assert.Contains(await verify.CustomerLedgerEntries.Include(x => x.Invoice).ToListAsync(),
            entry => entry.Type == CustomerLedgerEntryType.BalancePayment &&
                     entry.Amount == 5m &&
                     entry.Invoice!.InvoiceNumber == invoice.InvoiceNumber);

        await service.CompleteSaleAsync(new CheckoutRequest(
            customerId,
            [new CheckoutLine(productId, 1)],
            0,
            0,
            [new PaymentTender(PaymentMethod.Cash, 10)],
            userId,
            null));
        verify.ChangeTracker.Clear();
        Assert.Equal(2, await verify.CustomerLedgerEntries.CountAsync());

        var nearlyPaidInvoice = await service.CompleteSaleAsync(new CheckoutRequest(
            customerId,
            [new CheckoutLine(productId, 1)],
            0,
            0,
            [new PaymentTender(PaymentMethod.Cash, 9.99m)],
            userId,
            null));
        verify.ChangeTracker.Clear();
        var nearlyPaidEntry = await verify.CustomerLedgerEntries
            .SingleAsync(entry => entry.InvoiceId == nearlyPaidInvoice.Id);
        Assert.Equal(CustomerLedgerEntryType.InvoiceCharge, nearlyPaidEntry.Type);
        Assert.Equal(0.01m, nearlyPaidEntry.Amount);
    }

    [Fact]
    public async Task InitialAdmin_IsSeededAndMustChangePassword()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var auth = new AuthService(factory);
        await auth.EnsureInitialAdminAsync();
        var login = await auth.LoginAsync("admin", "Admin@12345");

        Assert.True(login.Succeeded);
        Assert.True(login.Session!.PasswordChangeRequired);
        await auth.ChangePasswordAsync(login.Session.UserId, "Admin@12345", "NewSecure@2026");
        var secondLogin = await auth.LoginAsync("admin", "NewSecure@2026");
        Assert.True(secondLogin.Succeeded);
        Assert.False(secondLogin.Session!.PasswordChangeRequired);
    }

    private sealed class TestContextFactory(DbContextOptions<CashierDbContext> options) : IDbContextFactory<CashierDbContext>
    {
        public CashierDbContext CreateDbContext() => new(options);
        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
    }
}
