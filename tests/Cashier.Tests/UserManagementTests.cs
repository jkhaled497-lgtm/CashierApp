using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Tests;

public sealed class UserManagementTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesAccountRoleEmployeeAndRequiresPasswordChange()
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
        await service.CreateAsync("cashier", "InitialSecure@2026", UserRole.Cashier, employeeId);
        var account = (await service.GetAllAsync()).Single();
        await service.UpdateAsync(account.Id, "manager", UserRole.Manager, employeeId, "UpdatedSecure@2026", true);

        await using var verify = await factory.CreateDbContextAsync();
        var updated = await verify.Users.Include(x => x.Employee).SingleAsync();
        Assert.Equal("manager", updated.Username);
        Assert.Equal(UserRole.Manager, updated.Role);
        Assert.True(updated.PasswordChangeRequired);
        Assert.Equal(employeeId, updated.Employee!.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("UpdatedSecure@2026", updated.PasswordHash));
    }

    private sealed class TestContextFactory(DbContextOptions<CashierDbContext> options) : IDbContextFactory<CashierDbContext>
    {
        public CashierDbContext CreateDbContext() => new(options);

        public Task<CashierDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashierDbContext(options));
    }
}
