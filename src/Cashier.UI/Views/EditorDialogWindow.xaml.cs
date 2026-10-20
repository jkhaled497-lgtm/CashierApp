using Cashier.Domain;
using Cashier.UI.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;
using Wpf.Ui.Controls;

namespace Cashier.UI.Views;

public partial class EditorDialogWindow : FluentWindow
{
    public EditorDialogWindow(object entity, string title, IReadOnlyList<Category> categories,
        IReadOnlyList<Brand> brands, IReadOnlyList<Employee> employees, bool canManagePrices)
    {
        InitializeComponent();
        DataContext = new EditorDialogContext(entity, title, categories, brands, employees, canManagePrices);
        var templateKey = entity switch
        {
            Product => "ProductEditorTemplate",
            CustomerEditorForm => "CustomerEditorTemplate",
            Category => "CategoryEditorTemplate",
            Brand => "BrandEditorTemplate",
            Employee => "EmployeeEditorTemplate",
            SupplierEditorForm => "SupplierEditorTemplate",
            UserEditorForm => "UserEditorTemplate",
            _ => throw new ArgumentException($"Unsupported editor form: {entity.GetType().Name}", nameof(entity))
        };
        EditorContent.ContentTemplate = (DataTemplate)Resources[templateKey];
    }

    private void FocusBarcodeInput_Click(object sender, RoutedEventArgs e)
    {
        var input = FindTemplateElement<System.Windows.Controls.TextBox>("BarcodeInput");
        input.Focus();
        input.SelectAll();
    }

    private void BarcodeInput_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (sender is not System.Windows.Controls.TextBox input) return;
        input.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
        _ = input.Focus();
    }

    private void ChooseProductImage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorDialogContext { Entity: Product product }) return;
        var picker = new OpenFileDialog
        {
            Title = "اختر صورة المنتج",
            Filter = "ملفات الصور|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        var extension = Path.GetExtension(picker.FileName).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif") ||
            new FileInfo(picker.FileName).Length > 10 * 1024 * 1024)
        {
            System.Windows.MessageBox.Show(this, "اختر صورة صالحة لا يتجاوز حجمها 10 ميغابايت.",
                "صورة غير صالحة", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(picker.FileName);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 640;
            image.EndInit();
            image.Freeze();

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cashier", "ProductImages");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
            File.Copy(picker.FileName, destination);
            product.ImagePath = destination;
            FindTemplateElement<System.Windows.Controls.Image>("ProductImagePreview").Source = image;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"تعذر حفظ صورة المنتج: {exception.Message}",
                "تعذر تحميل الصورة", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private T FindTemplateElement<T>(string name) where T : FrameworkElement =>
        FindVisualChild<T>(EditorContent, name)
        ?? throw new InvalidOperationException($"لم يتم العثور على عنصر النموذج المطبق: {name}.");

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && element.Name == name)
                return element;
            if (FindVisualChild<T>(child, name) is { } descendant)
                return descendant;
        }
        return null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorDialogContext context)
            return;

        if (HasValidationErrors(this))
        {
            System.Windows.MessageBox.Show(this, "راجع الحقول الرقمية؛ أدخل قيماً صحيحة قبل الحفظ.",
                "بيانات غير صالحة", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var validationMessage = context.Entity switch
        {
            Product product when string.IsNullOrWhiteSpace(product.Name)
                => "اسم المنتج مطلوب.",
            Category category when string.IsNullOrWhiteSpace(category.Name)
                => "اسم الفئة مطلوب.",
            Brand brand when string.IsNullOrWhiteSpace(brand.Name)
                => "اسم العلامة التجارية مطلوب.",
            SupplierEditorForm supplier when string.IsNullOrWhiteSpace(supplier.Supplier.Name)
                => "اسم المورد مطلوب.",
            SupplierEditorForm supplier when supplier.OpeningBalance < 0
                => "الرصيد الافتتاحي لا يمكن أن يكون سالباً.",
            CustomerEditorForm customer when customer.OpeningBalance < 0
                => "المبلغ المستحق لا يمكن أن يكون سالباً.",
            Employee employee when string.IsNullOrWhiteSpace(employee.FullName)
                => "اسم الموظف مطلوب.",
            Product product when product.CostPrice < 0 || product.SellingPrice < 0 ||
                                 product.CurrentStock < 0 || product.WarehouseStock < 0 || product.MinStockAlert < 0
                => "تحقق من الأسعار والمخزون؛ يجب أن تكون القيم غير سالبة.",
            Product { CostPrice: <= 0 } when context.CanManagePrices
                => "سعر التكلفة مطلوب.",
            Product { SellingPrice: <= 0 } when context.CanManagePrices
                => "سعر البيع مطلوب.",
            Product product when context.CanManagePrices && product.SellingPrice < product.CostPrice
                => "يجب أن يكون سعر البيع أكبر من أو يساوي سعر التكلفة.",
            Employee employee when employee.BasicSalary < 0
                => "الراتب الأساسي لا يمكن أن يكون سالباً.",
            UserEditorForm user when string.IsNullOrWhiteSpace(user.Username) || user.Username.Trim().Length < 3
                => "يجب أن يتكون اسم المستخدم من 3 أحرف على الأقل.",
            UserEditorForm user when user.IsNew && user.Password.Length < 12
                => "يجب أن تتكون كلمة المرور من 12 حرفاً على الأقل.",
            UserEditorForm user when !user.IsNew && !string.IsNullOrEmpty(user.Password) && user.Password.Length < 12
                => "كلمة المرور الجديدة يجب أن تتكون من 12 حرفاً على الأقل.",
            _ => null
        };
        if (validationMessage is not null)
        {
            System.Windows.MessageBox.Show(this, validationMessage, "يرجى استكمال البيانات",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static bool HasValidationErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element))
            return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (HasValidationErrors(VisualTreeHelper.GetChild(element, index)))
                return true;
        return false;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox &&
            passwordBox.Tag is UserEditorForm form)
            form.Password = passwordBox.Password;
    }
}

public sealed record EditorDialogContext(
    object Entity,
    string Title,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Brand> Brands,
    IReadOnlyList<Employee> Employees,
    bool CanManagePrices)
{
    public Array Units { get; } = Enum.GetValues<ProductUnit>();
}
