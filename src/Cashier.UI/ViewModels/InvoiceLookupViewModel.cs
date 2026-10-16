using System.Collections.ObjectModel;
using Cashier.Application;
using Cashier.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public sealed partial class InvoiceLookupViewModel(
    IInvoiceService invoices,
    IPrintService printService) : ObservableObject
{
    private int detailRequest;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private InvoiceLookupRow? selectedInvoice;
    [ObservableProperty] private Invoice? result;
    [ObservableProperty] private string status = string.Empty;

    public ObservableCollection<InvoiceLookupRow> InvoiceRows { get; } = [];
    public PageNavigator Pager { get; } = new();
    public bool HasResult => Result is not null;

    public void Initialize()
    {
        Pager.Bind(LoadPageAsync);
        _ = Pager.LoadAsync();
    }

    [RelayCommand]
    private Task SearchAsync() => Pager.LoadAsync(1);

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (Result is null)
            return;

        try
        {
            await printService.PrintReceiptAsync(Result);
            Status = string.Empty;
        }
        catch (Exception exception)
        {
            Status = $"تعذرت طباعة الفاتورة: {exception.Message}";
        }
    }

    private async Task LoadPageAsync(int pageNumber)
    {
        try
        {
            Status = string.Empty;
            DateTimeOffset? from = null;
            DateTimeOffset? to = null;
            if (FromDate is not null)
                from = BusinessTime.ToUtc(FromDate.Value.Date);
            if (ToDate is not null)
                to = BusinessTime.ToUtc(ToDate.Value.Date.AddDays(1));
            if (from is not null && to is not null && from >= to)
                throw new InvalidOperationException("تاريخ البداية يجب أن يسبق تاريخ النهاية.");
            var page = await invoices.SearchPageAsync(
                SearchText, pageNumber, PageNavigator.DefaultPageSize, from, to);
            InvoiceRows.Clear();
            foreach (var invoice in page.Items)
                InvoiceRows.Add(invoice);
            Pager.Update(page.PageNumber, page.TotalCount);

            SelectedInvoice = InvoiceRows.FirstOrDefault();
            if (SelectedInvoice is null)
            {
                Result = null;
                Status = string.IsNullOrWhiteSpace(SearchText)
                    ? "لا توجد فواتير مسجلة حتى الآن."
                    : "لم يتم العثور على فواتير مطابقة للبحث.";
            }
        }
        catch (Exception exception)
        {
            InvoiceRows.Clear();
            SelectedInvoice = null;
            Result = null;
            Status = $"تعذر تحميل الفواتير: {exception.Message}";
        }
    }

    partial void OnSelectedInvoiceChanged(InvoiceLookupRow? value)
    {
        var request = ++detailRequest;
        if (value is null)
        {
            Result = null;
            return;
        }

        _ = LoadDetailsAsync(value.InvoiceNumber, request);
    }

    partial void OnResultChanged(Invoice? value) => OnPropertyChanged(nameof(HasResult));

    private async Task LoadDetailsAsync(string invoiceNumber, int request)
    {
        try
        {
            Status = string.Empty;
            var invoice = await invoices.FindByNumberAsync(invoiceNumber);
            if (request != detailRequest)
                return;

            Result = invoice;
            if (invoice is null)
                Status = $"لم يتم العثور على فاتورة برقم {invoiceNumber}.";
        }
        catch (Exception exception)
        {
            if (request == detailRequest)
            {
                Result = null;
                Status = $"تعذر تحميل تفاصيل الفاتورة: {exception.Message}";
            }
        }
    }
}
