using System.Windows.Input;
using Cashier.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cashier.UI;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime lastInput = DateTime.UtcNow;
    private string barcodeBuffer = string.Empty;
    private DateTime lastBarcodeKey = DateTime.MinValue;
    private const int IdleTimeoutMinutes = 15;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Cashier.SaleCompleted += OnSaleCompleted;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) => lastInput = DateTime.UtcNow;
        idleTimer.Tick += OnIdleTimerTick;
        Loaded += OnMainWindowLoaded;
        Closed += (_, _) =>
        {
            idleTimer.Stop();
            viewModel.Cashier.SaleCompleted -= OnSaleCompleted;
        };
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        idleTimer.Start();
        await ((ShellViewModel)DataContext).RefreshLowStockProductsAsync();
    }

    private async void OnSaleCompleted(object? sender, EventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
            await viewModel.RefreshLowStockProductsAsync();
    }

    private async void LowStockAlert_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
            return;

        await viewModel.RefreshLowStockProductsAsync();
        if (viewModel.LowStockProducts.Count > 0)
            new Views.LowStockProductsWindow(viewModel.LowStockProducts) { Owner = this }.ShowDialog();
    }

    private void NavigateToTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string targetHeader })
            return;
        var destination = MainTabs.Items.OfType<TabItem>()
            .FirstOrDefault(tab => string.Equals(tab.Header?.ToString(), targetHeader, StringComparison.Ordinal));
        if (destination is not null && destination.IsEnabled)
            MainTabs.SelectedItem = destination;
    }

    private void CloseApplication_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.OriginalSource != MainTabs || MainTabs.SelectedItem is not TabItem tab ||
            DataContext is not ShellViewModel viewModel)
            return;

        switch (tab.Header?.ToString())
        {
            case "الرئيسية": await viewModel.RefreshLowStockProductsAsync(); break;
            case "نقطة البيع": await viewModel.Cashier.LoadAsync(); break;
            case "المنتجات": await viewModel.Products.LoadCommand.ExecuteAsync(null); break;
            case "العملاء": await viewModel.Customers.LoadCommand.ExecuteAsync(null); break;
            case "الموردون": await viewModel.Suppliers.LoadCommand.ExecuteAsync(null); break;
            case "فاتورة شراء": await viewModel.PurchaseEntry.LoadCommand.ExecuteAsync(null); break;
            case "فواتير الشراء": await viewModel.PurchaseInvoices.LoadCommand.ExecuteAsync(null); break;
            case "الموظفون": await viewModel.Employees.LoadCommand.ExecuteAsync(null); break;
            case "الفئات والعلامات التجارية": await viewModel.MasterData.LoadCommand.ExecuteAsync(null); break;
            case "المخزون": await viewModel.Inventory.LoadCommand.ExecuteAsync(null); break;
            case "المستخدمون": await viewModel.Users.LoadCommand.ExecuteAsync(null); break;
            case "الشيت اليومي": await viewModel.DailySheet.LoadCommand.ExecuteAsync(null); break;
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        lastInput = DateTime.UtcNow;
        if (Keyboard.FocusedElement is TextBox or PasswordBox)
            return;
        if (e.Key == Key.Enter)
        {
            if (barcodeBuffer.Length >= 6 && (DateTime.UtcNow - lastBarcodeKey).TotalMilliseconds < 200)
            {
                var barcode = barcodeBuffer;
                barcodeBuffer = string.Empty;
                e.Handled = true;
                await ((ShellViewModel)DataContext).Cashier.AddScannedBarcodeAsync(barcode);
            }
            else
            {
                barcodeBuffer = string.Empty;
            }
            return;
        }
        var now = DateTime.UtcNow;
        if ((now - lastBarcodeKey).TotalMilliseconds > 120)
            barcodeBuffer = string.Empty;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var character = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            >= Key.A and <= Key.Z => key.ToString(),
            Key.OemMinus => "-",
            _ => string.Empty
        };
        if (character.Length > 0 && barcodeBuffer.Length < 64)
            barcodeBuffer += character;
        lastBarcodeKey = now;
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        if ((DateTime.UtcNow - lastInput).TotalMinutes < IdleTimeoutMinutes)
            return;
        idleTimer.Stop();
        App.Services.GetRequiredService<Cashier.Application.SessionManager>().SignOut();
        var login = App.Services.GetRequiredService<Cashier.UI.Views.LoginWindow>();
        System.Windows.Application.Current.MainWindow = login;
        login.Show();
        Close();
    }
}
