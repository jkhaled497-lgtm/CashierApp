using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cashier.Infrastructure;

public sealed class CashierDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CashierDbContext>
{
    public CashierDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CASHIER_CONNECTION_STRING")
            ?? ReadConfiguredConnectionString()
            ?? throw new InvalidOperationException(
                "Set CASHIER_CONNECTION_STRING or provide src/Cashier.UI/appsettings.json with ConnectionStrings:Cashier.");

        var options = new DbContextOptionsBuilder<CashierDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new CashierDbContext(options);
    }

    private static string? ReadConfiguredConnectionString()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var settingsPath = Path.Combine(directory.FullName, "src", "Cashier.UI", "appsettings.json");
            if (!File.Exists(settingsPath))
                continue;

            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (settings.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
                connectionStrings.TryGetProperty("Cashier", out var connectionString))
                return connectionString.GetString();
        }

        return null;
    }
}
