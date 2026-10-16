using System.Windows;
using System.Windows.Controls;
using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class InvoiceLookupView : UserControl
{
    private bool initialized;

    public InvoiceLookupView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized || DataContext is not InvoiceLookupViewModel viewModel)
            return;

        initialized = true;
        viewModel.Initialize();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {

    }

    private void ShowInvoiceItems_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not InvoiceLookupViewModel viewModel || viewModel.Result is null)
            return;

        new InvoiceItemsWindow(viewModel.Result)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }


}
