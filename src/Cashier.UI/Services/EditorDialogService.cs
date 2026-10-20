using System.IO;
using Cashier.Domain;
using Cashier.UI.Views;

namespace Cashier.UI.Services;

public interface IEditorDialogService
{
    bool Show<T>(T entity, string title, IReadOnlyList<Category>? categories = null,
        IReadOnlyList<Brand>? brands = null, IReadOnlyList<Employee>? employees = null, bool canManagePrices = false);
}

public sealed class EditorDialogService : IEditorDialogService
{
    public bool Show<T>(T entity, string title, IReadOnlyList<Category>? categories = null,
        IReadOnlyList<Brand>? brands = null, IReadOnlyList<Employee>? employees = null, bool canManagePrices = false)
    {
        var dialog = new EditorDialogWindow(entity!, title, categories ?? [], brands ?? [], employees ?? [], canManagePrices)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true;
    }
}

public sealed class UserEditorForm
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Cashier;
    public Employee? Employee { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsNew => Id == 0;
    public Array Roles { get; } = Enum.GetValues<UserRole>();
}

public sealed class SupplierEditorForm
{
    public Supplier Supplier { get; set; } = new() { Name = string.Empty };
    public decimal OpeningBalance { get; set; }
    public bool IsNew => Supplier.Id == 0;
}

public sealed class CustomerEditorForm
{
    public Customer Customer { get; set; } = new() { CustomerCode = string.Empty, FullName = string.Empty };
    public decimal OpeningBalance { get; set; }
    public bool IsNew => Customer.Id == 0;
}

public sealed class ProductImageSourceConverter : System.Windows.Data.IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 400;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
