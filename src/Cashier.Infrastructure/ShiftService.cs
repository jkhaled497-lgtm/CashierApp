using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class ShiftService(IDbContextFactory<CashierDbContext> contextFactory) : IShiftService
{
    public async Task<CashierShift?> GetOpenAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CashierShifts.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null, cancellationToken);
    }

    public async Task<CashierShift> StartAsync(int userId, decimal openingCash, CancellationToken cancellationToken = default)
    {
        if (openingCash < 0)
            throw new ArgumentOutOfRangeException(nameof(openingCash), "لا يمكن أن يكون رصيد النقدية الافتتاحي سالباً.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Users.Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("حساب المستخدم النشط غير موجود.");
        if (account.Employee is not { IsActive: true } employee)
            throw new InvalidOperationException("اربط الحساب بسجل موظف نشط قبل بدء الوردية.");
        if (await db.CashierShifts.AnyAsync(x => x.UserId == userId && x.EndedAt == null, cancellationToken))
            throw new InvalidOperationException("لدى هذا المستخدم وردية مفتوحة بالفعل.");
        var shift = new CashierShift
        {
            UserId = userId,
            EmployeeId = employee.Id,
            Employee = employee,
            OpeningCash = decimal.Round(openingCash, 2, MidpointRounding.AwayFromZero)
        };
        db.CashierShifts.Add(shift);
        await db.SaveChangesAsync(cancellationToken);
        return shift;
    }

    public async Task<CashierShift> EndAsync(int userId, decimal closingCash, CancellationToken cancellationToken = default)
    {
        if (closingCash < 0)
            throw new ArgumentOutOfRangeException(nameof(closingCash), "لا يمكن أن يكون رصيد النقدية الختامي سالباً.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null, cancellationToken)
            ?? throw new InvalidOperationException("لا توجد وردية مفتوحة لإغلاقها.");
        shift.EndedAt = DateTimeOffset.UtcNow;
        shift.ClosingCash = decimal.Round(closingCash, 2, MidpointRounding.AwayFromZero);
        await db.SaveChangesAsync(cancellationToken);
        return shift;
    }
}
