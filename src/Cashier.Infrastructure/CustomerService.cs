using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class CustomerService(IDbContextFactory<CashierDbContext> contextFactory) : ICustomerService
{
    public async Task<IReadOnlyList<Customer>> SearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Customers.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = $"%{text.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.FullName, term) ||
                EF.Functions.Like(x.CustomerCode, term) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, term)));
        }
        return await query.OrderBy(x => x.FullName).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Customer>> SearchPageAsync(string? text, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Customers.AsNoTracking().Where(x => x.IsActive && !x.IsWalkIn);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = $"%{text.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.FullName, term) ||
                EF.Functions.Like(x.CustomerCode, term) ||
                (x.Phone != null && EF.Functions.Like(x.Phone, term)));
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<Customer>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        customer.FullName = string.IsNullOrWhiteSpace(customer.FullName) ? "عميل جديد" : customer.FullName.Trim();
        customer.CustomerCode = $"TMP-{Guid.NewGuid():N}"[..20];
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        customer.CustomerCode = $"CUST-{customer.Id:0000}";
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return customer;
    }

    public async Task SaveAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Customers.SingleOrDefaultAsync(x => x.Id == customer.Id, cancellationToken)
            ?? throw new KeyNotFoundException("العميل غير موجود.");
        existing.FullName = string.IsNullOrWhiteSpace(customer.FullName) ? "عميل جديد" : customer.FullName.Trim();
        existing.Phone = customer.Phone;
        existing.Address = customer.Address;
        existing.Notes = customer.Notes;
        existing.IsActive = customer.IsActive;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await db.Customers.FindAsync([customerId], cancellationToken)
            ?? throw new KeyNotFoundException("العميل غير موجود.");
        if (customer.IsWalkIn)
            throw new InvalidOperationException("لا يمكن حذف عميل البيع النقدي الافتراضي.");
        if (await db.Invoices.AnyAsync(x => x.CustomerId == customerId, cancellationToken) ||
            await db.CustomerLedgerEntries.AnyAsync(x => x.CustomerId == customerId, cancellationToken))
            customer.IsActive = false;
        else
            db.Customers.Remove(customer);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerLedgerEntry>> GetLedgerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CustomerLedgerEntries.AsNoTracking()
            .Include(x => x.Invoice)
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<PageResult<CustomerLedgerEntry>> GetLedgerPageAsync(int customerId, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(pageNumber, pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.CustomerLedgerEntries.AsNoTracking().Include(x => x.Invoice)
            .Where(x => x.CustomerId == customerId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<CustomerLedgerEntry>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<CustomerAccountReport>> GetAccountReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ValidateReportRange(from, to);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var accounts = await db.Customers.AsNoTracking()
            .Where(x => !x.IsWalkIn)
            .OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Select(customer => new
            {
                customer.Id,
                customer.CustomerCode,
                customer.FullName,
                customer.Phone,
                customer.CurrentBalance
            })
            .ToListAsync(cancellationToken);
        var movements = (await db.CustomerLedgerEntries.AsNoTracking()
            .ToListAsync(cancellationToken))
            .Where(entry => entry.CreatedAt >= from && entry.CreatedAt < to &&
                (entry.Type == CustomerLedgerEntryType.InvoiceCharge ||
                 entry.Type == CustomerLedgerEntryType.BalancePayment))
            .Select(entry => new { entry.CustomerId, entry.Type, entry.Amount })
            .ToList();
        var movementsByCustomer = movements.GroupBy(entry => entry.CustomerId)
            .ToDictionary(group => group.Key, group => (
                Charges: group.Where(entry => entry.Type == CustomerLedgerEntryType.InvoiceCharge).Sum(entry => entry.Amount),
                Payments: group.Where(entry => entry.Type == CustomerLedgerEntryType.BalancePayment).Sum(entry => entry.Amount)));
        return accounts.Select(account =>
        {
            var movement = movementsByCustomer.GetValueOrDefault(account.Id);
            return new CustomerAccountReport(
                account.CustomerCode,
                account.FullName,
                account.Phone,
                account.CurrentBalance,
                movement.Charges,
                movement.Payments);
        }).ToArray();
    }

    public async Task<Customer> RecordBalancePaymentAsync(int customerId, decimal amount, int userId, PaymentMethod method, string? note, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "يجب أن تكون الدفعة أكبر من صفر.");
        if (method is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ArgumentOutOfRangeException(nameof(method), "اختر نقداً أو بطاقة ائتمان.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("العميل غير موجود أو غير مفعل.");
        if (amount > customer.CurrentBalance)
            throw new InvalidOperationException("قيمة الدفعة أكبر من رصيد العميل المستحق.");
        var remainingPayment = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var outstandingInvoices = await db.Invoices
            .Where(x => x.CustomerId == customerId && x.Status == InvoiceStatus.Partial && x.TotalPaid < x.GrandTotal)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var balanceAfterPayment = customer.CurrentBalance;
        foreach (var invoice in outstandingInvoices)
        {
            if (remainingPayment == 0)
                break;
            var invoiceBalance = decimal.Round(invoice.GrandTotal - invoice.TotalPaid, 2, MidpointRounding.AwayFromZero);
            var applied = Math.Min(invoiceBalance, remainingPayment);
            invoice.TotalPaid = decimal.Round(invoice.TotalPaid + applied, 2, MidpointRounding.AwayFromZero);
            if (invoice.TotalPaid >= invoice.GrandTotal)
                invoice.Status = InvoiceStatus.Paid;
            db.Payments.Add(new Payment { Invoice = invoice, Method = method, Amount = applied });
            balanceAfterPayment = decimal.Round(balanceAfterPayment - applied, 2, MidpointRounding.AwayFromZero);
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                Customer = customer,
                Invoice = invoice,
                UserId = userId,
                Type = CustomerLedgerEntryType.BalancePayment,
                Amount = applied,
                BalanceAfter = balanceAfterPayment,
                Notes = $"{(string.IsNullOrWhiteSpace(note) ? "سداد رصيد العميل" : note.Trim())} - الفاتورة {invoice.InvoiceNumber}"
            });
            remainingPayment -= applied;
        }
        customer.CurrentBalance = decimal.Round(customer.CurrentBalance - amount, 2, MidpointRounding.AwayFromZero);
        if (remainingPayment > 0)
        {
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                Customer = customer,
                UserId = userId,
                Type = CustomerLedgerEntryType.BalancePayment,
                Amount = remainingPayment,
                BalanceAfter = customer.CurrentBalance,
                Notes = string.IsNullOrWhiteSpace(note) ? "سداد رصيد العميل" : note.Trim()
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return customer;
    }

    private static void ValidatePage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "رقم الصفحة وحجمها غير صالحين.");
    }

    private static void ValidateReportRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");
    }
}
