using Cashier.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Cashier.UI.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView() => InitializeComponent();

    private void OpenSupplierRecords_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SuppliersViewModel viewModel || viewModel.Selected is null)
        {
            if (DataContext is SuppliersViewModel suppliers)
                suppliers.Status = "اختر المورد أولاً لعرض بياناته.";
            return;
        }

        var initialTab = (sender as Button)?.Tag?.ToString() == "Invoices" ? 1 : 0;
        var window = new SupplierRecordsWindow(viewModel, initialTab)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }
}
