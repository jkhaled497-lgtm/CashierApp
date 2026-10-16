using Cashier.UI.ViewModels;

namespace Cashier.UI.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    private void ProductsGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (DataContext is Cashier.UI.ViewModels.ProductsViewModel viewModel)
            e.Row.Header = viewModel.GetDisplayRowNumber(e.Row.GetIndex()).ToString();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        //if (DataContext is Cashier.UI.ViewModels.ProductsViewModel viewModel)
        //{
        //    //  „—Ì— «·‰’ «·„ﬂ Ê» ›Ì Õﬁ· «·»ÕÀ ≈·Ï «·‹ ViewModel
        //    //viewModel.Search(SearchTextBox.Text);
        //}
    }
}
