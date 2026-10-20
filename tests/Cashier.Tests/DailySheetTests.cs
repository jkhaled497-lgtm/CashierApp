using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class DailySheetTests
{
    [Fact]
    public async Task DailyProductSale_UsesShopStockAndQuantityEditsApplyTheDifference()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = "DAILY-01",
            Name = "منتج يومي",
            CurrentStock = 5,
            WarehouseStock = 8,
            SellingPrice = 10,
            CostPrice = 5
        };
        var user = NewUser();
        var customer = new Customer { CustomerCode = "DAILY-C1", FullName = "عميل يومي" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            db.Users.Add(user);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        var service = new DailySheetService(factory);
        var sale = await service.CreateDailyProductAsync(new CreateDailyProductRequest(
            product.Id, 2, customer.Id, user.Id));

        Assert.Equal(3m, (await new ProductService(factory).GetByIdAsync(product.Id))!.CurrentStock);
        Assert.Equal(8m, (await new ProductService(factory).GetByIdAsync(product.Id))!.WarehouseStock);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal(user.Id, sale.UserId);

        await service.UpdateDailyProductAsync(sale.Id, 3, null);
        Assert.Equal(2m, (await new ProductService(factory).GetByIdAsync(product.Id))!.CurrentStock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateDailyProductAsync(sale.Id, 6, null));
        Assert.Equal(2m, (await new ProductService(factory).GetByIdAsync(product.Id))!.CurrentStock);
    }

    [Fact]
    public async Task DailyProductSale_UpdatesSalesProfitRevenueAndCustomerBalanceReports()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = "DAILY-REPORT-01",
            Name = "منتج تقرير يومي",
            CurrentStock = 8,
            SellingPrice = 10,
            CostPrice = 5
        };
        var user = NewUser();
        var customer = new Customer { CustomerCode = "DAILY-REPORT-C1", FullName = "عميل تقرير يومي" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AddRange(product, user, customer);
            await db.SaveChangesAsync();
            db.DailyProducts.Add(new DailyProduct
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = 5,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }

        var service = new DailySheetService(factory);
        var sale = await service.CreateDailyProductAsync(new CreateDailyProductRequest(
            product.Id, 2, customer.Id, user.Id, 7, PaymentMethod.CreditCard));

        var from = BusinessTime.ToUtc(BusinessTime.ToEgyptLocal(sale.CreatedAt).Date);
        var to = BusinessTime.ToUtc(BusinessTime.ToEgyptLocal(sale.CreatedAt).Date.AddDays(1));
        var invoiceService = new InvoiceService(factory);
        var dailyReport = Assert.Single(await invoiceService.GetDailyReportsAsync(from, to));
        Assert.Equal(0, dailyReport.InvoiceCount);
        Assert.Equal(1, dailyReport.DailySaleCount);
        Assert.Equal(20m, dailyReport.SalesTotal);
        Assert.Equal(10m, dailyReport.CostTotal);
        Assert.Equal(10m, dailyReport.ProfitTotal);
        Assert.Equal(7m, dailyReport.CardReceived);

        var revenue = Assert.Single(await invoiceService.GetRevenueReportsAsync(from, to));
        Assert.Equal(7m, revenue.CardReceived);
        var topProduct = Assert.Single(await invoiceService.GetTopSellingProductsAsync(from, to));
        Assert.Equal(2m, topProduct.QuantitySold);

        var journal = Assert.Single((await new DailyJournalReportService(factory).GetForPeriodAsync(from, to))
            .Where(row => row.Type == DailyJournalType.DailySale));
        Assert.Equal(20m, journal.Amount);
        Assert.Contains("المتبقي 13.00", journal.Notes);
        var account = Assert.Single(await new CustomerService(factory).GetAccountReportsAsync(from, to));
        Assert.Equal(13m, account.PeriodCharges);
        Assert.Equal(13m, account.CurrentBalance);

        await service.UpdateDailyProductAsync(sale.Id, 2, customer.Id, 12, PaymentMethod.CreditCard);
        account = Assert.Single(await new CustomerService(factory).GetAccountReportsAsync(from, to));
        Assert.Equal(8m, account.CurrentBalance);
        Assert.Equal(13m, account.PeriodCharges);
        Assert.Equal(5m, account.PeriodPayments);
        revenue = Assert.Single(await invoiceService.GetRevenueReportsAsync(from, to));
        Assert.Equal(12m, revenue.CardReceived);
    }

    [Fact]
    public async Task UpdateWarehouseTransfer_AppliesQuantityDifferenceToBothBalances()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = "DAILY-02",
            Name = "منتج تحويل",
            CurrentStock = 3,
            WarehouseStock = 7,
            SellingPrice = 10,
            CostPrice = 5
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.StockAdjustments.Add(new StockAdjustment
            {
                ProductId = product.Id,
                Type = StockAdjustmentType.TransferToShop,
                Quantity = 2,
                Reason = "تحويل تجريبي"
            });
            await db.SaveChangesAsync();
        }

        var movement = await new StockAdjustmentService(factory)
            .GetRecentPageAsync(1, 10);
        var service = new StockAdjustmentService(factory);
        await service.UpdateAsync(movement.Items.Single().Id, 4, "تعديل التحويل");

        await using var verify = await factory.CreateDbContextAsync();
        var savedProduct = await verify.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(5m, savedProduct.CurrentStock);
        Assert.Equal(5m, savedProduct.WarehouseStock);
        var savedMovement = await verify.StockAdjustments.SingleAsync();
        Assert.Equal(4m, savedMovement.Quantity);
        Assert.Equal("تعديل التحويل", savedMovement.Reason);
    }

    [Fact]
    public async Task DailyProductListing_FiltersBySelectedDay()
    {
        await using var factory = await CreateDatabaseAsync();
        var product = new Product
        {
            Sku = "DAILY-03",
            Name = "منتج اليوم",
            CurrentStock = 10,
            SellingPrice = 10,
            CostPrice = 5
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.DailyProducts.AddRange(
                new DailyProduct
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Sku = product.Sku,
                    Quantity = 1,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new DailyProduct
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Sku = product.Sku,
                    Quantity = 1,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
                });
            await db.SaveChangesAsync();
        }

        var entries = await new DailySheetService(factory).GetDailyProductsAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Single(entries);
        Assert.Equal(1m, entries[0].Quantity);
    }

    [Fact]
    public async Task DailyAccountSheets_ReturnOnlyCustomerAndSupplierMovementsFromSelectedDay()
    {
        await using var factory = await CreateDatabaseAsync();
        var customer = new Customer { CustomerCode = "DAILY-C2", FullName = "عميل حركات" };
        var supplier = new Supplier { Name = "مورد حركات" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Add(customer);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            db.CustomerLedgerEntries.AddRange(
                new CustomerLedgerEntry
                {
                    CustomerId = customer.Id,
                    Type = CustomerLedgerEntryType.BalanceCharge,
                    Amount = 20,
                    BalanceAfter = 20,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new CustomerLedgerEntry
                {
                    CustomerId = customer.Id,
                    Type = CustomerLedgerEntryType.BalancePayment,
                    Amount = 10,
                    BalanceAfter = 10,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
                });
            db.SupplierBalanceEntries.AddRange(
                new SupplierBalanceEntry
                {
                    SupplierId = supplier.Id,
                    Type = SupplierBalanceEntryType.Charge,
                    Amount = 30,
                    BalanceAfter = 30,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new SupplierBalanceEntry
                {
                    SupplierId = supplier.Id,
                    Type = SupplierBalanceEntryType.Payment,
                    Amount = 15,
                    BalanceAfter = 15,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
                });
            await db.SaveChangesAsync();
        }

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var service = new DailySheetService(factory);
        var customerRows = await service.GetCustomerMovementsAsync(from, to);
        var supplierRows = await service.GetSupplierMovementsAsync(from, to);

        Assert.Equal(CustomerLedgerEntryType.BalanceCharge, Assert.Single(customerRows).Type);
        Assert.Equal(SupplierBalanceEntryType.Charge, Assert.Single(supplierRows).Type);
    }

    [Fact]
    public async Task DailyJournalReport_CombinesBusinessMovementsAndCreatesFallbackNotes()
    {
        await using var factory = await CreateDatabaseAsync();
        var happenedAt = new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero);
        var product = new Product
        {
            Sku = "JOURNAL-01",
            Name = "منتج اليومية",
            SellingPrice = 20,
            CostPrice = 10
        };
        var customer = new Customer { CustomerCode = "JOURNAL-C1", FullName = "عميل اليومية" };
        var supplier = new Supplier { Name = "مورد اليومية" };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AddRange(product, customer, supplier);
            await db.SaveChangesAsync();

            db.Invoices.Add(new Invoice
            {
                InvoiceNumber = "SALE-1",
                CustomerId = customer.Id,
                GrandTotal = 40,
                TotalPaid = 30,
                CreatedAt = happenedAt,
                Items =
                [
                    new InvoiceLine
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Sku = product.Sku,
                        UnitPrice = 20,
                        Quantity = 2,
                        LineTotal = 40
                    }
                ]
            });
            db.PurchaseInvoices.Add(new PurchaseInvoice
            {
                InvoiceNumber = "bill1",
                SupplierId = supplier.Id,
                GrandTotal = 50,
                TotalPaid = 20,
                CreatedAt = happenedAt.AddMinutes(1),
                Items =
                [
                    new PurchaseInvoiceLine
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Sku = product.Sku,
                        UnitPrice = 10,
                        Quantity = 5,
                        LineTotal = 50
                    }
                ]
            });
            db.DailyProducts.Add(new DailyProduct
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = 3,
                CustomerId = customer.Id,
                CreatedAt = happenedAt.AddMinutes(2)
            });
            db.StockAdjustments.AddRange(
                new StockAdjustment
                {
                    ProductId = product.Id,
                    Type = StockAdjustmentType.WarehouseIn,
                    Quantity = 5,
                    Reason = "bill1",
                    CreatedAt = happenedAt.AddMinutes(1)
                },
                new StockAdjustment
                {
                    ProductId = product.Id,
                    Type = StockAdjustmentType.TransferToShop,
                    Quantity = 2,
                    CreatedAt = happenedAt.AddMinutes(3)
                });
            db.CustomerLedgerEntries.AddRange(
                new CustomerLedgerEntry
                {
                    CustomerId = customer.Id,
                    Type = CustomerLedgerEntryType.BalanceCharge,
                    Amount = 15,
                    BalanceAfter = 15,
                    CreatedAt = happenedAt.AddMinutes(4)
                },
                new CustomerLedgerEntry
                {
                    CustomerId = customer.Id,
                    Type = CustomerLedgerEntryType.BalancePayment,
                    Amount = 5,
                    BalanceAfter = 10,
                    Notes = "سداد يدوي",
                    CreatedAt = happenedAt.AddMinutes(5)
                });
            db.SupplierBalanceEntries.AddRange(
                new SupplierBalanceEntry
                {
                    SupplierId = supplier.Id,
                    Type = SupplierBalanceEntryType.Charge,
                    Amount = 30,
                    BalanceAfter = 30,
                    CreatedAt = happenedAt.AddMinutes(6)
                },
                new SupplierBalanceEntry
                {
                    SupplierId = supplier.Id,
                    Type = SupplierBalanceEntryType.Payment,
                    Amount = 10,
                    BalanceAfter = 20,
                    CreatedAt = happenedAt.AddMinutes(7)
                });
            db.Expenses.Add(new BusinessExpense
            {
                Title = "كهرباء",
                Amount = 12,
                CreatedAt = happenedAt.AddMinutes(8)
            });
            db.DailyProducts.Add(new DailyProduct
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = 1,
                CreatedAt = happenedAt.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        var rows = await new DailyJournalReportService(factory).GetForPeriodAsync(
            new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(10, rows.Count);
        var saleInvoice = Assert.Single(rows.Where(row => row.Type == DailyJournalType.SalesInvoice));
        Assert.Contains("منتج اليومية × 2", saleInvoice.Products ?? string.Empty);
        Assert.Contains("المتبقي 10.00", saleInvoice.Notes);
        var purchaseInvoice = Assert.Single(rows.Where(row => row.Type == DailyJournalType.PurchaseInvoice));
        Assert.Equal("bill1", purchaseInvoice.Reference);
        var warehouseIn = Assert.Single(rows.Where(row => row.Type == DailyJournalType.WarehouseEntry));
        Assert.Contains("فاتورة شراء", warehouseIn.Notes);
        Assert.Contains("بدون فاتورة", Assert.Single(rows.Where(row => row.Type == DailyJournalType.DailySale)).Notes);
        Assert.Contains("استحقاق على العميل", Assert.Single(rows.Where(row => row.Type == DailyJournalType.CustomerCharge)).Notes);
        Assert.Equal("سداد يدوي", Assert.Single(rows.Where(row => row.Type == DailyJournalType.CustomerPayment)).Notes);
        Assert.Contains("استحقاق للمورد", Assert.Single(rows.Where(row => row.Type == DailyJournalType.SupplierCharge)).Notes);
        Assert.Contains("مصروف: كهرباء", Assert.Single(rows.Where(row => row.Type == DailyJournalType.Expense)).Notes);
        Assert.Equal(rows.OrderBy(row => row.CreatedAt), rows);
    }

    private static UserAccount NewUser() => new()
    {
        Username = $"user-{Guid.NewGuid():N}"[..12],
        PasswordHash = "hash",
        Salt = "salt",
        Role = UserRole.Manager
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
