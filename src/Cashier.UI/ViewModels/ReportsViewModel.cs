using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using Cashier.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Cashier.UI.ViewModels;

public sealed partial class ReportsViewModel(
    IInvoiceService invoices,
    ICustomerService customers,
    ISupplierService suppliers) : ObservableObject
{
    public ObservableCollection<InvoiceDailyReport> Rows { get; } = [];
    public ObservableCollection<InvoiceRevenueReport> RevenueRows { get; } = [];
    public ObservableCollection<CustomerAccountReport> CustomerRows { get; } = [];
    public ObservableCollection<SupplierAccountReport> SupplierRows { get; } = [];
    [ObservableProperty] private DateTime fromDate = DateTime.Today;
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isLoading;

    public decimal CustomerBalanceTotal => CustomerRows.Sum(x => x.CurrentBalance);
    public decimal SupplierBalanceTotal => SupplierRows.Sum(x => x.CurrentBalance);
    public decimal PeriodSalesTotal => Rows.Sum(x => x.SalesTotal);
    public decimal PeriodProfitTotal => Rows.Sum(x => x.ProfitTotal);
    public decimal PeriodRevenueTotal => RevenueRows.Sum(x => x.TotalReceived);
    public bool CanExportDaily => Rows.Count > 0;
    public bool CanExportProfit => Rows.Count > 0;
    public bool CanExportRevenue => RevenueRows.Count > 0;
    public bool CanExportCustomers => CustomerRows.Count > 0;
    public bool CanExportSuppliers => SupplierRows.Count > 0;

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

            try { Replace(Rows, await invoices.GetDailyReportsAsync(from, to)); }
            catch (Exception exception) { Rows.Clear(); errors.Add($"العمل اليومي: {exception.Message}"); }

            try { Replace(RevenueRows, await invoices.GetRevenueReportsAsync(from, to)); }
            catch (Exception exception) { RevenueRows.Clear(); errors.Add($"الإيرادات: {exception.Message}"); }

            try { Replace(CustomerRows, await customers.GetAccountReportsAsync(from, to)); }
            catch (Exception exception) { CustomerRows.Clear(); errors.Add($"العملاء: {exception.Message}"); }

            try { Replace(SupplierRows, await suppliers.GetAccountReportsAsync(from, to)); }
            catch (Exception exception) { SupplierRows.Clear(); errors.Add($"الموردون: {exception.Message}"); }

            NotifyReportChanges();

            if (errors.Count > 0)
                Status = $"تعذر تحميل بعض التقارير: {string.Join(" | ", errors)}";
            else if (Rows.Count == 0 && RevenueRows.Count == 0 && CustomerRows.Count == 0 && SupplierRows.Count == 0)
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
    private async Task ExportDailyAsync() => await ExportAsync(
        "تقرير-العمل-اليومي",
        ["التاريخ", "عدد الفواتير", "إجمالي المبيعات", "نقدي", "بطاقة", "آجل للعملاء"],
        Rows.Select(x => new[]
        {
            x.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Number(x.InvoiceCount),
            Number(x.SalesTotal),
            Number(x.CashReceived),
            Number(x.CardReceived),
            Number(x.CustomerBalance)
        }).Append(new[]
        {
            CsvText("الإجمالي"),
            Number(Rows.Sum(x => x.InvoiceCount)),
            Number(Rows.Sum(x => x.SalesTotal)),
            Number(Rows.Sum(x => x.CashReceived)),
            Number(Rows.Sum(x => x.CardReceived)),
            Number(Rows.Sum(x => x.CustomerBalance))
        }));

    [RelayCommand]
    private async Task ExportProfitAsync() => await ExportAsync(
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
        OnPropertyChanged(nameof(CanExportDaily));
        OnPropertyChanged(nameof(CanExportProfit));
        OnPropertyChanged(nameof(CanExportRevenue));
        OnPropertyChanged(nameof(CanExportCustomers));
        OnPropertyChanged(nameof(CanExportSuppliers));
    }

}
