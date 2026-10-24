using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class UserManagementTests
{
    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Manager, false)]
    [InlineData(UserRole.Cashier, false)]
    public void CanManageCatalog_IsRestrictedToAdmin(UserRole role, bool expected)
    {
        var session = new UserSession(1, "user", role, null);

        Assert.Equal(expected, session.CanManageCatalog);
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_SeedsDefaultAdminWithoutRequiringPasswordChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        var auth = new AuthService(factory);
        await auth.EnsureInitialAdminAsync();
        await auth.EnsureInitialAdminAsync();

        await using var verify = await factory.CreateDbContextAsync();
        var admin = await verify.Users.SingleAsync();
        Assert.Equal("admin", admin.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.False(admin.PasswordChangeRequired);
        Assert.True(BCrypt.Net.BCrypt.Verify("admin1234", admin.PasswordHash));
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_AddsAdminWhenDatabaseContainsOnlyNonAdminAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserAccount
            {
                Username = "cashier",
                PasswordHash = "hash",
                Salt = "salt",
                Role = UserRole.Cashier
            });
            await db.SaveChangesAsync();
        }

        await new AuthService(factory).EnsureInitialAdminAsync();

        await using var verify = await factory.CreateDbContextAsync();
        var admin = await verify.Users.SingleAsync(user => user.Role == UserRole.Admin);
        Assert.Equal("admin", admin.Username);
        Assert.False(admin.PasswordChangeRequired);
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_DoesNotPromoteExistingAccountNamedAdmin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserAccount
            {
                Username = "admin",
                PasswordHash = "hash",
                Salt = "salt",
                Role = UserRole.Cashier
            });
            await db.SaveChangesAsync();
        }

        await new AuthService(factory).EnsureInitialAdminAsync();

        await using var verify = await factory.CreateDbContextAsync();
        var accounts = await verify.Users.OrderBy(user => user.Id).ToListAsync();
        Assert.Equal(UserRole.Cashier, accounts[0].Role);
        Assert.Equal("admin-setup", accounts[1].Username);
        Assert.Equal(UserRole.Admin, accounts[1].Role);
        Assert.False(accounts[1].PasswordChangeRequired);
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_UpdatesOldUnchangedSeedPassword()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        var salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new UserAccount
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@12345", salt),
                Salt = salt,
                Role = UserRole.Admin,
                PasswordChangeRequired = true
            });
            await db.SaveChangesAsync();
        }

        await new AuthService(factory).EnsureInitialAdminAsync();

        await using var verify = await factory.CreateDbContextAsync();
        var admin = await verify.Users.SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify("admin1234", admin.PasswordHash));
        Assert.False(admin.PasswordChangeRequired);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAccountRoleEmployeeWithoutForcingPasswordChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CashierDbContext>().UseSqlite(connection).Options;
        var factory = new TestContextFactory(options);
        int employeeId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            var employee = new Employee { FullName = "موظف اختبار" };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            employeeId = employee.Id;
        }

        var service = new UserManagementService(factory);
        await service.CreateAsync("cashier", "x", UserRole.Cashier, employeeId);
        var account = (await service.GetAllAsync()).Single();
        await service.UpdateAsync(account.Id, "manager", UserRole.Manager, employeeId, "y", true);

        await using var verify = await factory.CreateDbContextAsync();
        var updated = await verify.Users.Include(x => x.Employee).SingleAsync();
        Assert.Equal("manager", updated.Username);
        Assert.Equal(UserRole.Manager, updated.Role);
        Assert.False(updated.PasswordChangeRequired);
        Assert.Equal(employeeId, updated.Employee!.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("y", updated.PasswordHash));
    }

    private sealed class TestContextFactory(DbContextOptions<CashierDbContext> options) : IDbContextFactory<CashierDbContext>
    {
        public CashierDbContext CreateDbContext() => new(options);

        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
    }
}
