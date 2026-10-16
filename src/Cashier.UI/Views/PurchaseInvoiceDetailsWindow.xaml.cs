using System.Windows;
using Cashier.Domain;

namespace Cashier.UI.Views;

public partial class PurchaseInvoiceDetailsWindow : Window
{
    public PurchaseInvoiceDetailsWindow(PurchaseInvoice invoice)
    {
        InitializeComponent();
        DataContext = invoice;
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) => Close();
}
