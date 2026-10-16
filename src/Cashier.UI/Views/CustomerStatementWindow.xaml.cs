using System.Windows;
using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class CustomerStatementWindow : Window
{
    public CustomerStatementWindow(CustomersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e) => Close();
}
