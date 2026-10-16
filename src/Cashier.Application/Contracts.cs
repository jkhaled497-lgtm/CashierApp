using Cashier.Domain;

namespace Cashier.Application;

public sealed record UserSession(int UserId, string Username, UserRole Role, int? EmployeeId, bool PasswordChangeRequired = false)
{
    public bool CanApplyGlobalDiscount => Role is UserRole.Admin or UserRole.Manager;
    public bool CanManageCatalog => Role is UserRole.Admin or UserRole.Manager;
}

public sealed record ProductSearch(string? Text = null, int? CategoryId = null);
public sealed record PageResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
public sealed record CheckoutLine(int ProductId, decimal Quantity, decimal DiscountPercent = 0, decimal DiscountFixed = 0);
public sealed record PaymentTender(PaymentMethod Method, decimal Amount, string? Reference = null);
public sealed record CheckoutRequest(
    int? CustomerId,
    IReadOnlyList<CheckoutLine> Items,
    decimal GlobalDiscountPercent,
    decimal GlobalDiscountFixed,
    IReadOnlyList<PaymentTender> Payments,
    int UserId,
    int? EmployeeId);
public sealed record InvoiceDailyReport(
    DateTime Date,
    int InvoiceCount,
    decimal SalesTotal,
    decimal CashReceived,
    decimal CardReceived,
    decimal CustomerBalance,
    decimal CostTotal,
    decimal ProfitTotal);
public sealed record InvoiceRevenueReport(DateTime Date, decimal CashReceived, decimal CardReceived)
{
    public decimal TotalReceived => CashReceived + CardReceived;
}
public sealed record CustomerAccountReport(
    string CustomerCode,
    string CustomerName,
    string? Phone,
    decimal CurrentBalance,
    decimal PeriodCharges,
    decimal PeriodPayments);
public sealed record SupplierAccountReport(
    string SupplierName,
    string? Phone,
    decimal CurrentBalance,
    decimal PeriodCharges,
    decimal PeriodPayments);
public sealed record InvoiceLookupRow(
    long Id,
    string InvoiceNumber,
    DateTimeOffset CreatedAt,
    string CustomerName,
    decimal GrandTotal,
    InvoiceStatus Status);
public sealed record PurchaseLineRequest(int ProductId, decimal UnitPrice, decimal Quantity, decimal DiscountAmount);
public sealed record CreatePurchaseInvoiceRequest(
    int SupplierId,
    IReadOnlyList<PurchaseLineRequest> Items,
    decimal PaymentAmount,
    PaymentMethod PaymentMethod,
    int UserId);
public sealed record PurchaseInvoicePaymentRequest(
    long InvoiceId, decimal Amount, PaymentMethod Method, int UserId);
public sealed record PurchaseInvoiceLookupRow(
    long Id,
    string InvoiceNumber,
    DateTimeOffset CreatedAt,
    string SupplierName,
    decimal GrandTotal,
    decimal TotalPaid,
    PurchaseInvoiceStatus Status);
public sealed record PurchaseSupplierStatistics(
    int InvoiceCount,
    decimal TotalPurchases,
    decimal TotalPaid,
    decimal OutstandingAmount,
    decimal SupplierBalance);
public sealed record LoginResult(bool Succeeded, string? Error, UserSession? Session);

