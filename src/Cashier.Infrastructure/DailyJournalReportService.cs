using System.Linq.Expressions;
using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class DailyJournalReportService(
    IDbContextFactory<CashierDbContext> contextFactory) : IDailyJournalReportService
{
    public async Task<IReadOnlyList<DailyJournalEntry>> GetForPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = new List<DailyJournalEntry>();

        var invoices = await LoadPeriodAsync(
            db.Invoices.AsNoTracking().Include(invoice => invoice.Customer).Include(invoice => invoice.Items),
            invoice => invoice.CreatedAt, db, from, to, cancellationToken);
        foreach (var invoice in invoices)
        {
            var products = string.Join("، ", invoice.Items.Select(item => $"{item.ProductName} × {item.Quantity:0.###}"));
            var status = invoice.Status == InvoiceStatus.Void ? " (ملغاة)"
                : invoice.Status == InvoiceStatus.Refunded ? " (مرتجعة)" : string.Empty;
            entries.Add(new DailyJournalEntry(
                invoice.CreatedAt,
                DailyJournalType.SalesInvoice,
                $"فاتورة بيع{status}",
                invoice.InvoiceNumber,
                invoice.Customer?.FullName,
                products,
                invoice.Items.Sum(item => item.Quantity),
                invoice.GrandTotal,
                $"فاتورة بيع رقم {invoice.InvoiceNumber}؛ المدفوع {invoice.TotalPaid:N2} والمتبقي {Math.Max(0, invoice.GrandTotal - invoice.TotalPaid):N2}.",
                invoice.Status is not (InvoiceStatus.Void or InvoiceStatus.Refunded)));
        }

        var purchaseInvoices = await LoadPeriodAsync(
            db.PurchaseInvoices.AsNoTracking().Include(invoice => invoice.Supplier).Include(invoice => invoice.Items),
            invoice => invoice.CreatedAt, db, from, to, cancellationToken);
        foreach (var invoice in purchaseInvoices)
        {
            var products = string.Join("، ", invoice.Items.Select(item => $"{item.ProductName} × {item.Quantity:0.###}"));
            entries.Add(new DailyJournalEntry(
                invoice.CreatedAt,
                DailyJournalType.PurchaseInvoice,
                "فاتورة شراء",
                invoice.InvoiceNumber,
                invoice.Supplier.Name,
                products,
                invoice.Items.Sum(item => item.Quantity),
                invoice.GrandTotal,
                $"فاتورة شراء رقم {invoice.InvoiceNumber}؛ المدفوع {invoice.TotalPaid:N2} والمتبقي {Math.Max(0, invoice.GrandTotal - invoice.TotalPaid):N2}."));
        }

        var dailySales = await LoadPeriodAsync(
            db.DailyProducts.AsNoTracking().Include(sale => sale.Customer).Include(sale => sale.Product),
            sale => sale.CreatedAt, db, from, to, cancellationToken);
        entries.AddRange(dailySales.Select(sale =>
        {
            var unitPrice = sale.UnitSellingPrice ?? sale.Product.SellingPrice;
            var total = Math.Round(unitPrice * sale.Quantity, 2, MidpointRounding.AwayFromZero);
            var remaining = Math.Max(0, total - sale.AmountPaid);
            return new DailyJournalEntry(
                sale.CreatedAt,
                DailyJournalType.DailySale,
                "بيع من الشيت اليومي",
                null,
                sale.Customer?.FullName,
                $"{sale.ProductName} × {sale.Quantity:0.###}",
                sale.Quantity,
                total,
                $"بيع {sale.ProductName} بإجمالي {total:N2}؛ المدفوع {sale.AmountPaid:N2} والمتبقي {remaining:N2} بدون فاتورة.");
        }));

        var stockMovements = await LoadPeriodAsync(
            db.StockAdjustments.AsNoTracking().Include(movement => movement.Product),
            movement => movement.CreatedAt, db, from, to, cancellationToken);
        entries.AddRange(stockMovements
            .Where(movement => movement.Type is StockAdjustmentType.WarehouseIn
                or StockAdjustmentType.WarehouseOut or StockAdjustmentType.TransferToShop)
            .Select(movement => CreateStockEntry(movement)));

        var customerEntries = await LoadPeriodAsync(
            db.CustomerLedgerEntries.AsNoTracking().Include(entry => entry.Customer).Include(entry => entry.Invoice),
            entry => entry.CreatedAt, db, from, to, cancellationToken);
        entries.AddRange(customerEntries.Select(entry => CreateCustomerEntry(entry)));

        var supplierEntries = await LoadPeriodAsync(
            db.SupplierBalanceEntries.AsNoTracking().Include(entry => entry.Supplier),
            entry => entry.CreatedAt, db, from, to, cancellationToken);
        entries.AddRange(supplierEntries.Select(entry => CreateSupplierEntry(entry)));

        var expenses = await LoadPeriodAsync(
            db.Expenses.AsNoTracking(),
            expense => expense.CreatedAt, db, from, to, cancellationToken);
        entries.AddRange(expenses.Select(expense => new DailyJournalEntry(
            expense.CreatedAt,
            DailyJournalType.Expense,
            expense.Title,
            null,
            null,
            null,
            null,
            expense.Amount,
            string.IsNullOrWhiteSpace(expense.Notes) ? $"مصروف: {expense.Title}." : expense.Notes.Trim())));

        return entries.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Type).ToArray();
    }

    private static DailyJournalEntry CreateStockEntry(StockAdjustment movement)
    {
        var type = movement.Type switch
        {
            StockAdjustmentType.WarehouseIn => DailyJournalType.WarehouseEntry,
            StockAdjustmentType.WarehouseOut => DailyJournalType.WarehouseExit,
            StockAdjustmentType.TransferToShop => DailyJournalType.WarehouseTransfer,
            _ => throw new ArgumentOutOfRangeException(nameof(movement))
        };
        var details = movement.Type switch
        {
            StockAdjustmentType.WarehouseIn => "إضافة كمية إلى المخزن",
            StockAdjustmentType.WarehouseOut => "إخراج كمية من المخزن",
            StockAdjustmentType.TransferToShop => "تحويل كمية من المخزن إلى المحل",
            _ => throw new ArgumentOutOfRangeException(nameof(movement))
        };
        var reason = movement.Reason?.Trim();
        var reference = movement.Type == StockAdjustmentType.WarehouseIn &&
            !string.IsNullOrWhiteSpace(reason) && reason.StartsWith("bill", StringComparison.OrdinalIgnoreCase)
                ? reason
                : null;
        var note = reference is not null
            ? $"{details}: {movement.Product.Name}، الكمية {movement.Quantity:0.###} من فاتورة شراء رقم {reference}."
            : string.IsNullOrWhiteSpace(reason)
                ? $"{details}: {movement.Product.Name}، الكمية {movement.Quantity:0.###}."
                : $"{details}: {movement.Product.Name}، الكمية {movement.Quantity:0.###}. السبب: {reason}.";
        return new DailyJournalEntry(
            movement.CreatedAt, type, details, reference, null,
            $"{movement.Product.Name} × {movement.Quantity:0.###}", movement.Quantity, null, note);
    }

    private static DailyJournalEntry CreateCustomerEntry(CustomerLedgerEntry entry)
    {
        var isPayment = entry.Type is CustomerLedgerEntryType.InvoicePayment or CustomerLedgerEntryType.BalancePayment;
        var details = isPayment ? "سداد من العميل" : "استحقاق على العميل";
        var note = string.IsNullOrWhiteSpace(entry.Notes)
            ? $"{details}: {entry.Customer.FullName}، المبلغ {entry.Amount:N2}."
            : entry.Notes.Trim();
        return new DailyJournalEntry(
            entry.CreatedAt,
            isPayment ? DailyJournalType.CustomerPayment : DailyJournalType.CustomerCharge,
            details,
            entry.Invoice?.InvoiceNumber,
            entry.Customer.FullName,
            null,
            null,
            entry.Amount,
            note);
    }

    private static DailyJournalEntry CreateSupplierEntry(SupplierBalanceEntry entry)
    {
        var isPayment = entry.Type == SupplierBalanceEntryType.Payment;
        var details = entry.Type == SupplierBalanceEntryType.OpeningBalance
            ? "رصيد افتتاحي للمورد"
            : isPayment ? "سداد للمورد" : "استحقاق للمورد";
        var note = string.IsNullOrWhiteSpace(entry.Notes)
            ? $"{details}: {entry.Supplier.Name}، المبلغ {entry.Amount:N2}."
            : entry.Notes.Trim();
        return new DailyJournalEntry(
            entry.CreatedAt,
            isPayment ? DailyJournalType.SupplierPayment : DailyJournalType.SupplierCharge,
            details,
            null,
            entry.Supplier.Name,
            null,
            null,
            entry.Amount,
            note);
    }

    private static async Task<List<T>> LoadPeriodAsync<T>(
        IQueryable<T> query,
        Expression<Func<T, DateTimeOffset>> createdAtSelector,
        CashierDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            var selector = createdAtSelector.Compile();
            return (await query.ToListAsync(cancellationToken))
                .Where(item => selector(item) >= from && selector(item) < to)
                .ToList();
        }

        var parameter = createdAtSelector.Parameters[0];
        var predicate = Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(
                Expression.GreaterThanOrEqual(createdAtSelector.Body, Expression.Constant(from)),
                Expression.LessThan(createdAtSelector.Body, Expression.Constant(to))),
            parameter);
        return await query.Where(predicate).ToListAsync(cancellationToken);
    }
}
