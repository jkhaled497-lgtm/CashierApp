namespace Cashier.UI.Views;

public partial class CashierView : UserControl
{
    public CashierView()
    {
        InitializeComponent();
        //Loaded += (_, _) => BarcodeScanner.Focus();
    }

    private async void BarcodeScanner_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;
        e.Handled = true;
        //var barcode = BarcodeScanner.Text.Trim();
        //BarcodeScanner.Clear();
        //if (barcode.Length > 0 && DataContext is Cashier.UI.ViewModels.CashierViewModel viewModel)
        //    await viewModel.AddScannedBarcodeAsync(barcode);
        ////BarcodeScanner.Focus();
    }

    private void ProductSearch_OnGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is Cashier.UI.ViewModels.CashierViewModel viewModel)
            viewModel.IsProductDropdownOpen = true;
    }

    private void ProductSearch_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<Cashier.Domain.Product>().FirstOrDefault() is { } product &&
            DataContext is Cashier.UI.ViewModels.CashierViewModel viewModel)
        {
            viewModel.SelectProductFromSearch(product);
            ProductSearchBox.SelectedIndex = -1;
        }
    }
}