public interface IAuthService
{
    Task EnsureInitialAdminAsync(CancellationToken cancellationToken = default);
    Task<bool> HasAccountsAsync(CancellationToken cancellationToken = default);
    Task SetupInitialAdminAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

public interface IProductService
{
    Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken = default);
    Task<PageResult<Product>> SearchPageAsync(ProductSearch search, int pageNumber, int pageSize, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task SaveAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(int productId, CancellationToken cancellationToken = default);
}

public interface ICustomerService
{
    Task<IReadOnlyList<Customer>> SearchAsync(string? text, CancellationToken cancellationToken = default);
    Task<PageResult<Customer>> SearchPageAsync(string? text, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task SaveAsync(Customer customer, CancellationToken cancellationToken = default);
    Task DeactivateAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerLedgerEntry>> GetLedgerAsync(int customerId, CancellationToken cancellationToken = default);
    Task<PageResult<CustomerLedgerEntry>> GetLedgerPageAsync(int customerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerAccountReport>> GetAccountReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<Customer> RecordBalancePaymentAsync(int customerId, decimal amount, int userId, PaymentMethod method, string? note, CancellationToken cancellationToken = default);
}

public interface IEmployeeService
{
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PageResult<Employee>> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task SaveAsync(Employee employee, CancellationToken cancellationToken = default);
    Task DeactivateAsync(int employeeId, CancellationToken cancellationToken = default);
}

public interface IUserManagementService
{
    Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PageResult<UserAccount>> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task CreateAsync(string username, string password, UserRole role, int? employeeId, CancellationToken cancellationToken = default);
    Task UpdateAsync(int userId, string username, UserRole role, int? employeeId, string? newPassword, bool isActive, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken = default);
}

public interface IShiftService
{
    Task<CashierShift?> GetOpenAsync(int userId, CancellationToken cancellationToken = default);
    Task<CashierShift> StartAsync(int userId, decimal openingCash, CancellationToken cancellationToken = default);
    Task<CashierShift> EndAsync(int userId, decimal closingCash, CancellationToken cancellationToken = default);
}

public interface IMasterDataService
{
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Brand>> GetBrandsAsync(CancellationToken cancellationToken = default);
    Task<PageResult<Category>> GetCategoryPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PageResult<Brand>> GetBrandPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task SaveCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task SaveBrandAsync(Brand brand, CancellationToken cancellationToken = default);
    Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task DeactivateBrandAsync(int brandId, CancellationToken cancellationToken = default);
}

public interface IStockAdjustmentService
{
    Task<IReadOnlyList<StockAdjustment>> GetRecentAsync(CancellationToken cancellationToken = default);
    Task<PageResult<StockAdjustment>> GetRecentPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task AdjustAsync(int productId, StockAdjustmentType type, decimal quantity, int? employeeId, string? reason, CancellationToken cancellationToken = default);
}

public interface ISupplierService
{
    Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Supplier>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<PageResult<Supplier>> GetPageAsync(string? search, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PageResult<SupplierBalanceEntry>> GetLedgerPageAsync(int supplierId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierAccountReport>> GetAccountReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task SaveAsync(Supplier supplier, decimal openingBalance, int? userId, CancellationToken cancellationToken = default);
    Task ChangeBalanceAsync(int supplierId, decimal amount, SupplierBalanceEntryType type, int? userId,
        PaymentMethod paymentMethod, string? note, CancellationToken cancellationToken = default);
    Task DeleteAsync(int supplierId, CancellationToken cancellationToken = default);
}

public interface IPurchaseInvoiceService
{
    Task<PurchaseInvoice> CreateAsync(CreatePurchaseInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice> RecordPaymentAsync(
        PurchaseInvoicePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PageResult<PurchaseInvoiceLookupRow>> GetPageAsync(
        int supplierId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PurchaseInvoice?> GetByIdAsync(long invoiceId, CancellationToken cancellationToken = default);
    Task<PurchaseSupplierStatistics> GetStatisticsAsync(int supplierId, CancellationToken cancellationToken = default);
}

public interface IPurchaseBillPrintService
{
    void Print(PurchaseInvoice invoice);
}

public interface IInvoiceService
{
    Task<Invoice> CompleteSaleAsync(CheckoutRequest request, CancellationToken cancellationToken = default);
    Task<PageResult<InvoiceLookupRow>> SearchPageAsync(
        string? search, int pageNumber, int pageSize,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
    Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceDailyReport>> GetDailyReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceRevenueReport>> GetRevenueReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public interface IPrintService
{
    Task PrintReceiptAsync(Invoice invoice, CancellationToken cancellationToken = default);
}

public sealed class SessionManager
{
    public UserSession? Current { get; private set; }
    public event EventHandler? SessionChanged;

    public void SignIn(UserSession session)
    {
        Current = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        Current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
