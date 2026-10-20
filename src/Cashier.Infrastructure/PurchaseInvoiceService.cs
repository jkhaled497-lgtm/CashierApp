using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class PurchaseInvoiceService(IDbContextFactory<CashierDbContext> contextFactory)
    : IPurchaseInvoiceService
{
    public async Task<PurchaseInvoice> CreateAsync(
        CreatePurchaseInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items.Count == 0)
            throw new InvalidOperationException("أضف منتجاً واحداً على الأقل إلى فاتورة الشراء.");
        if (request.Items.Any(item => item.UnitPrice <= 0 || item.Quantity <= 0 ||
                                      item.DiscountAmount < 0 ||
                                      item.DiscountAmount > item.UnitPrice * item.Quantity))
            throw new ArgumentOutOfRangeException(nameof(request), "تحقق من سعر وكمية وخصم كل صنف.");
        if (request.PaymentAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "قيمة الدفع لا يمكن أن تكون سالبة.");
        if (request.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ArgumentOutOfRangeException(nameof(request), "اختر الدفع نقداً أو ببطاقة ائتمان.");

        var lines = request.Items.Select(item => new
        {
            item.ProductId,
            UnitPrice = Money(item.UnitPrice),
            item.Quantity,
            Discount = Money(item.DiscountAmount),
            Gross = Money(item.UnitPrice * item.Quantity)
        }).ToArray();
        if (lines.Any(line => line.Discount > line.Gross))
            throw new ArgumentOutOfRangeException(nameof(request), "لا يمكن أن يتجاوز الخصم إجمالي الصنف.");

        var subtotal = Money(lines.Sum(line => line.Gross));
        var discountTotal = Money(lines.Sum(line => line.Discount));
        var grandTotal = Money(subtotal - discountTotal);
        if (request.PaymentAmount > grandTotal)
            throw new InvalidOperationException("قيمة الدفع أكبر من إجمالي فاتورة الشراء.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var account = await db.Users.AsNoTracking()
            .Where(user => user.Id == request.UserId && user.IsActive)
            .Select(user => (UserRole?)user.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (account is not (UserRole.Admin or UserRole.Manager))
            throw new UnauthorizedAccessException("تتطلب فواتير الشراء صلاحية المدير أو مدير النظام.");

        var supplier = await db.Suppliers.SingleOrDefaultAsync(
            item => item.Id == request.SupplierId && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("اختر مورداً نشطاً.");
        var productIds = lines.Select(line => line.ProductId).Distinct().ToArray();
        var products = await db.Products
            .Where(product => productIds.Contains(product.Id) && product.IsActive)
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != productIds.Length)
            throw new InvalidOperationException("منتج واحد أو أكثر لم يعد متاحاً.");

        var sequence = await db.PurchaseInvoiceSequences
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (sequence is null)
        {
            sequence = new PurchaseInvoiceSequence { Id = 1, LastNumber = 0 };
            db.PurchaseInvoiceSequences.Add(sequence);
        }
        sequence.LastNumber++;
        var invoiceNumber = $"bill{sequence.LastNumber}";
        var invoice = new PurchaseInvoice
        {
            InvoiceNumber = invoiceNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            SupplierId = supplier.Id,
            UserId = request.UserId,
            Subtotal = subtotal,
            DiscountTotal = discountTotal,
            GrandTotal = grandTotal,
            TotalPaid = Money(request.PaymentAmount),
            Status = request.PaymentAmount == grandTotal
                ? PurchaseInvoiceStatus.Paid
                : request.PaymentAmount == 0
                    ? PurchaseInvoiceStatus.Unpaid
                    : PurchaseInvoiceStatus.Partial
        };

        foreach (var line in lines)
        {
            var product = products[line.ProductId];
            invoice.Items.Add(new PurchaseInvoiceLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
                DiscountAmount = line.Discount,
                LineTotal = Money(line.Gross - line.Discount)
            });
        }

        foreach (var productGroup in lines.GroupBy(line => line.ProductId))
        {
            var product = products[productGroup.Key];
            var oldQuantity = product.CurrentStock + product.WarehouseStock;
            var addedQuantity = productGroup.Sum(line => line.Quantity);
            var addedCost = productGroup.Sum(line => line.Gross - line.Discount);
            product.CostPrice = Money(oldQuantity + addedQuantity == 0
                ? 0
                : (oldQuantity * product.CostPrice + addedCost) / (oldQuantity + addedQuantity));
            product.WarehouseStock += addedQuantity;
            foreach (var line in productGroup)
            {
                db.StockAdjustments.Add(new StockAdjustment
                {
                    ProductId = product.Id,
                    Type = StockAdjustmentType.WarehouseIn,
                    Quantity = line.Quantity,
                    Reason = invoiceNumber
                });
            }
        }

        var outstandingAfterPayment = Money(grandTotal - request.PaymentAmount);
        supplier.CurrentBalance = Money(supplier.CurrentBalance + grandTotal);
        if (grandTotal > request.PaymentAmount)
        {
            db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
            {
                SupplierId = supplier.Id,
                UserId = request.UserId,
                Type = SupplierBalanceEntryType.Charge,
                Amount = outstandingAfterPayment,
                BalanceAfter = supplier.CurrentBalance,
                Notes = $"المتبقي من فاتورة الشراء {invoiceNumber}"
            });
        }
        if (request.PaymentAmount > 0)
        {
            supplier.CurrentBalance = Money(supplier.CurrentBalance - request.PaymentAmount);
            db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
            {
                SupplierId = supplier.Id,
                UserId = request.UserId,
                Type = SupplierBalanceEntryType.Payment,
                Amount = Money(request.PaymentAmount),
                BalanceAfter = supplier.CurrentBalance,
                PaymentMethod = request.PaymentMethod,
                Notes = $"تسديد فاتورة الشراء {invoiceNumber}"
            });
            invoice.Payments.Add(new PurchasePayment
            {
                Method = request.PaymentMethod,
                Amount = invoice.TotalPaid,
                CreatedAt = invoice.CreatedAt
            });
        }

        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<PurchaseInvoice> RecordPaymentAsync(
        PurchaseInvoicePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "يجب أن تكون الدفعة أكبر من صفر.");
        if (request.Method is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ArgumentOutOfRangeException(nameof(request), "اختر الدفع نقداً أو ببطاقة ائتمان.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var role = await db.Users.AsNoTracking()
            .Where(user => user.Id == request.UserId && user.IsActive)
            .Select(user => (UserRole?)user.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not (UserRole.Admin or UserRole.Manager))
            throw new UnauthorizedAccessException("تتطلب تسديد فواتير المورد صلاحية المدير أو مدير النظام.");

        var invoice = await db.PurchaseInvoices.Include(item => item.Supplier)
            .Include(item => item.Items).Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == request.InvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("فاتورة الشراء غير موجودة.");
        var amount = Money(request.Amount);
        var due = Money(invoice.GrandTotal - invoice.TotalPaid);
        if (amount > due)
            throw new InvalidOperationException($"قيمة التسديد أكبر من المتبقي على الفاتورة ({due:N2} ج.م).");
        if (amount > invoice.Supplier.CurrentBalance)
            throw new InvalidOperationException("قيمة التسديد أكبر من رصيد المورد المستحق.");

        invoice.TotalPaid = Money(invoice.TotalPaid + amount);
        invoice.Status = invoice.TotalPaid == invoice.GrandTotal
            ? PurchaseInvoiceStatus.Paid
            : PurchaseInvoiceStatus.Partial;
        var paidAt = DateTimeOffset.UtcNow;
        invoice.Payments.Add(new PurchasePayment
        {
            Method = request.Method,
            Amount = amount,
            CreatedAt = paidAt
        });
        invoice.Supplier.CurrentBalance = Money(invoice.Supplier.CurrentBalance - amount);
        db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
        {
            SupplierId = invoice.SupplierId,
            UserId = request.UserId,
            Type = SupplierBalanceEntryType.Payment,
            Amount = amount,
            BalanceAfter = invoice.Supplier.CurrentBalance,
            PaymentMethod = request.Method,
            Notes = $"تسديد فاتورة الشراء {invoice.InvoiceNumber}"
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<PageResult<PurchaseInvoiceLookupRow>> GetPageAsync(
        int supplierId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PurchaseInvoices.AsNoTracking().Where(invoice => invoice.SupplierId == supplierId);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(invoice => invoice.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(invoice => new PurchaseInvoiceLookupRow(
                invoice.Id, invoice.InvoiceNumber, invoice.CreatedAt, invoice.Supplier.Name,
                invoice.GrandTotal, invoice.TotalPaid, invoice.Status))
            .ToListAsync(cancellationToken);
        return new PageResult<PurchaseInvoiceLookupRow>(rows, pageNumber, pageSize, count);
    }

    public async Task<PurchaseInvoice?> GetByIdAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PurchaseInvoices.AsNoTracking()
            .Include(invoice => invoice.Supplier)
            .Include(invoice => invoice.CreatedByUser)
            .Include(invoice => invoice.Items)
            .Include(invoice => invoice.Payments)
            .SingleOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);
    }

    public async Task<PurchaseSupplierStatistics> GetStatisticsAsync(
        int supplierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var supplier = await db.Suppliers.AsNoTracking()
            .Where(item => item.Id == supplierId)
            .Select(item => new { item.CurrentBalance })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("المورد غير موجود.");
        var invoiceTotals = await db.PurchaseInvoices.AsNoTracking()
            .Where(invoice => invoice.SupplierId == supplierId)
            .Select(invoice => new { invoice.GrandTotal, invoice.TotalPaid })
            .ToListAsync(cancellationToken);
        var totalPurchases = invoiceTotals.Sum(invoice => invoice.GrandTotal);
        var totalPaid = invoiceTotals.Sum(invoice => invoice.TotalPaid);
        return new PurchaseSupplierStatistics(
            invoiceTotals.Count,
            totalPurchases,
            totalPaid,
            Money(totalPurchases - totalPaid),
            supplier.CurrentBalance);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }
}
