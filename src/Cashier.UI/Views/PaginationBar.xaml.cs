using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cashier.UI.Views;

public partial class PaginationBar : UserControl
{
    public static readonly DependencyProperty SummaryProperty =
        DependencyProperty.Register(nameof(Summary), typeof(string), typeof(PaginationBar), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HasPreviousPageProperty =
        DependencyProperty.Register(nameof(HasPreviousPage), typeof(bool), typeof(PaginationBar), new PropertyMetadata(false));
    public static readonly DependencyProperty HasNextPageProperty =
        DependencyProperty.Register(nameof(HasNextPage), typeof(bool), typeof(PaginationBar), new PropertyMetadata(false));
    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.Register(nameof(RefreshCommand), typeof(ICommand), typeof(PaginationBar));
    public static readonly DependencyProperty PreviousCommandProperty =
        DependencyProperty.Register(nameof(PreviousCommand), typeof(ICommand), typeof(PaginationBar));
    public static readonly DependencyProperty NextCommandProperty =
        DependencyProperty.Register(nameof(NextCommand), typeof(ICommand), typeof(PaginationBar));

    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public bool HasPreviousPage { get => (bool)GetValue(HasPreviousPageProperty); set => SetValue(HasPreviousPageProperty, value); }
    public bool HasNextPage { get => (bool)GetValue(HasNextPageProperty); set => SetValue(HasNextPageProperty, value); }
    public ICommand? RefreshCommand { get => (ICommand?)GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
    public ICommand? PreviousCommand { get => (ICommand?)GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }
    public ICommand? NextCommand { get => (ICommand?)GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }

    public PaginationBar() => InitializeComponent();
}
