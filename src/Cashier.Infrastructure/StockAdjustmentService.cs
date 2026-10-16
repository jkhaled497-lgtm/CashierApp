using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class StockAdjustmentService(IDbContextFactory<CashierDbContext> contextFactory) : IStockAdjustmentService
{
    public async Task<IReadOnlyList<StockAdjustment>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StockAdjustments.AsNoTracking().Include(x => x.Product).Include(x => x.Employee)
            .OrderByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<StockAdjustment>> GetRecentPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.StockAdjustments.AsNoTracking().Include(x => x.Product).Include(x => x.Employee);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<StockAdjustment>(items, pageNumber, pageSize, count);
    }

    public async Task AdjustAsync(int productId, StockAdjustmentType type, decimal quantity, int? employeeId, string? reason, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "يجب أن تكون كمية الحركة أكبر من صفر.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("المنتج النشط غير موجود.");
        var signedChange = type == StockAdjustmentType.StockIn ? quantity : -quantity;
        if (product.CurrentStock + signedChange < 0)
            throw new InvalidOperationException("ستؤدي هذه الحركة إلى جعل المخزون سالباً.");
        product.CurrentStock += signedChange;
        db.StockAdjustments.Add(new StockAdjustment
        {
            ProductId = productId, Type = type, Quantity = quantity,
            EmployeeId = employeeId, Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
