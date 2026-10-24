using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Cashier.Application;
using Cashier.Domain;
using Cashier.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public sealed partial class PurchaseDraftLine(Product product, decimal unitPrice) : ObservableObject
{
    public Product Product { get; } = product;
    public string ProductName => Product.Name;
    [ObservableProperty] private decimal unitPrice = unitPrice;
    [ObservableProperty] private decimal quantity = 1;
    [ObservableProperty] private decimal discountAmount;
    public decimal LineTotal => decimal.Round(Math.Max(0, UnitPrice * Quantity - DiscountAmount), 2);

    partial void OnUnitPriceChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
    partial void OnDiscountAmountChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
}

public sealed partial class PurchaseEntryViewModel(
    IPurchaseInvoiceService purchaseInvoices,
    ISupplierService suppliers,
    IProductService products,
    IMasterDataService masterData,
    IEditorDialogService editor,
    SessionManager session) : ObservableObject
{
    private const int PageSize = 200;

    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<PurchaseDraftLine> Items { get; } = [];
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.CreditCard];
    public bool CanCreatePurchase => session.Current is not null;
    public bool CanManageCatalog => session.Current?.CanManageCatalog == true;
    public decimal Subtotal => Money(Items.Sum(item => item.UnitPrice * item.Quantity));
    public decimal DiscountTotal => Money(Items.Sum(item => item.DiscountAmount));
    public decimal GrandTotal => Money(Math.Max(0, Subtotal - DiscountTotal));
    public decimal RemainingAmount => Money(Math.Max(0, GrandTotal - PaymentAmount));

    [ObservableProperty] private Supplier? selectedSupplier;
    [ObservableProperty] private Product? selectedProduct;
    [ObservableProperty] private PaymentMethod paymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private decimal paymentAmount;
    [ObservableProperty] private string status = string.Empty;
    private bool itemEventsAttached;

    partial void OnPaymentAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(RemainingAmount));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        EnsureItemChangeTracking();
        if (!CanCreatePurchase)
        {
            Status = "تتطلب فواتير الشراء صلاحية المدير أو مدير النظام.";
            return;
        }

        try
        {
            var selectedSupplierId = SelectedSupplier?.Id;
            var activeSuppliers = await suppliers.GetActiveAsync();
            Suppliers.Clear();
            foreach (var supplier in activeSuppliers)
                Suppliers.Add(supplier);
            SelectedSupplier = Suppliers.FirstOrDefault(supplier => supplier.Id == selectedSupplierId)
                ?? Suppliers.FirstOrDefault();

            Products.Clear();
            for (var pageNumber = 1; ; pageNumber++)
            {
                var page = await products.SearchPageAsync(new ProductSearch(), pageNumber, PageSize);
                foreach (var product in page.Items)
                    Products.Add(product);
                if (pageNumber >= page.TotalPages)
                    break;
            }
            Status = string.Empty;
        }
        catch (Exception exception)
        {
            Status = $"تعذر تحميل بيانات فاتورة الشراء: {exception.Message}";
        }
    }

    [RelayCommand]
    private void AddItem()
    {
        EnsureItemChangeTracking();
        if (SelectedProduct is null)
        {
            Status = "اختر منتجاً لإضافته إلى الفاتورة.";
            return;
        }
        Items.Add(new PurchaseDraftLine(SelectedProduct, SelectedProduct.CostPrice > 0 ? SelectedProduct.CostPrice : 1m));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(RemainingAmount));
        SelectedProduct = null;
        Status = string.Empty;
    }

    [RelayCommand]
    private void RemoveItem(PurchaseDraftLine? item)
    {
        if (item is not null)
            Items.Remove(item);
    }

    [RelayCommand]
    private async Task CreateProductAsync()
    {
        if (!CanManageCatalog)
        {
            Status = "إضافة المنتجات متاحة لمدير النظام فقط.";
            return;
        }

        try
        {
            var categories = await masterData.GetCategoriesAsync();
            var brands = await masterData.GetBrandsAsync();
            var product = new Product
            {
                Sku = $"SKU-{Guid.NewGuid():N}"[..12],
                Name = string.Empty,
                CostPrice = 1,
                SellingPrice = 1,
                IsActive = true
            };
            if (!editor.Show(product, "إضافة منتج", categories, brands, canManagePrices: true))
                return;

            await products.SaveAsync(product);
            var saved = await products.GetByIdAsync(product.Id);
            if (saved is null)
            {
                await LoadAsync();
                saved = Products.FirstOrDefault(item => item.Id == product.Id);
            }
            else
            {
                Products.Add(saved);
                var orderedProducts = Products.OrderBy(item => item.Name).ToArray();
                Products.Clear();
                foreach (var productItem in orderedProducts)
                    Products.Add(productItem);
            }
            SelectedProduct = saved;
            Status = "تم حفظ المنتج. اختره لإضافته إلى فاتورة الشراء.";
        }
        catch (Exception exception)
        {
            Status = $"تعذر إضافة المنتج: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanCreatePurchase)
        {
            Status = "تتطلب فواتير الشراء صلاحية المدير أو مدير النظام.";
            return;
        }
        if (SelectedSupplier is null)
        {
            Status = "اختر المورد أولاً.";
            return;
        }
        if (Items.Count == 0)
        {
            Status = "أضف منتجاً واحداً على الأقل إلى الفاتورة.";
            return;
        }
        if (Items.Any(item => item.UnitPrice <= 0 || item.Quantity <= 0 ||
                              item.DiscountAmount < 0 ||
                              item.DiscountAmount > item.UnitPrice * item.Quantity))
        {
            Status = "تحقق من أن السعر والكمية موجبان والخصم ضمن إجمالي الصنف.";
            return;
        }
        if (PaymentAmount < 0 || PaymentAmount > GrandTotal)
        {
            Status = "قيمة الدفع يجب أن تكون بين صفر وإجمالي الفاتورة.";
            return;
        }

        try
        {
            var invoice = await purchaseInvoices.CreateAsync(new CreatePurchaseInvoiceRequest(
                SelectedSupplier.Id,
                Items.Select(item => new PurchaseLineRequest(
                    item.Product.Id, item.UnitPrice, item.Quantity, item.DiscountAmount)).ToArray(),
                PaymentAmount,
                PaymentMethod,
                session.Current!.UserId));
            Status = $"تم حفظ فاتورة الشراء رقم {invoice.InvoiceNumber}.";
            Items.Clear();
            PaymentAmount = 0;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Status = $"تعذر حفظ فاتورة الشراء: {exception.Message}";
        }
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private void EnsureItemChangeTracking()
    {
        if (itemEventsAttached)
            return;
        itemEventsAttached = true;
        Items.CollectionChanged += ItemsOnCollectionChanged;
    }

    private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PurchaseDraftLine item in e.OldItems)
                item.PropertyChanged -= ItemOnPropertyChanged;
        if (e.NewItems is not null)
            foreach (PurchaseDraftLine item in e.NewItems)
                item.PropertyChanged += ItemOnPropertyChanged;
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(RemainingAmount));
    }

    private void ItemOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PurchaseDraftLine.UnitPrice) or nameof(PurchaseDraftLine.Quantity)
            or nameof(PurchaseDraftLine.DiscountAmount))
        {
            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(DiscountTotal));
            OnPropertyChanged(nameof(GrandTotal));
            OnPropertyChanged(nameof(RemainingAmount));
        }
    }
}

