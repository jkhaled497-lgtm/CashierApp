using System.Windows;
using System.Windows.Controls;
using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class DailySheetView : UserControl
{
    public DailySheetView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DailySheetViewModel viewModel)
            await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
