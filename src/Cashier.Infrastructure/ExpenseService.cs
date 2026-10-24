using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class ExpenseService(IDbContextFactory<CashierDbContext> contextFactory) : IExpenseService
{
    public async Task<BusinessExpense> CreateAsync(
        CreateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = request.Title.Trim();
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (title.Length is < 1 or > 180)
            throw new ArgumentException("اسم المصروف مطلوب وبحد أقصى 180 حرفاً.", nameof(request));
        if (request.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "يجب أن تكون قيمة المصروف أكبر من صفر.");
        if (notes?.Length > 512)
            throw new ArgumentException("الملاحظات يجب ألا تتجاوز 512 حرفاً.", nameof(request));
        if (request.Date == default)
            throw new ArgumentException("تاريخ المصروف مطلوب.", nameof(request));
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "يجب أن تكون قيمة المصروف أكبر من صفر بعد التقريب.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var role = await db.Users.AsNoTracking()
            .Where(user => user.Id == request.UserId && user.IsActive)
            .Select(user => (UserRole?)user.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not (UserRole.Admin or UserRole.Manager))
            throw new UnauthorizedAccessException("تسجيل المصروفات يتطلب صلاحية المدير أو مدير النظام.");

        var expense = new BusinessExpense
        {
            Title = title,
            Amount = amount,
            Notes = notes,
            CreatedAt = BusinessTime.ToUtc(request.Date.Date),
            UserId = request.UserId
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        return expense;
    }

    public async Task<IReadOnlyList<BusinessExpense>> GetForPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from >= to)
            throw new ArgumentOutOfRangeException(nameof(from), "نطاق التاريخ غير صالح.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Expenses.AsNoTracking()
            .Include(expense => expense.User)
            .AsQueryable();

        if (string.Equals(
                db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return (await query.ToListAsync(cancellationToken))
                .Where(expense => expense.CreatedAt >= from && expense.CreatedAt < to)
                .OrderByDescending(expense => expense.CreatedAt)
                .ThenByDescending(expense => expense.Id)
                .ToArray();
        }

        return await query
            .Where(expense => expense.CreatedAt >= from && expense.CreatedAt < to)
            .OrderByDescending(expense => expense.CreatedAt)
            .ThenByDescending(expense => expense.Id)
            .ToListAsync(cancellationToken);
    }
}
