using Cashier.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cashier.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddCashierInfrastructure(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        services.AddDbContextFactory<CashierDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IMasterDataService, MasterDataService>();
        services.AddScoped<IStockAdjustmentService, StockAdjustmentService>();
        services.AddScoped<IDailySheetService, DailySheetService>();
        services.AddScoped<IDailyJournalReportService, DailyJournalReportService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseInvoiceService, PurchaseInvoiceService>();
        return services;
    }
}
