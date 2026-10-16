using Cashier.Application;
using Cashier.Domain;
using Cashier.Infrastructure;
using Cashier.UI.Services;
using Cashier.UI.ViewModels;
using Cashier.UI.Views;
using AutoUpdaterDotNET;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Globalization;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cashier.UI;

public partial class App : System.Windows.Application
{
    private static readonly (string Key, string Light, string Dark)[] ThemeColors =
    [
        ("WindowBackgroundBrush", "#FFF1F5FB", "#FF121A29"),
        ("CardBackgroundBrush", "#FFFFFFFF", "#FF1E2838"),
        ("CardBackgroundAltBrush", "#FFF9FBFE", "#FF222C3E"),
        ("SurfaceBrush", "#FFE8EEF7", "#FF1B2131"),
        ("PrimaryTextBrush", "#FF172033", "#FFF3F7FF"),
        ("SubtleBrush", "#FF53627A", "#FF9AA9C5"),
        ("AlertBrush", "#FFB42318", "#FFFF8A80"),
        ("GridHeaderBrush", "#FFE4EAF3", "#FF2B3649"),
        ("GridAlternateBrush", "#FFF4F7FB", "#FF232C3D"),
        ("GridHoverBrush", "#FFE5EDFF", "#FF2F3E59"),
        ("InputBorderBrush", "#FFCBD5E1", "#FF465774"),
        ("PanelBorderBrush", "#FFD7E0ED", "#FF2E3A52")
    ];

    private IHost? host;
    public static IServiceProvider Services => ((App)System.Windows.Application.Current).host!.Services;

    public static void ApplyThemePalette(bool isDarkTheme, bool initialize = false)
    {
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, light, dark) in ThemeColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(isDarkTheme ? dark : light);
            if (initialize || resources[key] is not SolidColorBrush brush || brush.IsFrozen)
                resources[key] = new SolidColorBrush(color);
            else
                brush.Color = color;
        }

        resources[SystemColors.WindowBrushKey] = resources["CardBackgroundBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["PrimaryTextBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["AccentBrush"];
        resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
    }

    public static void ApplyAccentPalette(string paletteKey)
    {
        var (accent, deep, soft) = paletteKey switch
        {
            "teal" => ("#FF20BFA9", "#FF008E85", "#FFA5F0E3"),
            "violet" => ("#FFA78BFA", "#FF7C3AED", "#FFDDD2FF"),
            "amber" => ("#FFF59E0B", "#FFD97706", "#FFFFE0A3"),
            _ => ("#FF4F8CFF", "#FF2E5BFF", "#FFC0D8FF")
        };
        var resources = System.Windows.Application.Current.Resources;
        resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
        resources["AccentDeepBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(deep));
        resources["AccentSoftBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(soft));
        resources["AccentGradientBrush"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(accent),
            (Color)ColorConverter.ConvertFromString(deep),
            new Point(0, 0),
            new Point(1, 1));
        resources[SystemColors.HighlightBrushKey] = resources["AccentBrush"];
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ar-EG");
        ApplyThemePalette(isDarkTheme: true, initialize: true);
        ApplyAccentPalette("blue");
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
        try
        {
            host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration
                        .AddJsonFile(
                            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                            optional: true,
                            reloadOnChange: false)
                        .AddJsonFile(
                            Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                "Cashier",
                                "appsettings.json"),
                            optional: true,
                            reloadOnChange: false)
                        .AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    var connectionString = context.Configuration.GetConnectionString("Cashier")
                        ?? throw new InvalidOperationException(
                            "سلسلة اتصال قاعدة بيانات Cashier غير موجودة. أضفها في C:\\ProgramData\\Cashier\\appsettings.json أو في متغير البيئة ConnectionStrings__Cashier.");
                    services.AddCashierInfrastructure(connectionString);
                    services.AddSingleton<SessionManager>();
                    services.AddSingleton<IPrintService, ThermalReceiptPrintService>();
                    services.AddSingleton<IPurchaseBillPrintService, PurchaseBillPrintService>();
                    services.AddSingleton<IEditorDialogService, EditorDialogService>();
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<CashierViewModel>();
                    services.AddTransient<ProductsViewModel>();
                    services.AddTransient<CustomersViewModel>();
                    services.AddTransient<SuppliersViewModel>();
                    services.AddTransient<EmployeesViewModel>();
                    services.AddTransient<MasterDataViewModel>();
                    services.AddTransient<InventoryViewModel>();
                    services.AddTransient<UsersViewModel>();
                    services.AddTransient<InvoiceLookupViewModel>();
                    services.AddTransient<PurchaseEntryViewModel>();
                    services.AddTransient<PurchaseInvoicesViewModel>();
                    services.AddTransient<ReportsViewModel>();
                    services.AddTransient<ShellViewModel>();
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<MainWindow>();
                })
                .Build();
            await host.StartAsync();
            var contextFactory = Services.GetRequiredService<IDbContextFactory<CashierDbContext>>();
            await using (var db = await contextFactory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }
            using (var scope = Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IAuthService>().EnsureInitialAdminAsync();
            await using (var db = await contextFactory.CreateDbContextAsync())
            {
                if (!await db.Customers.AnyAsync(x => x.IsWalkIn))
                {
                    db.Customers.Add(new Customer
                    {
                        CustomerCode = "CUST-WALKIN",
                        FullName = "عميل نقدي",
                        IsWalkIn = true,
                        CreditLimit = 0
                    });
                    await db.SaveChangesAsync();
                }
            }
            var login = Services.GetRequiredService<LoginWindow>();
            MainWindow = login;
            login.Show();

            AutoUpdater.AppTitle = "Cashier";
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.Forced;
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.ReportErrors = false;
            AutoUpdater.ClearAppDirectory = true;
            AutoUpdater.InstallationPath = AppContext.BaseDirectory;
            AutoUpdater.DownloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cashier",
                "Updates");
            Directory.CreateDirectory(AutoUpdater.DownloadPath);
            AutoUpdater.Start(
                "https://raw.githubusercontent.com/jkhaled497-lgtm/CashierApp/main/version.xml",
                typeof(App).Assembly);
        }
        catch (Exception exception)
        {
            host?.Dispose();
            host = null;
            System.Windows.MessageBox.Show(
                $"تعذر تشغيل التطبيق أو الاتصال بقاعدة بيانات SQL Server. تحقق من الخادم وقاعدة البيانات والصلاحيات وشهادة TLS.\n\nالتفاصيل: {exception.Message}",
                "تعذر الاتصال بقاعدة البيانات",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync();
            host.Dispose();
        }
        base.OnExit(e);
    }
}
