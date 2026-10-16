using System.Windows;
using System.Windows.Controls;
using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class ReportsView : UserControl
{
    private bool hasLoaded;

    public ReportsView()
    {
        InitializeComponent();
    }

    private async void ReportsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (hasLoaded || DataContext is not ReportsViewModel viewModel)
            return;

        hasLoaded = true;
        await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
