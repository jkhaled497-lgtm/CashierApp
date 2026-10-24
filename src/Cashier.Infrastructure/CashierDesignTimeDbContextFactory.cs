using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cashier.Infrastructure;

public sealed class CashierDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CashierDbContext>
{
    public CashierDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(AppContext.BaseDirectory, "cashier.db");
        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new CashierDbContext(options);
    }
}
