using System.Windows;
using Cashier.Domain;

namespace Cashier.UI.Views;

public partial class LowStockProductsWindow : Window
{
    public LowStockProductsWindow(IEnumerable<Product> products)
    {
        InitializeComponent();
        LowStockGrid.ItemsSource = products;
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) => Close();
}