public sealed partial class PurchaseInvoicesViewModel(
    IPurchaseInvoiceService purchaseInvoices,
    ISupplierService suppliers,
    IPurchaseBillPrintService printService) : ObservableObject
{
    private int detailRequest;
    private int supplierRequest;

    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<PurchaseInvoiceLookupRow> InvoiceRows { get; } = [];
    public PageNavigator Pager { get; } = new();
    public PurchaseSupplierStatistics? Statistics { get; private set; }
    public PurchaseInvoice? Result { get; private set; }
    public bool HasResult => Result is not null;

    [ObservableProperty] private Supplier? selectedSupplier;
    [ObservableProperty] private PurchaseInvoiceLookupRow? selectedInvoice;
    [ObservableProperty] private string status = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var selectedSupplierId = SelectedSupplier?.Id;
            Suppliers.Clear();
            var activeSuppliers = await suppliers.GetAllAsync();
            foreach (var supplier in activeSuppliers)
                Suppliers.Add(supplier);
            SelectedSupplier = Suppliers.FirstOrDefault(supplier => supplier.Id == selectedSupplierId)
                ?? Suppliers.FirstOrDefault();
            if (SelectedSupplier is null)
                Status = "لا يوجد موردون نشطون لعرض فواتيرهم.";
        }
        catch (Exception exception)
        {
            Status = $"تعذر تحميل الموردين: {exception.Message}";
        }
    }

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        var request = ++supplierRequest;
        SelectedInvoice = null;
        Result = null;
        InvoiceRows.Clear();
        Pager.Update(1, 0);
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(HasResult));
        if (value is null)
        {
            InvoiceRows.Clear();
            Statistics = null;
            OnPropertyChanged(nameof(Statistics));
            return;
        }
        Pager.Bind(LoadPageAsync);
        _ = LoadSupplierSummaryAsync(value.Id, request);
        _ = LoadSelectedSupplierPageAsync();
    }

    partial void OnSelectedInvoiceChanged(PurchaseInvoiceLookupRow? value)
    {
        var request = ++detailRequest;
        if (value is null)
        {
            Result = null;
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(HasResult));
            return;
        }
        _ = LoadDetailsAsync(value.Id, request);
    }

    [RelayCommand]
    private void Print()
    {
        if (Result is null)
            return;
        try
        {
            printService.Print(Result);
            Status = string.Empty;
        }
        catch (Exception exception)
        {
            Status = $"تعذرت طباعة فاتورة الشراء: {exception.Message}";
        }
    }

    private async Task LoadSupplierSummaryAsync(int supplierId, int request)
    {
        try
        {
            var statistics = await purchaseInvoices.GetStatisticsAsync(supplierId);
            if (request != supplierRequest)
                return;
            Statistics = statistics;
            OnPropertyChanged(nameof(Statistics));
            Status = string.Empty;
        }
        catch (Exception exception)
        {
            if (request == supplierRequest)
                Status = $"تعذر تحميل إحصائيات المورد: {exception.Message}";
        }
    }

    private async Task LoadPageAsync(int pageNumber)
    {
        if (SelectedSupplier is null)
            return;
        var supplierId = SelectedSupplier.Id;
        try
        {
            var page = await purchaseInvoices.GetPageAsync(
                supplierId, pageNumber, PageNavigator.DefaultPageSize);
            if (SelectedSupplier?.Id != supplierId)
                return;
            InvoiceRows.Clear();
            foreach (var invoice in page.Items)
                InvoiceRows.Add(invoice);
            Pager.Update(page.PageNumber, page.TotalCount);
            SelectedInvoice = InvoiceRows.FirstOrDefault();
            if (SelectedInvoice is null)
            {
                Result = null;
                OnPropertyChanged(nameof(Result));
                OnPropertyChanged(nameof(HasResult));
                Status = "لا توجد فواتير شراء مسجلة لهذا المورد.";
            }
        }
        catch (Exception exception)
        {
            InvoiceRows.Clear();
            Result = null;
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(HasResult));
            Status = $"تعذر تحميل فواتير الشراء: {exception.Message}";
        }
    }

    private async Task LoadSelectedSupplierPageAsync()
    {
        while (Pager.IsLoading)
            await Task.Delay(25);
        await Pager.LoadAsync(1);
    }

    private async Task LoadDetailsAsync(long invoiceId, int request)
    {
        try
        {
            var invoice = await purchaseInvoices.GetByIdAsync(invoiceId);
            if (request != detailRequest)
                return;
            Result = invoice;
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(HasResult));
        }
        catch (Exception exception)
        {
            if (request == detailRequest)
                Status = $"تعذر تحميل تفاصيل فاتورة الشراء: {exception.Message}";
        }
    }
}
