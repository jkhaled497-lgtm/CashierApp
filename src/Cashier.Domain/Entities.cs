namespace Cashier.Domain;

public enum UserRole { Admin, Manager, Cashier }
public enum ProductUnit { Piece, Kg, Box }
public enum StockAdjustmentType { StockIn, StockOut, Damaged, Expired, WarehouseIn, WarehouseOut, TransferToShop }
public enum PaymentMethod { Cash, DebitCard, CreditCard, ClientAccount }
public enum InvoiceStatus { Paid, Partial, Refunded, Void }
public enum SupplierBalanceEntryType { OpeningBalance, Charge, Payment }
public enum PurchaseInvoiceStatus { Unpaid, Partial, Paid }
public enum DailyJournalType
{
    SalesInvoice,
    PurchaseInvoice,
    DailySale,
    WarehouseEntry,
    WarehouseTransfer,
    WarehouseExit,
    CustomerCharge,
    CustomerPayment,
    SupplierCharge,
    SupplierPayment,
    Expense
}

public sealed class UserAccount
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Salt { get; set; }
    public UserRole Role { get; set; } = UserRole.Cashier;
    public bool IsActive { get; set; } = true;
    public bool PasswordChangeRequired { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class Employee
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public UserAccount? User { get; set; }
    public required string FullName { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? JobTitle { get; set; }
    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal BasicSalary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Customer
{
    public int Id { get; set; }
    public required string CustomerCode { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public int LoyaltyPoints { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsWalkIn { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<CustomerLedgerEntry> LedgerEntries { get; set; } = new List<CustomerLedgerEntry>();
}

public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? Description { get; set; }
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class Brand
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? LogoPath { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class Supplier
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public string? Notes { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<SupplierBalanceEntry> BalanceEntries { get; set; } = new List<SupplierBalanceEntry>();
    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();
}

public sealed class SupplierBalanceEntry
{
    public long Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public int? UserId { get; set; }
    public UserAccount? User { get; set; }
    public SupplierBalanceEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Product
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public string? Barcode { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal MinStockAlert { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal WarehouseStock { get; set; }
    public decimal TaxRate { get; set; }
    public ProductUnit Unit { get; set; } = ProductUnit.Piece;
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLowStock => CurrentStock <= MinStockAlert;
}

public sealed class StockAdjustment
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public StockAdjustmentType Type { get; set; }
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DailyProduct
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string ProductName { get; set; }
    public required string Sku { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitSellingPrice { get; set; }
    public decimal? UnitCostPrice { get; set; }
    public decimal AmountPaid { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal TotalAmount => Math.Round(
        (UnitSellingPrice ?? Product?.SellingPrice ?? 0) * Quantity,
        2,
        MidpointRounding.AwayFromZero);
    public decimal RemainingAmount => Math.Max(0, TotalAmount - AmountPaid);
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? UserId { get; set; }
    public UserAccount? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BusinessExpense
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? UserId { get; set; }
    public UserAccount? User { get; set; }
}

public sealed class PurchaseInvoice
{
    public long Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public int? UserId { get; set; }
    public UserAccount? CreatedByUser { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Unpaid;
    public ICollection<PurchaseInvoiceLine> Items { get; set; } = new List<PurchaseInvoiceLine>();
    public ICollection<PurchasePayment> Payments { get; set; } = new List<PurchasePayment>();
}

public sealed class PurchaseInvoiceLine
{
    public long Id { get; set; }
    public long PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string ProductName { get; set; }
    public required string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class PurchasePayment
{
    public long Id { get; set; }
    public long PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PurchaseInvoiceSequence
{
    public int Id { get; set; }
    public int LastNumber { get; set; }
}

public sealed class Invoice
{
    public long Id { get; set; }
    public Guid InvoiceGuid { get; set; } = Guid.NewGuid();
    public required string InvoiceNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? CashierEmployeeId { get; set; }
    public Employee? CashierEmployee { get; set; }
    public int? UserId { get; set; }
    public UserAccount? CreatedByUser { get; set; }
    public long? ShiftId { get; set; }
    public CashierShift? Shift { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Rounding { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal ChangeReturned { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Paid;
    public ICollection<InvoiceLine> Items { get; set; } = new List<InvoiceLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string ProductName { get; set; }
    public required string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Payment
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reference { get; set; }
}

public enum CustomerLedgerEntryType { InvoiceCharge, InvoicePayment, BalancePayment, BalanceCharge }

public sealed class CustomerLedgerEntry
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public long? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int? UserId { get; set; }
    public CustomerLedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CashierShift
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? ClosingCash { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

public sealed class InvoiceSequence
{
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
