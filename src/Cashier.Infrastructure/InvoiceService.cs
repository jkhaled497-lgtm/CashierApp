using Cashier.Application;
using Cashier.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cashier.Infrastructure;

public sealed class InvoiceService(IDbContextFactory<CashierDbContext> contextFactory) : IInvoiceService
{
    public async Task<Invoice> CompleteSaleAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items.Count == 0)
            throw new InvalidOperationException("أضف منتجاً واحداً على الأقل قبل إتمام الفاتورة.");
        if (request.GlobalDiscountPercent is < 0 or > 100 || request.GlobalDiscountFixed < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "يجب أن يكون الخصم العام بين صفر و١٠٠٪.");
        if (request.Items.Any(x => x.Quantity <= 0 || x.DiscountPercent is < 0 or > 100 || x.DiscountFixed < 0))
            throw new ArgumentOutOfRangeException(nameof(request), "يجب أن تكون الكمية موجبة والخصومات صالحة.");
        if (request.Payments.Any(x => x.Amount < 0))
            throw new InvalidOperationException("لا يمكن أن تكون قيمة الدفع سالبة.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var customer = request.CustomerId is null
            ? null
            : await db.Customers.SingleOrDefaultAsync(x => x.Id == request.CustomerId && x.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("العميل المختار غير موجود أو غير نشط.");
        var account = await db.Users.AsNoTracking()
                .Where(x => x.Id == request.UserId && x.IsActive)
                .Select(x => new { x.Role, EmployeeId = x.Employee == null ? (int?)null : x.Employee.Id })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("لم يعد حساب المستخدم المسجل نشطاً.");
        if (account.EmployeeId != request.EmployeeId)
            throw new UnauthorizedAccessException("تغير ربط الموظف بالحساب؛ سجل الدخول مجدداً.");
        if (request.GlobalDiscountPercent > 0 || request.GlobalDiscountFixed > 0)
        {
            if (account.Role is not (UserRole.Admin or UserRole.Manager))
                throw new UnauthorizedAccessException("يتطلب خصم الفاتورة صلاحية المدير أو مدير النظام.");
        }
        var productIds = request.Items.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.Where(x => productIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != productIds.Length)
            throw new InvalidOperationException("منتج واحد أو أكثر لم يعد متاحاً.");
        foreach (var line in request.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || product.CurrentStock < line.Quantity)
                throw new InvalidOperationException($"المخزون غير كافٍ للمنتج {products.GetValueOrDefault(line.ProductId)?.Name ?? "المحدد"}.");
        }

        var pricing = SalePricingCalculator.Calculate(
            request.Items.Select(line => (products[line.ProductId], line)).ToArray(),
            request.GlobalDiscountPercent,
            request.GlobalDiscountFixed);
        var grandTotal = pricing.GrandTotal;
        var cashTender = request.Payments.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount);
        var cardPaid = request.Payments.Where(x => x.Method is PaymentMethod.DebitCard or PaymentMethod.CreditCard).Sum(x => x.Amount);
        var requestedAccountCharge = request.Payments.Where(x => x.Method == PaymentMethod.ClientAccount).Sum(x => x.Amount);
        if (cardPaid > grandTotal)
            throw new InvalidOperationException("قيمة الدفع بالبطاقة أكبر من إجمالي الفاتورة.");
        var appliedCash = Math.Min(cashTender, Math.Max(0, grandTotal - cardPaid));
        var totalPaid = Money(appliedCash + cardPaid);
        var accountCharge = Money(grandTotal - totalPaid);
        if (requestedAccountCharge > 0 && Money(requestedAccountCharge) != accountCharge)
            throw new InvalidOperationException($"المبلغ الآجل للعميل يجب أن يساوي الرصيد المتبقي {accountCharge:C2}.");
        if (accountCharge > 0 && (customer is null || customer.IsWalkIn))
            throw new InvalidOperationException("لا يمكن تسجيل رصيد آجل على العميل النقدي؛ اختر عميلاً مسجلاً.");
        if (accountCharge > 0 && customer is { IsWalkIn: false, CreditLimit: > 0 } &&
            customer.CurrentBalance + accountCharge > customer.CreditLimit)
            throw new InvalidOperationException("الرصيد المتبقي يتجاوز حد ائتمان العميل.");
        if (request.Payments.Count == 0 && totalPaid == 0)
            throw new InvalidOperationException("أدخل دفعة أو قيمة الرصيد الآجل للفاتورة.");

        const int sequenceKey = 0;
        var sequence = await db.InvoiceSequences.SingleOrDefaultAsync(x => x.Year == sequenceKey, cancellationToken);
        if (sequence is null)
        {
            var previousNumbers = await db.Invoices.AsNoTracking()
                .Select(x => x.InvoiceNumber)
                .ToListAsync(cancellationToken);
            var lastNumber = previousNumbers
                .Select(number => int.TryParse(number, out var parsed) ? parsed : 0)
                .DefaultIfEmpty()
                .Max();
            sequence = new InvoiceSequence { Year = sequenceKey, LastNumber = lastNumber + 1 };
            db.InvoiceSequences.Add(sequence);
        }
        else
        {
            sequence.LastNumber++;
        }

        var invoice = new Invoice
        {
            InvoiceNumber = sequence.LastNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = DateTimeOffset.UtcNow,
            CustomerId = customer?.Id,
            UserId = request.UserId,
            ShiftId = null,
            CashierEmployeeId = request.EmployeeId,
            Subtotal = pricing.Subtotal,
            DiscountTotal = pricing.DiscountTotal,
            TaxTotal = pricing.TaxTotal,
            Rounding = pricing.Rounding,
            GrandTotal = grandTotal,
            TotalPaid = totalPaid,
            ChangeReturned = Money(cashTender - appliedCash),
            Customer = customer,
            Status = accountCharge > 0 ? InvoiceStatus.Partial : InvoiceStatus.Paid
        };
        for (var index = 0; index < pricing.Lines.Count; index++)
        {
            var pricedLine = pricing.Lines[index];
            var product = pricedLine.Product;
            invoice.Items.Add(new InvoiceLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                UnitPrice = product.SellingPrice,
                CostPrice = product.CostPrice,
                Quantity = pricedLine.Quantity,
                DiscountAmount = pricedLine.DiscountAmount,
                TaxRate = 0,
                TaxAmount = pricedLine.TaxAmount,
                LineTotal = pricedLine.LineTotal
            });
            product.CurrentStock -= pricedLine.Quantity;
            db.StockAdjustments.Add(new StockAdjustment
            {
                ProductId = product.Id,
                Type = StockAdjustmentType.StockOut,
                Quantity = pricedLine.Quantity,
                EmployeeId = request.EmployeeId,
                Reason = invoice.InvoiceNumber
            });
        }
        if (cardPaid > 0)
        {
            foreach (var tender in request.Payments.Where(x => x.Method is PaymentMethod.DebitCard or PaymentMethod.CreditCard))
                invoice.Payments.Add(new Payment { Method = tender.Method, Amount = tender.Amount, Reference = tender.Reference });
        }
        if (appliedCash > 0)
            invoice.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = appliedCash });
        if (accountCharge > 0)
            invoice.Payments.Add(new Payment { Method = PaymentMethod.ClientAccount, Amount = accountCharge });
        if (customer is not null)
        {
            customer.CurrentBalance += accountCharge;
            customer.LoyaltyPoints += (int)decimal.Floor(grandTotal);
        }
        db.Invoices.Add(invoice);
        if (customer is not null && accountCharge > 0)
        {
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                Customer = customer,
                Invoice = invoice,
                UserId = request.UserId,
                Type = CustomerLedgerEntryType.InvoiceCharge,
                Amount = accountCharge,
                BalanceAfter = customer.CurrentBalance,
                Notes = $"رصيد متبقٍ من الفاتورة {invoice.InvoiceNumber}"
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<PageResult<InvoiceLookupRow>> SearchPageAsync(
        string? search, int pageNumber, int pageSize,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (from is not null)
            query = query.Where(x => x.CreatedAt >= from.Value);
        if (to is not null)
            query = query.Where(x => x.CreatedAt < to.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.InvoiceNumber.Contains(term) ||
                (x.Customer != null && x.Customer.FullName.Contains(term)) ||
                (x.Customer != null && x.Customer.Phone != null && x.Customer.Phone.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var invoices = await query
            .OrderByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new InvoiceLookupRow(
                x.Id,
                x.InvoiceNumber,
                x.CreatedAt,
                x.Customer == null ? "بدون عميل" : x.Customer.FullName,
                x.GrandTotal,
                x.Status))
            .ToListAsync(cancellationToken);

        return new PageResult<InvoiceLookupRow>(invoices, pageNumber, pageSize, totalCount);
    }

    public async Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Invoices.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.CreatedByUser)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.InvoiceNumber == invoiceNumber.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceDailyReport>> GetDailyReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var invoiceQuery = db.Invoices.AsNoTracking()
            .Where(x => x.Status != InvoiceStatus.Void && x.Status != InvoiceStatus.Refunded)
            .Include(x => x.Items);
        var invoices = IsSqlite(db)
            ? (await invoiceQuery.ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToList()
            : await invoiceQuery.Where(x => x.CreatedAt >= from && x.CreatedAt < to)
                .ToListAsync(cancellationToken);
        var paymentQuery = db.Payments.AsNoTracking()
            .Where(x => x.Invoice.Status != InvoiceStatus.Void && x.Invoice.Status != InvoiceStatus.Refunded)
            .Where(x => x.Method == PaymentMethod.Cash ||
                        x.Method == PaymentMethod.DebitCard ||
                        x.Method == PaymentMethod.CreditCard)
            .Select(x => new { x.CreatedAt, x.Method, x.Amount });
        var payments = IsSqlite(db)
            ? (await paymentQuery.ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToList()
            : await paymentQuery.Where(x => x.CreatedAt >= from && x.CreatedAt < to)
                .ToListAsync(cancellationToken);
        var balanceQuery = db.CustomerLedgerEntries.AsNoTracking()
            .Where(x => x.Type == CustomerLedgerEntryType.InvoiceCharge ||
                        x.Type == CustomerLedgerEntryType.BalanceCharge)
            .Select(x => new { x.CreatedAt, x.Amount });
        var balanceCharges = IsSqlite(db)
            ? (await balanceQuery.ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToList()
            : await balanceQuery.Where(x => x.CreatedAt >= from && x.CreatedAt < to)
                .ToListAsync(cancellationToken);

        var salesByDate = invoices
            .GroupBy(x => BusinessTime.ToEgyptLocal(x.CreatedAt).Date)
            .ToDictionary(day => day.Key, day =>
            {
                var cost = day.Sum(invoice => invoice.Items.Sum(item => item.CostPrice * item.Quantity));
                var sales = day.Sum(invoice => invoice.GrandTotal);
                return (InvoiceCount: day.Count(), SalesTotal: sales, CostTotal: cost, ProfitTotal: sales - cost);
            });
        var paymentsByDate = payments
            .GroupBy(x => BusinessTime.ToEgyptLocal(x.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => (
                Cash: day.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount),
                Card: day.Where(x => x.Method is PaymentMethod.DebitCard or PaymentMethod.CreditCard).Sum(x => x.Amount)));
        var chargeByDate = balanceCharges
            .GroupBy(x => BusinessTime.ToEgyptLocal(x.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => day.Sum(x => x.Amount));

        var dailySales = await LoadDailySalesAsync(db, from, to, cancellationToken);
        var dailySalesByDate = dailySales
            .GroupBy(sale => BusinessTime.ToEgyptLocal(sale.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => (
                DailySaleCount: day.Count(),
                SalesTotal: day.Sum(sale => Money(
                    (sale.UnitSellingPrice ?? sale.Product.SellingPrice) * sale.Quantity - sale.DiscountAmount)),
                CostTotal: day.Sum(sale => Money((sale.UnitCostPrice ?? sale.Product.CostPrice) * sale.Quantity))));
        var dailyPaymentsByDate = dailySales
            .GroupBy(sale => BusinessTime.ToEgyptLocal(sale.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => (
                Cash: day.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => sale.AmountPaid),
                Card: day.Where(sale => sale.PaymentMethod is PaymentMethod.DebitCard or PaymentMethod.CreditCard)
                    .Sum(sale => sale.AmountPaid)));

        return salesByDate.Keys.Concat(dailySalesByDate.Keys).Concat(paymentsByDate.Keys).Concat(chargeByDate.Keys)
            .Distinct()
            .OrderBy(date => date)
            .Select(date =>
            {
                var sales = salesByDate.GetValueOrDefault(date);
                var daily = dailySalesByDate.GetValueOrDefault(date);
                var paymentsForDate = paymentsByDate.GetValueOrDefault(date);
                var dailyPayments = dailyPaymentsByDate.GetValueOrDefault(date);
                var salesTotal = sales.SalesTotal + daily.SalesTotal;
                var costTotal = sales.CostTotal + daily.CostTotal;
                return new InvoiceDailyReport(
                    date,
                    sales.InvoiceCount,
                    salesTotal,
                    paymentsForDate.Cash + dailyPayments.Cash,
                    paymentsForDate.Card + dailyPayments.Card,
                    chargeByDate.GetValueOrDefault(date),
                    costTotal,
                    salesTotal - costTotal,
                    daily.DailySaleCount);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<InvoiceRevenueReport>> GetRevenueReportsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var paymentQuery = db.Payments.AsNoTracking()
            .Where(x => x.Invoice.Status != InvoiceStatus.Void && x.Invoice.Status != InvoiceStatus.Refunded)
            .Where(x => x.Method == PaymentMethod.Cash ||
                        x.Method == PaymentMethod.DebitCard ||
                        x.Method == PaymentMethod.CreditCard)
            .Select(x => new { x.CreatedAt, x.Method, x.Amount });
        var payments = IsSqlite(db)
            ? (await paymentQuery.ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToList()
            : await paymentQuery.Where(x => x.CreatedAt >= from && x.CreatedAt < to)
                .ToListAsync(cancellationToken);

        var dailySales = await LoadDailySalesAsync(db, from, to, cancellationToken);
        var dailyByDate = dailySales
            .GroupBy(sale => BusinessTime.ToEgyptLocal(sale.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => (
                Cash: day.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => sale.AmountPaid),
                Card: day.Where(sale => sale.PaymentMethod is PaymentMethod.DebitCard or PaymentMethod.CreditCard)
                    .Sum(sale => sale.AmountPaid)));
        var invoiceByDate = payments
            .GroupBy(x => BusinessTime.ToEgyptLocal(x.CreatedAt).Date)
            .ToDictionary(day => day.Key, day => (
                Cash: day.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount),
                Card: day.Where(x => x.Method is PaymentMethod.DebitCard or PaymentMethod.CreditCard).Sum(x => x.Amount)));
        return invoiceByDate.Keys.Concat(dailyByDate.Keys)
            .Distinct()
            .Select(date =>
            {
                var invoiceAmounts = invoiceByDate.GetValueOrDefault(date);
                var dailyAmounts = dailyByDate.GetValueOrDefault(date);
                return new InvoiceRevenueReport(
                    date, invoiceAmounts.Cash + dailyAmounts.Cash, invoiceAmounts.Card + dailyAmounts.Card);
            })
            .OrderBy(x => x.Date)
            .ToArray();
    }

    public async Task<IReadOnlyList<TopSellingProductReport>> GetTopSellingProductsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (from is not null && to is not null && to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var invoiceQuery = db.Invoices.AsNoTracking()
            .Where(invoice => invoice.Status != InvoiceStatus.Void && invoice.Status != InvoiceStatus.Refunded);
        var filteredInvoiceQuery = invoiceQuery;
        if (from is not null)
            filteredInvoiceQuery = filteredInvoiceQuery.Where(invoice => invoice.CreatedAt >= from.Value);
        if (to is not null)
            filteredInvoiceQuery = filteredInvoiceQuery.Where(invoice => invoice.CreatedAt < to.Value);
        var invoices = IsSqlite(db)
            ? (await invoiceQuery.Include(invoice => invoice.Items).ToListAsync(cancellationToken))
                .Where(invoice => (from is null || invoice.CreatedAt >= from.Value) &&
                    (to is null || invoice.CreatedAt < to.Value)).ToList()
            : await filteredInvoiceQuery.Include(invoice => invoice.Items).ToListAsync(cancellationToken);

        var dailySales = await LoadDailySalesAsync(db, from, to, cancellationToken);
        return invoices.SelectMany(invoice => invoice.Items)
            .Select(line => new { line.ProductId, line.ProductName, line.Sku, line.Quantity })
            .Concat(dailySales.Select(sale => new
            {
                sale.ProductId,
                sale.ProductName,
                sale.Sku,
                Quantity = sale.Quantity
            }))
            .GroupBy(line => line.ProductId)
            .Select(group => new TopSellingProductReport(
                group.Key,
                group.First().ProductName,
                group.First().Sku,
                group.Sum(line => line.Quantity)))
            .OrderByDescending(report => report.QuantitySold)
            .ThenBy(report => report.ProductName)
            .ToArray();
    }

    private static async Task<List<DailyProduct>> LoadDailySalesAsync(
        CashierDbContext db,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            var allSales = await db.DailyProducts.AsNoTracking().Include(sale => sale.Product)
                .ToListAsync(cancellationToken);
            return allSales.Where(sale =>
                (from is null || sale.CreatedAt >= from.Value) &&
                (to is null || sale.CreatedAt < to.Value)).ToList();
        }
        var query = db.DailyProducts.AsNoTracking().Include(sale => sale.Product).AsQueryable();
        if (from is not null)
            query = query.Where(sale => sale.CreatedAt >= from.Value);
        if (to is not null)
            query = query.Where(sale => sale.CreatedAt < to.Value);
        return await query.ToListAsync(cancellationToken);
    }

    private static bool IsSqlite(CashierDbContext db) =>
        string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
