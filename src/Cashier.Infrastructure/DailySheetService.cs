using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class DailySheetService(IDbContextFactory<CashierDbContext> contextFactory) : IDailySheetService
{
    public async Task<IReadOnlyList<DailyProduct>> GetDailyProductsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.DailyProducts.AsNoTracking()
            .Include(item => item.Product)
            .Include(item => item.Customer)
            .Include(item => item.User)
            .AsQueryable();
        if (IsSqlite(db))
            return (await query.ToListAsync(cancellationToken))
                .Where(item => item.CreatedAt >= from && item.CreatedAt < to)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray();

        return await query.Where(item => item.CreatedAt >= from && item.CreatedAt < to)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<DailyProduct> CreateDailyProductAsync(
        CreateDailyProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "كمية البيع يجب أن تكون أكبر من صفر.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(
            item => item.Id == request.ProductId && item.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("المنتج غير موجود أو غير نشط.");
        if (product.CurrentStock < request.Quantity)
            throw new InvalidOperationException("كمية المحل غير كافية لإتمام البيع.");
        var total = Money(product.SellingPrice * request.Quantity);
        var amountPaid = request.AmountPaid is { } requestedAmountPaid
            ? Money(requestedAmountPaid)
            : total;
        ValidatePayment(total, amountPaid, request.PaymentMethod);

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await db.Customers.SingleOrDefaultAsync(
                item => item.Id == customerId && item.IsActive && !item.IsWalkIn, cancellationToken)
                ?? throw new KeyNotFoundException("العميل غير موجود أو غير نشط.");
        }
        if (request.UserId is { } userId &&
            !await db.Users.AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken))
            throw new UnauthorizedAccessException("حساب المستخدم غير نشط.");
        var remaining = total - amountPaid;
        if (remaining > 0 && customer is null)
            throw new InvalidOperationException("اختر عميلاً لتسجيل المبلغ المتبقي كمديونية.");

        product.CurrentStock -= request.Quantity;
        var entry = new DailyProduct
        {
            Product = product,
            ProductName = product.Name,
            Sku = product.Sku,
            Quantity = request.Quantity,
            UnitSellingPrice = product.SellingPrice,
            UnitCostPrice = product.CostPrice,
            AmountPaid = amountPaid,
            PaymentMethod = request.PaymentMethod,
            Customer = customer,
            UserId = request.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DailyProducts.Add(entry);
        if (remaining > 0)
        {
            customer!.CurrentBalance = Money(customer.CurrentBalance + remaining);
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                Customer = customer,
                UserId = request.UserId,
                Type = CustomerLedgerEntryType.BalanceCharge,
                Amount = remaining,
                BalanceAfter = customer.CurrentBalance,
                Notes = $"مبلغ متبقٍ من بيع {product.Name} بالشيت اليومي"
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entry;
    }

    public async Task UpdateDailyProductAsync(
        long dailyProductId,
        decimal quantity,
        int? customerId,
        decimal? amountPaid = null,
        PaymentMethod? paymentMethod = null,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "كمية البيع يجب أن تكون أكبر من صفر.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var entry = await db.DailyProducts.Include(item => item.Product).Include(item => item.Customer)
            .SingleOrDefaultAsync(item => item.Id == dailyProductId, cancellationToken)
            ?? throw new KeyNotFoundException("سطر البيع اليومي غير موجود.");
        var product = entry.Product;
        var quantityDifference = entry.Quantity - quantity;
        if (product.CurrentStock + quantityDifference < 0)
            throw new InvalidOperationException("لا يمكن زيادة كمية البيع؛ رصيد المحل الحالي لا يكفي.");

        var unitSellingPrice = entry.UnitSellingPrice ?? product.SellingPrice;
        var unitCostPrice = entry.UnitCostPrice ?? product.CostPrice;
        var total = Money(unitSellingPrice * quantity);
        var newAmountPaid = amountPaid is { } specifiedAmount
            ? Money(specifiedAmount)
            : total;
        var newPaymentMethod = paymentMethod ?? entry.PaymentMethod;
        ValidatePayment(total, newAmountPaid, newPaymentMethod);
        var oldRemaining = Money(
            Money((entry.UnitSellingPrice ?? product.SellingPrice) * entry.Quantity) - entry.AmountPaid);
        var newRemaining = Money(total - newAmountPaid);

        Customer? customer = null;
        if (customerId is { } selectedCustomerId)
        {
            customer = await db.Customers.SingleOrDefaultAsync(
                item => item.Id == selectedCustomerId && item.IsActive && !item.IsWalkIn, cancellationToken)
                ?? throw new KeyNotFoundException("العميل غير موجود أو غير نشط.");
        }
        if (newRemaining > 0 && customer is null)
            throw new InvalidOperationException("اختر عميلاً لتسجيل المبلغ المتبقي كمديونية.");

        AdjustCustomerBalance(
            db, entry.Customer, customer, oldRemaining, newRemaining, entry.UserId);

        product.CurrentStock += quantityDifference;
        entry.Quantity = quantity;
        entry.Customer = customer;
        entry.UnitSellingPrice = unitSellingPrice;
        entry.UnitCostPrice = unitCostPrice;
        entry.AmountPaid = newAmountPaid;
        entry.PaymentMethod = newPaymentMethod;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockAdjustment>> GetWarehouseMovementsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = WarehouseMovements(db)
            .Include(movement => movement.Product)
            .Include(movement => movement.Employee)
            .AsQueryable();
        if (IsSqlite(db))
            return (await query.ToListAsync(cancellationToken))
                .Where(movement => movement.CreatedAt >= from && movement.CreatedAt < to)
                .OrderByDescending(movement => movement.CreatedAt)
                .ToArray();

        return await query.Where(movement => movement.CreatedAt >= from && movement.CreatedAt < to)
            .OrderByDescending(movement => movement.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerLedgerEntry>> GetCustomerMovementsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.CustomerLedgerEntries.AsNoTracking()
            .Include(entry => entry.Customer)
            .Include(entry => entry.Invoice)
            .AsQueryable();
        if (IsSqlite(db))
            return (await query.ToListAsync(cancellationToken))
                .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
                .OrderByDescending(entry => entry.CreatedAt)
                .ToArray();

        return await query.Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
            .OrderByDescending(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierBalanceEntry>> GetSupplierMovementsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ValidateRange(from, to);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.SupplierBalanceEntries.AsNoTracking()
            .Include(entry => entry.Supplier)
            .Include(entry => entry.User)
            .AsQueryable();
        if (IsSqlite(db))
            return (await query.ToListAsync(cancellationToken))
                .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
                .OrderByDescending(entry => entry.CreatedAt)
                .ToArray();

        return await query.Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
            .OrderByDescending(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<StockAdjustment> WarehouseMovements(CashierDbContext db) =>
        db.StockAdjustments.AsNoTracking()
            .Where(item => item.Type == StockAdjustmentType.WarehouseIn ||
                           item.Type == StockAdjustmentType.WarehouseOut ||
                           item.Type == StockAdjustmentType.TransferToShop);

    private static bool IsSqlite(CashierDbContext db) =>
        string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void ValidatePayment(decimal total, decimal amountPaid, PaymentMethod method)
    {
        if (amountPaid < 0 || amountPaid > total)
            throw new ArgumentOutOfRangeException(nameof(amountPaid), "المبلغ المدفوع يجب أن يكون بين صفر وإجمالي البيع.");
        if (method is not (PaymentMethod.Cash or PaymentMethod.DebitCard or PaymentMethod.CreditCard))
            throw new ArgumentOutOfRangeException(nameof(method), "اختر طريقة تحصيل نقدية أو بطاقة.");
    }

    private static void AdjustCustomerBalance(
        CashierDbContext db,
        Customer? oldCustomer,
        Customer? newCustomer,
        decimal oldRemaining,
        decimal newRemaining,
        int? userId)
    {
        if (oldCustomer?.Id == newCustomer?.Id)
        {
            if (newCustomer is null || oldRemaining == newRemaining)
                return;
            var difference = newRemaining - oldRemaining;
            var newBalance = Money(newCustomer.CurrentBalance + difference);
            if (newBalance < 0)
                throw new InvalidOperationException("لا يمكن تخفيض المديونية بأكثر من رصيد العميل الحالي.");
            newCustomer.CurrentBalance = newBalance;
            AddBalanceAdjustment(db, newCustomer, userId, difference, newBalance);
            return;
        }

        if (oldCustomer is not null && oldRemaining > 0)
        {
            var oldBalance = Money(oldCustomer.CurrentBalance - oldRemaining);
            if (oldBalance < 0)
                throw new InvalidOperationException("لا يمكن تغيير العميل؛ تم سداد جزء من المديونية المرتبطة بالحركة.");
            oldCustomer.CurrentBalance = oldBalance;
            AddBalanceAdjustment(db, oldCustomer, userId, -oldRemaining, oldBalance);
        }
        if (newCustomer is not null && newRemaining > 0)
        {
            newCustomer.CurrentBalance = Money(newCustomer.CurrentBalance + newRemaining);
            AddBalanceAdjustment(db, newCustomer, userId, newRemaining, newCustomer.CurrentBalance);
        }
    }

    private static void AddBalanceAdjustment(
        CashierDbContext db, Customer customer, int? userId, decimal difference, decimal balanceAfter)
    {
        if (difference == 0)
            return;
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            Customer = customer,
            UserId = userId,
            Type = difference > 0 ? CustomerLedgerEntryType.BalanceCharge : CustomerLedgerEntryType.BalancePayment,
            Amount = Math.Abs(difference),
            BalanceAfter = balanceAfter,
            Notes = "تعديل المبلغ المتبقي من بيع الشيت اليومي"
        });
    }

    private static void ValidateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");
    }
}
