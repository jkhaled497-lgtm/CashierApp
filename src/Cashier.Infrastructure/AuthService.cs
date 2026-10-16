using Cashier.Application;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class AuthService(IDbContextFactory<CashierDbContext> contextFactory) : IAuthService
{
    public async Task EnsureInitialAdminAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
            return;
        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        db.Users.Add(new Domain.UserAccount
        {
            Username = "admin",
            Salt = salt,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@12345", salt),
            Role = Domain.UserRole.Admin,
            PasswordChangeRequired = true
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AnyAsync(cancellationToken);
    }

    public async Task SetupInitialAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
            throw new ArgumentException("يجب أن يتكون اسم المستخدم من 3 أحرف على الأقل.");
        if (password.Length < 12)
            throw new ArgumentException("يجب أن تتكون كلمة مرور مدير النظام من 12 حرفاً على الأقل.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
            throw new InvalidOperationException("إنشاء مدير النظام الأول متاح قبل إنشاء أي حساب فقط.");
        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        db.Users.Add(new Domain.UserAccount
        {
            Username = username.Trim(),
            Salt = salt,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, salt),
            Role = Domain.UserRole.Admin
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(false, "أدخل اسم المستخدم وكلمة المرور.", null);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Users.Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Username == username.Trim(), cancellationToken);
        if (account is null || !account.IsActive || !BCrypt.Net.BCrypt.Verify(password, account.PasswordHash))
            return new(false, "اسم المستخدم أو كلمة المرور غير صحيحة، أو أن الحساب غير مفعل.", null);

        account.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new(true, null, new(account.Id, account.Username, account.Role, account.Employee?.Id, account.PasswordChangeRequired));
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (newPassword.Length < 12)
            throw new ArgumentException("يجب أن تتكون كلمة المرور الجديدة من 12 حرفاً على الأقل.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("الحساب غير موجود أو غير مفعل.");
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, account.PasswordHash))
            throw new UnauthorizedAccessException("كلمة المرور الحالية غير صحيحة.");
        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        account.Salt = salt;
        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, salt);
        account.PasswordChangeRequired = false;
        await db.SaveChangesAsync(cancellationToken);
    }
}
