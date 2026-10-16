using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class EmployeeService(IDbContextFactory<CashierDbContext> contextFactory) : IEmployeeService
{
    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Employees.AsNoTracking().Include(x => x.User).OrderBy(x => x.FullName).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Employee>> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Employees.AsNoTracking().Include(x => x.User);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Employee>(items, pageNumber, pageSize, totalCount);
    }

    public async Task SaveAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employee.FullName))
            throw new ArgumentException("اسم الموظف مطلوب.");
        if (employee.BasicSalary < 0)
            throw new ArgumentOutOfRangeException(nameof(employee), "لا يمكن أن يكون الراتب سالباً.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (employee.Id == 0)
            db.Employees.Add(employee);
        else
            db.Employees.Update(employee);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.Employees.FindAsync([employeeId], cancellationToken)
            ?? throw new KeyNotFoundException("الموظف غير موجود.");
        employee.IsActive = false;
        if (employee.UserId is not null)
        {
            var user = await db.Users.FindAsync([employee.UserId.Value], cancellationToken);
            if (user is not null)
                user.IsActive = false;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
