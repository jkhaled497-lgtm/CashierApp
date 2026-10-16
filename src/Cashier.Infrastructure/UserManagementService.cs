using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class UserManagementService(IDbContextFactory<CashierDbContext> contextFactory) : IUserManagementService
{
    public async Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<UserAccount>> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Users.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Username).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<UserAccount>(items, pageNumber, pageSize, totalCount);
    }

    public async Task CreateAsync(string username, string password, UserRole role, int? employeeId, CancellationToken cancellationToken = default)
    {
        ValidateUsername(username);
        ValidatePassword(password);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(x => x.Username == username.Trim(), cancellationToken))
            throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.");
        if (employeeId is not null && !await db.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("اختر موظفاً نشطاً لربطه بهذا الحساب.");
        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        db.Users.Add(new UserAccount
        {
            Username = username.Trim(), Salt = salt, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, salt),
            Role = role, Employee = employeeId is null ? null : await db.Employees.FindAsync([employeeId.Value], cancellationToken)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(int userId, string username, UserRole role, int? employeeId,
        string? newPassword, bool isActive, CancellationToken cancellationToken = default)
    {
        ValidateUsername(username);
        if (!string.IsNullOrEmpty(newPassword))
            ValidatePassword(newPassword);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Users.Include(x => x.Employee).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("حساب المستخدم غير موجود.");
        var normalizedUsername = username.Trim();
        if (await db.Users.AnyAsync(x => x.Id != userId && x.Username == normalizedUsername, cancellationToken))
            throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.");
        if (employeeId is not null && !await db.Employees.AnyAsync(
                x => x.Id == employeeId && x.IsActive && (x.UserId == null || x.UserId == userId), cancellationToken))
            throw new InvalidOperationException("الموظف المختار غير نشط أو مرتبط بحساب آخر.");

        var currentEmployee = account.Employee;
        if (currentEmployee is not null && currentEmployee.Id != employeeId)
            currentEmployee.UserId = null;
        if (employeeId is not null)
        {
            var employee = await db.Employees.FindAsync([employeeId.Value], cancellationToken)
                ?? throw new KeyNotFoundException("الموظف غير موجود.");
            employee.UserId = userId;
        }

        account.Username = normalizedUsername;
        account.Role = role;
        account.IsActive = isActive;
        if (!string.IsNullOrEmpty(newPassword))
        {
            account.Salt = BCrypt.Net.BCrypt.GenerateSalt(12);
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, account.Salt);
            account.PasswordChangeRequired = true;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Users.FindAsync([userId], cancellationToken)
            ?? throw new KeyNotFoundException("حساب المستخدم غير موجود.");
        account.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
            throw new ArgumentException("يجب أن يتكون اسم المستخدم من 3 أحرف على الأقل.");
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12)
            throw new ArgumentException("يجب أن تتكون كلمة المرور من 12 حرفاً على الأقل.");
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
