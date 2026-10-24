using System.Collections.ObjectModel;
using Cashier.Application;
using Cashier.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public sealed partial class DailySheetViewModel(
    IDailySheetService dailySheet,
    IStockAdjustmentService stock,
    IProductService products,
    ICustomerService customers,
    ISupplierService suppliers,
    SessionManager session) : ObservableObject
{
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<DailyProduct> DailyProducts { get; } = [];
    public ObservableCollection<StockAdjustment> WarehouseMovements { get; } = [];
    public ObservableCollection<CustomerLedgerEntry> CustomerMovements { get; } = [];
    public ObservableCollection<SupplierBalanceEntry> SupplierMovements { get; } = [];
    public IReadOnlyList<StockAdjustmentType> WarehouseMovementTypes { get; } =
        [StockAdjustmentType.WarehouseIn, StockAdjustmentType.TransferToShop];
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.CreditCard];
    public decimal SaleTotal => SelectedProduct is null
        ? 0
        : Math.Round(SelectedProduct.SellingPrice * SaleQuantity - SaleDiscountAmount, 2, MidpointRounding.AwayFromZero);
    public decimal SaleEffectiveAmountPaid => SaleAmountPaid;
    public decimal SaleRemaining => Math.Max(0, SaleTotal - SaleEffectiveAmountPaid);
    public decimal EditedSaleTotal => SelectedDailyProduct is null
        ? 0
        : Math.Round(
            (SelectedDailyProduct.UnitSellingPrice ?? SelectedDailyProduct.Product.SellingPrice) *
            EditedSaleQuantity - EditedSaleDiscountAmount,
            2,
            MidpointRounding.AwayFromZero);
    public decimal EditedSaleRemaining => SelectedDailyProduct is null
        ? 0
        : Math.Max(0, EditedSaleTotal - EditedSaleAmountPaid);
    public bool CanManageWarehouse => session.Current?.CanManageCatalog == true;
    public bool CanManageSupplierAccounts => session.Current?.CanManageCatalog == true;

    [ObservableProperty] private DateTime selectedDate = DateTime.Today;
    [ObservableProperty] private Product? selectedProduct;
    [ObservableProperty] private decimal saleQuantity = 1;
    [ObservableProperty] private decimal saleDiscountAmount;
    [ObservableProperty] private decimal saleAmountPaid;
    [ObservableProperty] private PaymentMethod salePaymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private Customer? saleCustomer;
    [ObservableProperty] private DailyProduct? selectedDailyProduct;
    [ObservableProperty] private decimal editedSaleQuantity = 1;
    [ObservableProperty] private decimal editedSaleDiscountAmount;
    [ObservableProperty] private decimal editedSaleAmountPaid;
    [ObservableProperty] private PaymentMethod editedSalePaymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private Customer? editedSaleCustomer;
    [ObservableProperty] private Product? warehouseProduct;
    [ObservableProperty] private StockAdjustmentType warehouseMovementType = StockAdjustmentType.WarehouseIn;
    [ObservableProperty] private decimal warehouseQuantity = 1;
    [ObservableProperty] private string? warehouseReason;
    [ObservableProperty] private StockAdjustment? selectedWarehouseMovement;
    [ObservableProperty] private decimal editedWarehouseQuantity = 1;
    [ObservableProperty] private string? editedWarehouseReason;
    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private Supplier? selectedSupplier;
    [ObservableProperty] private decimal accountAmount;
    [ObservableProperty] private PaymentMethod accountPaymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private string? accountNote;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isLoading;

    partial void OnSelectedDailyProductChanged(DailyProduct? value)
    {
        EditedSaleQuantity = value?.Quantity ?? 1;
        EditedSaleDiscountAmount = value?.DiscountAmount ?? 0;
        EditedSaleAmountPaid = value?.AmountPaid ?? 0;
        EditedSalePaymentMethod = value?.PaymentMethod ?? PaymentMethod.Cash;
        EditedSaleCustomer = value?.CustomerId is { } id
            ? Customers.FirstOrDefault(customer => customer.Id == id)
            : null;
        OnPropertyChanged(nameof(EditedSaleTotal));
        OnPropertyChanged(nameof(EditedSaleRemaining));
    }

    partial void OnSelectedProductChanged(Product? value) => NotifySaleAmounts();
    partial void OnSaleQuantityChanged(decimal value) => NotifySaleAmounts();
    partial void OnSaleDiscountAmountChanged(decimal value) => NotifySaleAmounts();
    partial void OnSaleAmountPaidChanged(decimal value) => NotifySaleAmounts();
    partial void OnEditedSaleQuantityChanged(decimal value) => NotifyEditedSaleAmounts();
    partial void OnEditedSaleDiscountAmountChanged(decimal value) => NotifyEditedSaleAmounts();
    partial void OnEditedSaleAmountPaidChanged(decimal value) => NotifyEditedSaleAmounts();

    partial void OnSelectedWarehouseMovementChanged(StockAdjustment? value)
    {
        EditedWarehouseQuantity = value?.Quantity ?? 1;
        EditedWarehouseReason = value?.Reason;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        Status = "جار تحميل سجلات الشيت اليومي...";
        try
        {
            var from = BusinessTime.ToUtc(SelectedDate.Date);
            var to = BusinessTime.ToUtc(SelectedDate.Date.AddDays(1));
            var selectedProductId = SelectedProduct?.Id;
            var warehouseProductId = WarehouseProduct?.Id;
            var saleCustomerId = SaleCustomer?.Id;
            var editedSaleCustomerId = EditedSaleCustomer?.Id;
            var selectedCustomerId = SelectedCustomer?.Id;
            var selectedSupplierId = SelectedSupplier?.Id;

            var productRows = await products.SearchAsync(new ProductSearch());
            Replace(Products, productRows);
            SelectedProduct = Products.FirstOrDefault(product => product.Id == selectedProductId);
            WarehouseProduct = Products.FirstOrDefault(product => product.Id == warehouseProductId);

            var customerRows = await LoadAllCustomersAsync();
            Replace(Customers, customerRows);
            SaleCustomer = Customers.FirstOrDefault(customer => customer.Id == saleCustomerId);
            EditedSaleCustomer = Customers.FirstOrDefault(customer => customer.Id == editedSaleCustomerId);
            SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == selectedCustomerId);

            Replace(Suppliers, await suppliers.GetActiveAsync());
            SelectedSupplier = Suppliers.FirstOrDefault(supplier => supplier.Id == selectedSupplierId);

            Replace(DailyProducts, await dailySheet.GetDailyProductsAsync(from, to));
            Replace(WarehouseMovements, await dailySheet.GetWarehouseMovementsAsync(from, to));
            Replace(CustomerMovements, await dailySheet.GetCustomerMovementsAsync(from, to));
            Replace(SupplierMovements, await dailySheet.GetSupplierMovementsAsync(from, to));
            Status = $"تم تحميل سجلات يوم {SelectedDate:dd-MM-yyyy}.";
        }
        catch (Exception exception)
        {
            Status = $"تعذر تحميل الشيت اليومي: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddDailyProductAsync()
    {
        if (!IsCurrentSheetDate())
            return;
        if (SelectedProduct is null)
        {
            Status = "اختر منتجاً من منتجات المحل.";
            return;
        }
        if (session.Current is null)
        {
            Status = "انتهت الجلسة؛ سجل الدخول مجدداً.";
            return;
        }

        try
        {
            await dailySheet.CreateDailyProductAsync(new CreateDailyProductRequest(
                SelectedProduct.Id,
                SaleQuantity,
                SaleCustomer?.Id,
                session.Current.UserId,
                SaleAmountPaid,
                SalePaymentMethod,
                SaleDiscountAmount));
            Status = "تم تسجيل البيع وخصم الكمية من رصيد المحل.";
            SaleQuantity = 1;
            SaleDiscountAmount = 0;
            SaleAmountPaid = 0;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل بيع المنتج: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateDailyProductAsync()
    {
        if (SelectedDailyProduct is null)
        {
            Status = "اختر حركة بيع لتعديلها.";
            return;
        }

        try
        {
            await dailySheet.UpdateDailyProductAsync(
                SelectedDailyProduct.Id,
                EditedSaleQuantity,
                EditedSaleCustomer?.Id,
                EditedSaleAmountPaid,
                EditedSalePaymentMethod,
                discountAmount: EditedSaleDiscountAmount);
            Status = "تم تعديل حركة البيع وتحديث رصيد المحل.";
            SelectedDailyProduct = null;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Status = $"تعذر تعديل حركة البيع: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task AddWarehouseMovementAsync()
    {
        if (!CanManageWarehouse)
        {
            Status = "حركات المخزن متاحة لمدير النظام فقط.";
            return;
        }
        if (!IsCurrentSheetDate())
            return;
        if (WarehouseProduct is null)
        {
            Status = "اختر منتجاً للمخزن.";
            return;
        }

        try
        {
            await stock.AdjustAsync(WarehouseProduct.Id, WarehouseMovementType, WarehouseQuantity,
                session.Current?.EmployeeId, WarehouseReason);
            Status = "تم تسجيل حركة المخزن.";
            WarehouseQuantity = 1;
            WarehouseReason = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل حركة المخزن: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateWarehouseMovementAsync()
    {
        if (!CanManageWarehouse)
        {
            Status = "تعديل حركات المخزن متاح لمدير النظام فقط.";
            return;
        }
        if (SelectedWarehouseMovement is null)
        {
            Status = "اختر حركة مخزن لتعديلها.";
            return;
        }

        try
        {
            await stock.UpdateAsync(SelectedWarehouseMovement.Id, EditedWarehouseQuantity, EditedWarehouseReason);
            Status = "تم تعديل حركة المخزن وتحديث الأرصدة.";
            SelectedWarehouseMovement = null;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Status = $"تعذر تعديل حركة المخزن: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task AddCustomerChargeAsync()
    {
        if (!IsCurrentSheetDate())
            return;
        if (SelectedCustomer is null || session.Current is null)
        {
            Status = "اختر عميلاً وتأكد من أن الجلسة ما زالت مفتوحة.";
            return;
        }

        try
        {
            await customers.RecordBalanceChargeAsync(
                SelectedCustomer.Id, AccountAmount, session.Current.UserId, AccountNote);
            await RefreshAccountMovementsAsync("تم تسجيل استحقاق العميل.");
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل استحقاق العميل: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PayCustomerBalanceAsync()
    {
        if (!IsCurrentSheetDate())
            return;
        if (SelectedCustomer is null || session.Current is null)
        {
            Status = "اختر عميلاً وتأكد من أن الجلسة ما زالت مفتوحة.";
            return;
        }

        try
        {
            await customers.RecordBalancePaymentAsync(
                SelectedCustomer.Id, AccountAmount, session.Current.UserId, AccountPaymentMethod, AccountNote);
            await RefreshAccountMovementsAsync("تم تسجيل سداد العميل.");
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل سداد العميل: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task AddSupplierChargeAsync()
    {
        if (!CanManageSupplierAccounts)
        {
            Status = "تسجيل حركات الموردين متاح لمدير النظام فقط.";
            return;
        }
        if (!IsCurrentSheetDate())
            return;
        if (SelectedSupplier is null || session.Current is null)
        {
            Status = "اختر مورداً وتأكد من أن الجلسة ما زالت مفتوحة.";
            return;
        }

        try
        {
            await suppliers.ChangeBalanceAsync(SelectedSupplier.Id, AccountAmount, SupplierBalanceEntryType.Charge,
                session.Current.UserId, AccountPaymentMethod, AccountNote);
            await RefreshAccountMovementsAsync("تم تسجيل مستحق المورد.");
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل مستحق المورد: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PaySupplierBalanceAsync()
    {
        if (!CanManageSupplierAccounts)
        {
            Status = "تسجيل حركات الموردين متاح لمدير النظام فقط.";
            return;
        }
        if (!IsCurrentSheetDate())
            return;
        if (SelectedSupplier is null || session.Current is null)
        {
            Status = "اختر مورداً وتأكد من أن الجلسة ما زالت مفتوحة.";
            return;
        }

        try
        {
            await suppliers.ChangeBalanceAsync(SelectedSupplier.Id, AccountAmount, SupplierBalanceEntryType.Payment,
                session.Current.UserId, AccountPaymentMethod, AccountNote);
            await RefreshAccountMovementsAsync("تم تسجيل سداد المورد.");
        }
        catch (Exception exception)
        {
            Status = $"تعذر تسجيل سداد المورد: {exception.Message}";
        }
    }

    private async Task RefreshAccountMovementsAsync(string successMessage)
    {
        var from = BusinessTime.ToUtc(SelectedDate.Date);
        var to = BusinessTime.ToUtc(SelectedDate.Date.AddDays(1));
        AccountAmount = 0;
        AccountNote = string.Empty;
        Replace(CustomerMovements, await dailySheet.GetCustomerMovementsAsync(from, to));
        Replace(SupplierMovements, await dailySheet.GetSupplierMovementsAsync(from, to));
        Status = successMessage;
    }

    private async Task<IReadOnlyList<Customer>> LoadAllCustomersAsync()
    {
        var result = new List<Customer>();
        var page = 1;
        PageResult<Customer> currentPage;
        do
        {
            currentPage = await customers.SearchPageAsync(null, page, 200);
            result.AddRange(currentPage.Items);
            page++;
        } while (page <= currentPage.TotalPages);
        return result;
    }

    private bool IsCurrentSheetDate()
    {
        if (SelectedDate.Date == BusinessTime.ToEgyptLocal(DateTimeOffset.UtcNow).Date)
            return true;
        Status = "إضافة الحركات متاحة لتاريخ اليوم فقط؛ اختر تاريخ اليوم لتسجيل حركة جديدة.";
        return false;
    }

    private void NotifySaleAmounts()
    {
        OnPropertyChanged(nameof(SaleTotal));
        OnPropertyChanged(nameof(SaleEffectiveAmountPaid));
        OnPropertyChanged(nameof(SaleRemaining));
    }

    private void NotifyEditedSaleAmounts()
    {
        OnPropertyChanged(nameof(EditedSaleTotal));
        OnPropertyChanged(nameof(EditedSaleRemaining));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
