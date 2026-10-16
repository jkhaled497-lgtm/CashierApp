using Cashier.UI.ViewModels;
using System.Windows;

namespace Cashier.UI.Views;

public partial class PurchaseInvoicesView : UserControl
{
    public PurchaseInvoicesView()
    {
        InitializeComponent();
    }

    private void ShowInvoiceItems_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchaseInvoicesViewModel viewModel || viewModel.Result is null)
            return;

        new PurchaseInvoiceDetailsWindow(viewModel.Result)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }
}
