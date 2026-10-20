using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class StockAdjustmentService(IDbContextFactory<CashierDbContext> contextFactory) : IStockAdjustmentService
{
    public async Task<IReadOnlyList<StockAdjustment>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StockAdjustments.AsNoTracking()
            .Where(x => x.Type == StockAdjustmentType.WarehouseIn ||
                        x.Type == StockAdjustmentType.WarehouseOut ||
                        x.Type == StockAdjustmentType.TransferToShop)
            .Include(x => x.Product).Include(x => x.Employee)
            .OrderByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<StockAdjustment>> GetRecentPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.StockAdjustments.AsNoTracking()
            .Where(x => x.Type == StockAdjustmentType.WarehouseIn ||
                        x.Type == StockAdjustmentType.WarehouseOut ||
                        x.Type == StockAdjustmentType.TransferToShop)
            .Include(x => x.Product).Include(x => x.Employee);
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
        switch (type)
        {
            case StockAdjustmentType.WarehouseIn:
                product.WarehouseStock += quantity;
                break;
            case StockAdjustmentType.TransferToShop:
                product.WarehouseStock -= quantity;
                product.CurrentStock += quantity;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), "نوع حركة المخزون غير صالح.");
        }
        if (product.CurrentStock < 0 || product.WarehouseStock < 0)
            throw new InvalidOperationException("الكمية المتاحة لا تكفي لإتمام حركة المخزون.");
        db.StockAdjustments.Add(new StockAdjustment
        {
            ProductId = productId, Type = type, Quantity = quantity,
            EmployeeId = employeeId, Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        long adjustmentId, decimal quantity, string? reason, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "يجب أن تكون كمية الحركة أكبر من صفر.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var adjustment = await db.StockAdjustments.Include(item => item.Product)
            .SingleOrDefaultAsync(item => item.Id == adjustmentId, cancellationToken)
            ?? throw new KeyNotFoundException("حركة المخزن غير موجودة.");
        var product = adjustment.Product;
        var difference = quantity - adjustment.Quantity;

        switch (adjustment.Type)
        {
            case StockAdjustmentType.WarehouseIn:
                product.WarehouseStock += difference;
                break;
            case StockAdjustmentType.TransferToShop:
                product.WarehouseStock -= difference;
                product.CurrentStock += difference;
                break;
            default:
                throw new InvalidOperationException("يمكن تعديل حركات المخزن الواردة أو التحويل إلى المحل فقط.");
        }

        if (product.WarehouseStock < 0 || product.CurrentStock < 0)
            throw new InvalidOperationException("تعديل الحركة سيؤدي إلى رصيد مخزون سالب.");
        adjustment.Quantity = quantity;
        adjustment.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
