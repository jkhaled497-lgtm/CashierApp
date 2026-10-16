using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class MasterDataService(IDbContextFactory<CashierDbContext> contextFactory) : IMasterDataService
{
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Brand>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Brands.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Category>> GetCategoryPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Categories.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Category>(items, pageNumber, pageSize, count);
    }

    public async Task<PageResult<Brand>> GetBrandPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Brands.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Brand>(items, pageNumber, pageSize, count);
    }

    public async Task SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
            throw new ArgumentException("اسم الفئة مطلوب.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (category.Id == 0)
        {
            category.Code = $"CAT-{Guid.NewGuid():N}"[..12];
            db.Categories.Add(category);
        }
        else
        {
            var existing = await db.Categories.FindAsync([category.Id], cancellationToken)
                ?? throw new KeyNotFoundException("الفئة غير موجودة.");
            existing.Name = category.Name.Trim();
            existing.Description = category.Description;
            existing.ColorHex = category.ColorHex;
            existing.Icon = category.Icon;
            existing.IsActive = category.IsActive;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveBrandAsync(Brand brand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brand.Name))
            throw new ArgumentException("اسم العلامة التجارية مطلوب.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (brand.Id == 0) db.Brands.Add(brand);
        else db.Brands.Update(brand);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FindAsync([categoryId], cancellationToken)
            ?? throw new KeyNotFoundException("الفئة غير موجودة.");
        if (await db.Products.AnyAsync(x => x.CategoryId == categoryId, cancellationToken))
            category.IsActive = false;
        else
            db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateBrandAsync(int brandId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var brand = await db.Brands.FindAsync([brandId], cancellationToken)
            ?? throw new KeyNotFoundException("العلامة التجارية غير موجودة.");
        if (await db.Products.AnyAsync(x => x.BrandId == brandId, cancellationToken))
            brand.IsActive = false;
        else
            db.Brands.Remove(brand);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
