using Cashier.UI.ViewModels;
using System.Windows;

namespace Cashier.UI.Views;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private void OpenCustomerStatement_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomersViewModel viewModel || viewModel.Selected is null)
        {
            if (DataContext is CustomersViewModel customers)
                customers.Status = "اختر العميل أولاً لعرض كشف حسابه.";
            return;
        }

        var window = new CustomerStatementWindow(viewModel)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }
}
