using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class SupplierService(IDbContextFactory<CashierDbContext> contextFactory) : ISupplierService
{
    public async Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Suppliers.AsNoTracking()
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Suppliers.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Supplier>> GetPageAsync(string? search, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Suppliers.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Name, term) ||
                                     (x.Phone != null && EF.Functions.Like(x.Phone, term)) ||
                                     (x.Email != null && EF.Functions.Like(x.Email, term)));
        }
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Supplier>(items, pageNumber, pageSize, count);
    }

    public async Task<PageResult<SupplierBalanceEntry>> GetLedgerPageAsync(int supplierId, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.SupplierBalanceEntries.AsNoTracking()
            .Where(x => x.SupplierId == supplierId);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<SupplierBalanceEntry>(items, pageNumber, pageSize, count);
    }

    public async Task<IReadOnlyList<SupplierAccountReport>> GetAccountReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Suppliers.AsNoTracking()
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(supplier => new
            {
                supplier.Id,
                supplier.Name,
                supplier.Phone,
                supplier.CurrentBalance
            })
            .ToListAsync(cancellationToken);
        var movements = (await db.SupplierBalanceEntries.AsNoTracking()
            .ToListAsync(cancellationToken))
            .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to)
            .Select(entry => new { entry.SupplierId, entry.Type, entry.Amount })
            .ToList();
        var movementsBySupplier = movements.GroupBy(entry => entry.SupplierId)
            .ToDictionary(group => group.Key, group => (
                Charges: group.Where(entry => entry.Type is SupplierBalanceEntryType.Charge or SupplierBalanceEntryType.OpeningBalance)
                    .Sum(entry => entry.Amount),
                Payments: group.Where(entry => entry.Type == SupplierBalanceEntryType.Payment).Sum(entry => entry.Amount)));

        return accounts.Select(account =>
        {
            var movement = movementsBySupplier.GetValueOrDefault(account.Id);
            return new SupplierAccountReport(
                account.Name,
                account.Phone,
                account.CurrentBalance,
                movement.Charges,
                movement.Payments);
        }).ToArray();
    }

    public async Task SaveAsync(Supplier supplier, decimal openingBalance, int? userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        if (string.IsNullOrWhiteSpace(supplier.Name))
            throw new ArgumentException("اسم المورد مطلوب.");
        openingBalance = decimal.Round(openingBalance, 2, MidpointRounding.AwayFromZero);
        if (openingBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(openingBalance), "الرصيد الافتتاحي لا يمكن أن يكون سالباً.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (supplier.Id == 0)
        {
            supplier.Name = supplier.Name.Trim();
            supplier.CurrentBalance = openingBalance;
            db.Suppliers.Add(supplier);
            if (openingBalance > 0)
                db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
                {
                    Supplier = supplier,
                    UserId = userId,
                    Type = SupplierBalanceEntryType.OpeningBalance,
                    Amount = openingBalance,
                    BalanceAfter = openingBalance,
                    Notes = "رصيد افتتاحي عند إضافة المورد"
                });
        }
        else
        {
            var existing = await db.Suppliers.FindAsync([supplier.Id], cancellationToken)
                ?? throw new KeyNotFoundException("المورد غير موجود.");
            existing.Name = supplier.Name.Trim();
            existing.Phone = supplier.Phone;
            existing.Email = supplier.Email;
            existing.Address = supplier.Address;
            existing.TaxNumber = supplier.TaxNumber;
            existing.Notes = supplier.Notes;
            existing.IsActive = supplier.IsActive;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangeBalanceAsync(int supplierId, decimal amount, SupplierBalanceEntryType type, int? userId,
        PaymentMethod paymentMethod, string? note, CancellationToken cancellationToken = default)
    {
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "يجب أن يكون المبلغ أكبر من صفر.");
        if (type is not (SupplierBalanceEntryType.Charge or SupplierBalanceEntryType.Payment))
            throw new ArgumentOutOfRangeException(nameof(type), "نوع حركة رصيد المورد غير صالح.");
        if (paymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ArgumentOutOfRangeException(nameof(paymentMethod), "اختر نقداً أو بطاقة ائتمان.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(
            x => x.Id == supplierId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("المورد غير موجود أو غير مفعل.");
        if (type == SupplierBalanceEntryType.Payment && amount > supplier.CurrentBalance)
            throw new InvalidOperationException("قيمة التسديد أكبر من المستحق للمورد.");

        supplier.CurrentBalance = decimal.Round(
            type == SupplierBalanceEntryType.Charge
                ? supplier.CurrentBalance + amount
                : supplier.CurrentBalance - amount,
            2,
            MidpointRounding.AwayFromZero);
        db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
        {
            Supplier = supplier,
            UserId = userId,
            Type = type,
            Amount = amount,
            BalanceAfter = supplier.CurrentBalance,
            PaymentMethod = paymentMethod,
            Notes = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var supplier = await db.Suppliers.FindAsync([supplierId], cancellationToken)
            ?? throw new KeyNotFoundException("المورد غير موجود.");
        if (supplier.CurrentBalance != 0)
            throw new InvalidOperationException("لا يمكن حذف مورد لديه مبلغ مستحق؛ سجل التسديد أولاً.");
        if (await db.SupplierBalanceEntries.AnyAsync(x => x.SupplierId == supplierId, cancellationToken))
            throw new InvalidOperationException("لا يمكن حذف مورد لديه سجل مالي؛ احتفظ به لأغراض المراجعة.");
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
