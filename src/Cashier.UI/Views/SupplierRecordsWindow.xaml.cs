using System.Windows;
using System.Windows.Controls;
using Cashier.Application;
using Cashier.Domain;
using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class SupplierRecordsWindow : Window
{
    public SupplierRecordsWindow(SuppliersViewModel viewModel, int initialTab)
    {
        InitializeComponent();
        DataContext = viewModel;
        RecordsTabs.SelectedIndex = initialTab;
    }

    private async void ShowPurchaseInvoice_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PurchaseInvoiceLookupRow row } ||
            DataContext is not SuppliersViewModel viewModel)
            return;

        try
        {
            var invoice = await viewModel.GetPurchaseInvoiceDetailsAsync(row.Id);
            if (invoice is null)
            {
                viewModel.Status = $"لم يتم العثور على الفاتورة {row.InvoiceNumber}.";
                MessageBox.Show(this, viewModel.Status, "الفاتورة غير موجودة",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            new PurchaseInvoiceDetailsWindow(invoice) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            viewModel.Status = $"تعذر تحميل تفاصيل الفاتورة: {exception.Message}";
            MessageBox.Show(this, viewModel.Status, "تعذر تحميل الفاتورة",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) => Close();
}
