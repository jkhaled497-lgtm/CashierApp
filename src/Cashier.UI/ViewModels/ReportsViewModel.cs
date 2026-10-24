using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using Cashier.Application;
using Cashier.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Cashier.UI.ViewModels;

public sealed partial class ReportsViewModel(
    IInvoiceService invoices,
    ICustomerService customers,
    ISupplierService suppliers,
    IExpenseService expenses,
    IDailyJournalReportService dailyJournal,
    SessionManager session) : ObservableObject
{
    public ObservableCollection<InvoiceDailyReport> Rows { get; } = [];
    public ObservableCollection<InvoiceRevenueReport> RevenueRows { get; } = [];
    public ObservableCollection<TopSellingProductReport> TopSellingRows { get; } = [];
    public ObservableCollection<CustomerAccountReport> CustomerRows { get; } = [];
    public ObservableCollection<SupplierAccountReport> SupplierRows { get; } = [];
    public ObservableCollection<BusinessExpense> ExpenseRows { get; } = [];
    public ObservableCollection<DailyJournalEntry> DailyJournalRows { get; } = [];
    [ObservableProperty] private DateTime fromDate = DateTime.Today;
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private DateTime journalDate = DateTime.Today;
    [ObservableProperty] private DateTime expenseDate = DateTime.Today;
    [ObservableProperty] private string expenseTitle = string.Empty;
    [ObservableProperty] private decimal expenseAmount;
    [ObservableProperty] private string? expenseNotes;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private string journalStatus = string.Empty;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isJournalLoading;

    public bool IsAdmin => session.Current?.Role == UserRole.Admin;
    public bool CanManageExpenses => IsAdmin;
    public decimal CustomerBalanceTotal => CustomerRows.Sum(x => x.CurrentBalance);
    public decimal SupplierBalanceTotal => SupplierRows.Sum(x => x.CurrentBalance);
    public decimal PeriodSalesTotal => Rows.Sum(x => x.SalesTotal);
    public decimal PeriodProfitTotal => Rows.Sum(x => x.ProfitTotal);
    public decimal PeriodRevenueTotal => RevenueRows.Sum(x => x.TotalReceived);
    public decimal PeriodExpenseTotal => ExpenseRows.Sum(x => x.Amount);
    public decimal JournalSalesTotal => DailyJournalRows
        .Where(row => (row.Type is DailyJournalType.SalesInvoice or DailyJournalType.DailySale) &&
            row.IncludeInSalesTotals)
        .Sum(row => row.Amount ?? 0);
    public decimal JournalPurchaseTotal => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.PurchaseInvoice).Sum(row => row.Amount ?? 0);
    public decimal JournalExpenseTotal => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.Expense).Sum(row => row.Amount ?? 0);
    public decimal JournalCustomerCharges => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.CustomerCharge).Sum(row => row.Amount ?? 0);
    public decimal JournalCustomerPayments => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.CustomerPayment).Sum(row => row.Amount ?? 0);
    public decimal JournalSupplierCharges => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.SupplierCharge).Sum(row => row.Amount ?? 0);
    public decimal JournalSupplierPayments => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.SupplierPayment).Sum(row => row.Amount ?? 0);
    public decimal JournalShopQuantitySold => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.DailySale ||
            row.Type == DailyJournalType.SalesInvoice && row.IncludeInSalesTotals)
        .Sum(row => row.Quantity ?? 0);
    public decimal JournalWarehouseEntries => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.WarehouseEntry).Sum(row => row.Quantity ?? 0);
    public decimal JournalWarehouseTransfers => DailyJournalRows
        .Where(row => row.Type == DailyJournalType.WarehouseTransfer).Sum(row => row.Quantity ?? 0);
    public bool CanExportDaily => Rows.Count > 0;
    public bool CanExportProfit => IsAdmin && Rows.Count > 0;
    public bool CanExportRevenue => RevenueRows.Count > 0;
    public bool CanExportTopSelling => TopSellingRows.Count > 0;
    public bool CanExportCustomers => CustomerRows.Count > 0;
    public bool CanExportSuppliers => SupplierRows.Count > 0;
    public bool CanExportExpenses => CanManageExpenses && ExpenseRows.Count > 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (FromDate.Date > ToDate.Date)
        {
            Status = "تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه.";
            return;
        }

        if (IsLoading)
            return;

        IsLoading = true;
        Status = "جار تحميل التقارير...";
        try
        {
            var from = BusinessTime.ToUtc(FromDate.Date);
            var to = BusinessTime.ToUtc(ToDate.Date.AddDays(1));
            var errors = new List<string>();

            try
            {
                var dailyReports = await invoices.GetDailyReportsAsync(from, to);
                if (!IsAdmin)
                    dailyReports = dailyReports
                        .Select(row => row with { CostTotal = 0, ProfitTotal = 0 })
                        .ToArray();
                Replace(Rows, dailyReports);
            }
            catch (Exception exception) { Rows.Clear(); errors.Add($"العمل اليومي: {exception.Message}"); }

            try { Replace(RevenueRows, await invoices.GetRevenueReportsAsync(from, to)); }
            catch (Exception exception) { RevenueRows.Clear(); errors.Add($"الإيرادات: {exception.Message}"); }

            try { Replace(TopSellingRows, await invoices.GetTopSellingProductsAsync(from, to)); }
            catch (Exception exception) { TopSellingRows.Clear(); errors.Add($"أعلى المنتجات مبيعاً: {exception.Message}"); }

            try { Replace(CustomerRows, await customers.GetAccountReportsAsync(from, to)); }
            catch (Exception exception) { CustomerRows.Clear(); errors.Add($"العملاء: {exception.Message}"); }

            try { Replace(SupplierRows, await suppliers.GetAccountReportsAsync(from, to)); }
            catch (Exception exception) { SupplierRows.Clear(); errors.Add($"الموردون: {exception.Message}"); }

            if (CanManageExpenses)
            {
                try { Replace(ExpenseRows, await expenses.GetForPeriodAsync(from, to)); }
                catch (Exception exception) { ExpenseRows.Clear(); errors.Add($"المصروفات: {exception.Message}"); }
            }
            else
            {
                ExpenseRows.Clear();
            }

            try
            {
                await LoadDailyJournalRowsAsync();
            }
            catch (Exception exception)
            {
                DailyJournalRows.Clear();
                JournalStatus = $"تعذر تحميل دفتر اليومية: {exception.Message}";
                errors.Add($"دفتر اليومية: {exception.Message}");
            }

            NotifyReportChanges();

            if (errors.Count > 0)
                Status = $"تعذر تحميل بعض التقارير: {string.Join(" | ", errors)}";
            else if (Rows.Count == 0 && RevenueRows.Count == 0 && TopSellingRows.Count == 0 && CustomerRows.Count == 0 &&
                     SupplierRows.Count == 0 && ExpenseRows.Count == 0 && DailyJournalRows.Count == 0)
                Status = "لا توجد بيانات في الفترة المحددة.";
            else
                Status = $"تم تحميل التقارير للفترة من {FromDate:dd-MM-yyyy} إلى {ToDate:dd-MM-yyyy}.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadDailyJournalAsync()
    {
        if (IsJournalLoading)
            return;

        IsJournalLoading = true;
        JournalStatus = "جار تحميل دفتر اليومية...";
        try
        {
            await LoadDailyJournalRowsAsync();
            JournalStatus = DailyJournalRows.Count == 0
                ? $"لا توجد حركات في يوم {JournalDate:dd-MM-yyyy}."
                : $"تم تحميل {DailyJournalRows.Count:N0} حركة ليوم {JournalDate:dd-MM-yyyy}.";
        }
        catch (Exception exception)
        {
            DailyJournalRows.Clear();
            NotifyJournalChanges();
            JournalStatus = $"تعذر تحميل دفتر اليومية: {exception.Message}";
        }
        finally
        {
            IsJournalLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportDailyAsync() => await ExportAsync(
        "تقرير-العمل-اليومي",
        ["التاريخ", "عدد الفواتير", "حركات الشيت اليومي", "إجمالي المبيعات", "نقدي", "بطاقة", "آجل للعملاء"],
        Rows.Select(x => new[]
        {
            x.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Number(x.InvoiceCount),
            Number(x.DailySaleCount),
            Number(x.SalesTotal),
            Number(x.CashReceived),
            Number(x.CardReceived),
            Number(x.CustomerBalance)
        }).Append(new[]
        {
            CsvText("الإجمالي"),
            Number(Rows.Sum(x => x.InvoiceCount)),
            Number(Rows.Sum(x => x.DailySaleCount)),
            Number(Rows.Sum(x => x.SalesTotal)),
            Number(Rows.Sum(x => x.CashReceived)),
            Number(Rows.Sum(x => x.CardReceived)),
            Number(Rows.Sum(x => x.CustomerBalance))
        }));

    [RelayCommand]
    private async Task ExportProfitAsync()
    {
        if (!IsAdmin)
        {
            Status = "عرض تقرير التكلفة والأرباح متاح لمدير النظام فقط.";
            return;
        }
        await ExportAsync(
        "تقرير-الأرباح",
        ["التاريخ", "قيمة المبيعات", "تكلفة البضاعة المباعة", "الربح الإجمالي"],
        Rows.Select(x => new[]
        {
            x.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Number(x.SalesTotal),
            Number(x.CostTotal),
            Number(x.ProfitTotal)
        }).Append(new[]
        {
            CsvText("الإجمالي"),
            Number(Rows.Sum(x => x.SalesTotal)),
            Number(Rows.Sum(x => x.CostTotal)),
            Number(Rows.Sum(x => x.ProfitTotal))
        }));
    }

    [RelayCommand]
    private async Task ExportRevenueAsync() => await ExportAsync(
        "تقرير-الإيرادات",
        ["التاريخ", "إيراد نقدي", "إيراد البطاقات", "إجمالي الإيرادات المحصلة"],
        RevenueRows.Select(x => new[]
        {
            x.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Number(x.CashReceived),
            Number(x.CardReceived),
            Number(x.TotalReceived)
        }).Append(new[]
        {
            CsvText("الإجمالي"),
            Number(RevenueRows.Sum(x => x.CashReceived)),
            Number(RevenueRows.Sum(x => x.CardReceived)),
            Number(PeriodRevenueTotal)
        }));

    [RelayCommand]
    private async Task ExportTopSellingAsync() => await ExportAsync(
        "تقرير-أعلى-المنتجات-مبيعا",
        ["رمز المنتج", "اسم المنتج", "الكمية المباعة"],
        TopSellingRows.Select(x => new[]
        {
            CsvText(x.Sku),
            CsvText(x.ProductName),
            Number(x.QuantitySold)
        }));

    [RelayCommand]
    private async Task ExportCustomersAsync() => await ExportAsync(
        "تقرير-أرصدة-العملاء",
        ["كود العميل", "اسم العميل", "الهاتف", "الرصيد الحالي", "مبيعات آجلة خلال الفترة", "مدفوعات خلال الفترة"],
        CustomerRows.Select(x => new[]
        {
            CsvText(x.CustomerCode),
            CsvText(x.CustomerName),
            CsvText(x.Phone),
            Number(x.CurrentBalance),
            Number(x.PeriodCharges),
            Number(x.PeriodPayments)
        }).Append(new[]
        {
            CsvText(""),
            CsvText("الإجمالي"),
            CsvText(""),
            Number(CustomerBalanceTotal),
            Number(CustomerRows.Sum(x => x.PeriodCharges)),
            Number(CustomerRows.Sum(x => x.PeriodPayments))
        }));

    [RelayCommand]
    private async Task ExportSuppliersAsync() => await ExportAsync(
        "تقرير-أرصدة-الموردين",
        ["اسم المورد", "الهاتف", "الرصيد الحالي", "مستحقات خلال الفترة", "مدفوعات خلال الفترة"],
        SupplierRows.Select(x => new[]
        {
            CsvText(x.SupplierName),
            CsvText(x.Phone),
            Number(x.CurrentBalance),
            Number(x.PeriodCharges),
            Number(x.PeriodPayments)
        }).Append(new[]
        {
            CsvText("الإجمالي"),
            CsvText(""),
            Number(SupplierBalanceTotal),
            Number(SupplierRows.Sum(x => x.PeriodCharges)),
            Number(SupplierRows.Sum(x => x.PeriodPayments))
        }));

    [RelayCommand]
    private async Task AddExpenseAsync()
    {
        if (!CanManageExpenses)
        {
            Status = "تسجيل المصروفات يتطلب صلاحية المدير أو مدير النظام.";
            return;
        }
        if (session.Current is not { } currentUser)
        {
            Status = "انتهت جلسة المستخدم؛ سجل الدخول مجدداً.";
            return;
        }

        try
        {
            await expenses.CreateAsync(new CreateExpenseRequest(
                ExpenseTitle, ExpenseAmount, ExpenseDate, ExpenseNotes, currentUser.UserId));
            ExpenseTitle = string.Empty;
            ExpenseAmount = 0;
            ExpenseNotes = string.Empty;
            var from = BusinessTime.ToUtc(FromDate.Date);
            var to = BusinessTime.ToUtc(ToDate.Date.AddDays(1));
            Replace(ExpenseRows, await expenses.GetForPeriodAsync(from, to));
            NotifyReportChanges();
            Status = "تم تسجيل المصروف وتحديث التقرير.";
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل المصروف: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportExpensesAsync()
    {
        if (!CanManageExpenses)
        {
            Status = "عرض المصروفات متاح للمدير أو مدير النظام فقط.";
            return;
        }
        await ExportAsync(
            "تقرير-المصروفات",
            ["التاريخ", "المصروف", "القيمة", "الملاحظات", "المستخدم"],
            ExpenseRows.Select(expense => new[]
            {
                BusinessTime.ToEgyptLocal(expense.CreatedAt).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                CsvText(expense.Title),
                Number(expense.Amount),
                CsvText(expense.Notes),
                CsvText(expense.User?.Username)
            }).Append(new[]
            {
                CsvText("الإجمالي"),
                CsvText(""),
                Number(PeriodExpenseTotal),
                CsvText(""),
                CsvText("")
            }));
    }

    private async Task LoadDailyJournalRowsAsync()
    {
        var from = BusinessTime.ToUtc(JournalDate.Date);
        var to = BusinessTime.ToUtc(JournalDate.Date.AddDays(1));
        var rows = await dailyJournal.GetForPeriodAsync(from, to);
        if (session.Current?.CanManageCatalog != true)
        {
            rows = rows.Where(row => row.Type is DailyJournalType.SalesInvoice or DailyJournalType.DailySale
                or DailyJournalType.CustomerCharge or DailyJournalType.CustomerPayment).ToArray();
        }
        Replace(DailyJournalRows, rows);
        NotifyJournalChanges();
        JournalStatus = DailyJournalRows.Count == 0
            ? $"لا توجد حركات في يوم {JournalDate:dd-MM-yyyy}."
            : $"تم تحميل {DailyJournalRows.Count:N0} حركة ليوم {JournalDate:dd-MM-yyyy}.";
    }

    private async Task ExportAsync(string fileName, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"{fileName}-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}.csv",
            Filter = "ملف CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var content = new StringBuilder();
            AppendCsvRow(content, headers.Select(CsvText));
            foreach (var row in rows)
                AppendCsvRow(content, row);
            await File.WriteAllTextAsync(dialog.FileName, content.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Status = $"تم تصدير التقرير إلى: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            Status = $"تعذر تصدير التقرير: {exception.Message}";
        }
    }

    private static void AppendCsvRow(StringBuilder output, IEnumerable<string> values)
    {
        output.AppendJoin(',', values.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));
        output.AppendLine();
    }

    private static string CsvText(string? value)
    {
        var text = value ?? string.Empty;
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
            text = $"'{text}";
        return text;
    }

    private static string Number<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private void NotifyReportChanges()
    {
        OnPropertyChanged(nameof(CustomerBalanceTotal));
        OnPropertyChanged(nameof(SupplierBalanceTotal));
        OnPropertyChanged(nameof(PeriodSalesTotal));
        OnPropertyChanged(nameof(PeriodProfitTotal));
        OnPropertyChanged(nameof(PeriodRevenueTotal));
        OnPropertyChanged(nameof(PeriodExpenseTotal));
        NotifyJournalChanges();
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanManageExpenses));
        OnPropertyChanged(nameof(CanExportDaily));
        OnPropertyChanged(nameof(CanExportProfit));
        OnPropertyChanged(nameof(CanExportRevenue));
        OnPropertyChanged(nameof(CanExportTopSelling));
        OnPropertyChanged(nameof(CanExportCustomers));
        OnPropertyChanged(nameof(CanExportSuppliers));
        OnPropertyChanged(nameof(CanExportExpenses));
    }

    private void NotifyJournalChanges()
    {
        OnPropertyChanged(nameof(JournalSalesTotal));
        OnPropertyChanged(nameof(JournalPurchaseTotal));
        OnPropertyChanged(nameof(JournalExpenseTotal));
        OnPropertyChanged(nameof(JournalCustomerCharges));
        OnPropertyChanged(nameof(JournalCustomerPayments));
        OnPropertyChanged(nameof(JournalSupplierCharges));
        OnPropertyChanged(nameof(JournalSupplierPayments));
        OnPropertyChanged(nameof(JournalShopQuantitySold));
        OnPropertyChanged(nameof(JournalWarehouseEntries));
        OnPropertyChanged(nameof(JournalWarehouseTransfers));
    }

}
