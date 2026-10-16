using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class ProductService(IDbContextFactory<CashierDbContext> contextFactory) : IProductService
{
    public async Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Products.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .SingleOrDefaultAsync(x => x.Id == productId && x.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Brand).Where(x => x.IsActive);
        if (search.CategoryId is not null)
            query = query.Where(x => x.CategoryId == search.CategoryId);
        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var term = $"%{search.Text.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, term) ||
                EF.Functions.Like(x.Sku, term) ||
                (x.Barcode != null && EF.Functions.Like(x.Barcode, term)) ||
                (x.Brand != null && EF.Functions.Like(x.Brand.Name, term)) ||
                (x.Category != null && EF.Functions.Like(x.Category.Name, term)));
        }
        return await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Product>> SearchPageAsync(ProductSearch search, int pageNumber, int pageSize,
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Brand).AsQueryable();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        if (search.CategoryId is not null)
            query = query.Where(x => x.CategoryId == search.CategoryId);
        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            query = query.Where(s => s.Name.ToLower().Contains(search.Text.Trim().ToLower()));
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Product>(items, pageNumber, pageSize, totalCount);
    }

    public async Task SaveAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (string.IsNullOrWhiteSpace(product.Name))
            throw new ArgumentException("اسم المنتج مطلوب.");
        if (product.CostPrice < 0 || product.SellingPrice < 0 || product.CurrentStock < 0 || product.MinStockAlert < 0)
            throw new ArgumentOutOfRangeException(nameof(product), "يجب ألا تكون الأسعار أو قيم المخزون سالبة.");
        if (product.SellingPrice < product.CostPrice)
            throw new ArgumentException("يجب أن يكون سعر البيع أكبر من أو يساوي سعر التكلفة.");
        if (product.CostPrice == 0 || product.SellingPrice == 0)
        {
            if (product.IsActive)
                throw new ArgumentException("سعر التكلفة وسعر البيع مطلوبان للمنتج المفعل.");
        }
        product.Sku = string.IsNullOrWhiteSpace(product.Sku) ? $"SKU-{Guid.NewGuid():N}"[..12] : product.Sku.Trim();
        product.TaxRate = 0;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var duplicate = await db.Products.AnyAsync(x => x.Id != product.Id &&
            (x.Sku == product.Sku || (product.Barcode != null && x.Barcode == product.Barcode.Trim())), cancellationToken);
        if (duplicate)
            throw new InvalidOperationException("يوجد منتج آخر بالرمز أو الباركود نفسه.");
        if (product.Id == 0)
        {
            db.Products.Add(product);
            if (product.CurrentStock > 0)
                db.StockAdjustments.Add(new StockAdjustment
                {
                    Product = product,
                    Type = StockAdjustmentType.StockIn,
                    Quantity = product.CurrentStock,
                    Reason = "رصيد افتتاحي للمنتج"
                });
        }

        else
        {
            var existing = await db.Products.SingleOrDefaultAsync(x => x.Id == product.Id, cancellationToken)
                ?? throw new KeyNotFoundException("المنتج غير موجود.");
            var stockDifference = product.CurrentStock - existing.CurrentStock;
            existing.Sku = product.Sku.Trim();
            existing.Barcode = string.IsNullOrWhiteSpace(product.Barcode) ? null : product.Barcode.Trim();
            existing.Name = product.Name.Trim();
            existing.Description = product.Description;
            existing.CategoryId = product.CategoryId;
            existing.BrandId = product.BrandId;
            existing.CostPrice = product.CostPrice;
            existing.SellingPrice = product.SellingPrice;
            existing.MinStockAlert = product.MinStockAlert;
            existing.CurrentStock = product.CurrentStock;
            existing.TaxRate = product.TaxRate;
            existing.Unit = product.Unit;
            existing.ImagePath = product.ImagePath;
            existing.IsActive = product.IsActive;
            if (stockDifference != 0)
                db.StockAdjustments.Add(new StockAdjustment
                {
                    ProductId = existing.Id,
                    Type = stockDifference > 0 ? StockAdjustmentType.StockIn : StockAdjustmentType.StockOut,
                    Quantity = Math.Abs(stockDifference),
                    Reason = "تعديل الرصيد من شاشة المنتجات"
                });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.FindAsync([productId], cancellationToken)
            ?? throw new KeyNotFoundException("المنتج غير موجود.");
        if (await db.InvoiceLines.AnyAsync(x => x.ProductId == productId, cancellationToken) ||
            await db.StockAdjustments.AnyAsync(x => x.ProductId == productId, cancellationToken))
        {
            product.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
