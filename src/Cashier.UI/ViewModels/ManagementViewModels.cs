using System.Collections.ObjectModel;
using System.Windows.Media;
using Cashier.Application;
using Cashier.Domain;
using Cashier.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cashier.UI.ViewModels;

public partial class ProductsViewModel(
    IProductService products,
    IMasterDataService masterData,
    SessionManager session,
    IEditorDialogService editor) : ObservableObject
{
    public ObservableCollection<Product> Items { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public Array Units { get; } = Enum.GetValues<ProductUnit>();
    public bool CanEdit => session.Current?.CanManageCatalog == true;
    public bool CanManagePrices => session.Current?.Role == UserRole.Admin;
    public bool HasSelectedProduct => Selected is not null;
    public PageNavigator Pager { get; } = new();
    private string appliedSearchText = string.Empty;
    [ObservableProperty] private Product? selected;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string status = string.Empty;

    public int GetDisplayRowNumber(int index) => (Pager.PageNumber - 1) * PageNavigator.DefaultPageSize + index + 1;

    partial void OnSelectedChanged(Product? value) => OnPropertyChanged(nameof(HasSelectedProduct));

    [RelayCommand]
    private async Task SearchAsync()
    {
        Status = string.Empty;
        try
        {
            appliedSearchText = SearchText.Trim();

            Pager.Bind(LoadPageAsync);
            while (Pager.IsLoading)
                await Task.Delay(25);
            await LoadPageAsync(1);
        }
        catch (Exception exception)
        {
            Status = $"تعذر البحث عن المنتجات: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        appliedSearchText = SearchText.Trim();
        Pager.Bind(LoadPageAsync);
        await Pager.LoadAsync(1);
        try
        {
            Categories.Clear();
            foreach (var item in await masterData.GetCategoriesAsync()) if (item.IsActive) Categories.Add(item);
            Brands.Clear();
            foreach (var item in await masterData.GetBrandsAsync()) if (item.IsActive) Brands.Add(item);
        }
        catch (Exception e) { Status = $"تعذر تحميل بيانات التصنيف: {e.Message}"; }
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await products.SearchPageAsync(new ProductSearch(appliedSearchText), page,
                PageNavigator.DefaultPageSize, includeInactive: CanManagePrices);
            Status = result.TotalCount == 0 && !string.IsNullOrWhiteSpace(appliedSearchText)
                ? "لا توجد منتجات تطابق كلمة البحث."
                : string.Empty;
            Selected = null;
            Items.Clear();
            foreach (var item in result.Items) Items.Add(item);
            Pager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل المنتجات: {e.Message}"; }
    }

    [RelayCommand]
    private async Task NewProductAsync()
    {
        if (!CanEdit) { Status = "تتطلب إدارة المنتجات صلاحية المدير أو مدير النظام."; return; }
        var product = new Product
        {
            Sku = $"SKU-{Guid.NewGuid():N}"[..12],
            Name = string.Empty,
            IsActive = CanManagePrices
        };
        if (!editor.Show(product, "إضافة منتج", Categories, Brands, canManagePrices: CanManagePrices)) return;
        await SaveProductAsync(product);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (!CanEdit || Selected is null) { Status = "اختر منتجاً وتأكد من امتلاكك صلاحية إدارة المنتجات."; return; }
        var editedProduct = Copy(Selected);
        if (editor.Show(editedProduct, "تعديل بيانات المنتج", Categories, Brands, canManagePrices: CanManagePrices))
            await SaveProductAsync(editedProduct);
    }

    private async Task SaveProductAsync(Product product)
    {
        if (CanManagePrices && product.SellingPrice < product.CostPrice)
        {
            Status = "يجب أن يكون سعر البيع أكبر من أو يساوي سعر التكلفة.";
            return;
        }
        if (!CanManagePrices)
        {
            if (product.Id == 0)
            {
                product.CostPrice = 0;
                product.SellingPrice = 0;
                product.IsActive = false;
            }
            product.TaxRate = 0;
        }
        try { await products.SaveAsync(product); Status = "تم حفظ بيانات المنتج."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حفظ المنتج: {e.Message}"; }
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (!CanEdit || Selected is null) { Status = "اختر منتجاً وتأكد من امتلاكك صلاحية إدارة المنتجات."; return; }
        if (Selected.Id == 0) { Items.Remove(Selected); Selected = null; return; }
        try { await products.DeleteAsync(Selected.Id); Status = "تم حذف المنتج (إيقافه مع الاحتفاظ بسجل الفواتير)."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حذف المنتج: {e.Message}"; }
    }

    private static Product Copy(Product product) => new()
    {
        Id = product.Id, Sku = product.Sku, Barcode = product.Barcode, Name = product.Name,
        Description = product.Description, CategoryId = product.CategoryId, BrandId = product.BrandId,
        CostPrice = product.CostPrice, SellingPrice = product.SellingPrice, MinStockAlert = product.MinStockAlert,
        CurrentStock = product.CurrentStock, WarehouseStock = product.WarehouseStock, TaxRate = product.TaxRate, Unit = product.Unit,
        ImagePath = product.ImagePath, IsActive = product.IsActive
    };
}

public partial class CustomersViewModel(
    ICustomerService customers,
    SessionManager session,
    IEditorDialogService editor) : ObservableObject
{
    private int? displayedCustomerId;

    public ObservableCollection<Customer> Items { get; } = [];
    public ObservableCollection<CustomerLedgerEntry> Ledger { get; } = [];
    public PageNavigator Pager { get; } = new();
    public PageNavigator LedgerPager { get; } = new();
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.CreditCard];
    public bool HasSelectedCustomer => Selected is not null;
    public int CustomerCount => Pager.TotalCount;
    public int CustomersWithBalanceCount => Items.Count(x => x.CurrentBalance > 0);
    public decimal CurrentPageBalanceTotal => Items.Sum(x => x.CurrentBalance);
    [ObservableProperty] private Customer? selected;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isDetailsVisible;
    [ObservableProperty] private decimal balancePaymentAmount;
    [ObservableProperty] private string? balancePaymentNote;
    [ObservableProperty] private PaymentMethod balancePaymentMethod = PaymentMethod.Cash;

    partial void OnSelectedChanged(Customer? value)
    {
        OnPropertyChanged(nameof(HasSelectedCustomer));
        if (value?.Id != displayedCustomerId)
            IsDetailsVisible = false;
        if (value is null)
            displayedCustomerId = null;
        _ = LoadLedgerAsync(value);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Pager.Bind(LoadPageAsync);
        await Pager.LoadAsync(1);
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await customers.SearchPageAsync(SearchText, page, PageNavigator.DefaultPageSize);
            Items.Clear();
            foreach (var item in result.Items) Items.Add(item);
            Pager.Update(result.PageNumber, result.TotalCount);
            OnPropertyChanged(nameof(CustomerCount));
            OnPropertyChanged(nameof(CustomersWithBalanceCount));
            OnPropertyChanged(nameof(CurrentPageBalanceTotal));
        }
        catch (Exception e) { Status = $"تعذر تحميل العملاء: {e.Message}"; }
    }

    [RelayCommand]
    private void ShowDetails(Customer? customer)
    {
        if (customer is null) return;
        Selected = customer;
        displayedCustomerId = customer.Id;
        IsDetailsVisible = true;
    }

    [RelayCommand]
    private async Task NewCustomerAsync()
    {
        var form = new CustomerEditorForm();
        if (!editor.Show(form, "إضافة عميل")) return;
        try
        {
            var customer = form.Customer;
            customer.CurrentBalance = form.OpeningBalance;
            var created = await customers.CreateAsync(customer, session.Current?.UserId);
            Status = "تم حفظ بيانات العميل.";
            await LoadAsync();
            Selected = Items.SingleOrDefault(x => x.Id == created.Id);
        }
        catch (Exception e) { Status = $"تعذر حفظ العميل: {e.Message}"; }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null || Selected.Id == 0 || Selected.IsWalkIn) return;
        var form = new CustomerEditorForm { Customer = Copy(Selected) };
        if (!editor.Show(form, "تعديل بيانات العميل")) return;
        try
        {
            await customers.SaveAsync(form.Customer);
            Status = "تم حفظ بيانات العميل.";
            await LoadAsync();
            Selected = Items.SingleOrDefault(x => x.Id == form.Customer.Id);
        }
        catch (Exception e) { Status = $"تعذر حفظ العميل: {e.Message}"; }
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (Selected is null || Selected.Id == 0) return;
        try
        {
            await customers.DeactivateAsync(Selected.Id);
            Status = "تم حذف العميل (إيقافه مع الاحتفاظ بسجل الفواتير).";
            Selected = null;
            await LoadAsync();
        }
        catch (Exception e) { Status = $"تعذر حذف العميل: {e.Message}"; }
    }

    [RelayCommand]
    private async Task RecordPaymentAsync()
    {
        if (Selected is null || Selected.Id == 0) { Status = "اختر عميلاً أولاً."; return; }
        if (session.Current is null) { Status = "انتهت الجلسة؛ سجل الدخول مجدداً."; return; }
        var customerId = Selected.Id;
        try
        {
            await customers.RecordBalancePaymentAsync(customerId, BalancePaymentAmount, session.Current.UserId,
                BalancePaymentMethod, BalancePaymentNote);
            BalancePaymentAmount = 0;
            BalancePaymentNote = string.Empty;
            Status = "تم تسجيل الدفعة وتحديث رصيد العميل.";
            await LoadAsync();
            Selected = Items.SingleOrDefault(x => x.Id == customerId);
        }
        catch (Exception e) { Status = $"تعذر تسجيل الدفعة: {e.Message}"; }
    }

    [RelayCommand]
    private async Task AddBalanceChargeAsync()
    {
        if (Selected is null || Selected.Id == 0 || Selected.IsWalkIn)
        {
            Status = "اختر عميلاً مسجلاً أولاً.";
            return;
        }
        if (session.Current is not { } currentUser)
        {
            Status = "انتهت الجلسة؛ سجل الدخول مجدداً.";
            return;
        }

        var customerId = Selected.Id;
        try
        {
            await customers.RecordBalanceChargeAsync(
                customerId, BalancePaymentAmount, currentUser.UserId, BalancePaymentNote);
            BalancePaymentAmount = 0;
            BalancePaymentNote = string.Empty;
            Status = "تم إضافة الاستحقاق وتحديث رصيد العميل.";
            await LoadAsync();
            Selected = Items.SingleOrDefault(item => item.Id == customerId);
            await LedgerPager.LoadAsync(1);
        }
        catch (Exception exception)
        {
            Status = $"تعذر إضافة استحقاق العميل: {exception.Message}";
        }
    }

    private async Task LoadLedgerAsync(Customer? customer)
    {
        LedgerPager.Bind(page => LoadLedgerPageAsync(customer, page));
        await LedgerPager.LoadAsync(1);
    }

    private async Task LoadLedgerPageAsync(Customer? customer, int page)
    {
        Ledger.Clear();
        if (customer is null || customer.Id == 0) return;
        try
        {
            var result = await customers.GetLedgerPageAsync(customer.Id, page, PageNavigator.DefaultPageSize);
            foreach (var entry in result.Items) Ledger.Add(entry);
            LedgerPager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل كشف حساب العميل: {e.Message}"; }
    }

    private static Customer Copy(Customer customer) => new()
    {
        Id = customer.Id, CustomerCode = customer.CustomerCode, FullName = customer.FullName,
        Phone = customer.Phone, Address = customer.Address,
        LoyaltyPoints = customer.LoyaltyPoints, CurrentBalance = customer.CurrentBalance,
        CreditLimit = customer.CreditLimit,
        Notes = customer.Notes, IsActive = customer.IsActive, IsWalkIn = customer.IsWalkIn
    };
}

public partial class EmployeesViewModel(IEmployeeService employees, IEditorDialogService editor) : ObservableObject
{
    public ObservableCollection<Employee> Items { get; } = [];
    public PageNavigator Pager { get; } = new();
    [ObservableProperty] private Employee? selected;
    [ObservableProperty] private string status = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        Pager.Bind(LoadPageAsync);
        await Pager.LoadAsync(1);
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await employees.GetPageAsync(page, PageNavigator.DefaultPageSize);
            Items.Clear();
            foreach (var item in result.Items) Items.Add(item);
            Pager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل الموظفين: {e.Message}"; }
    }

    [RelayCommand]
    private async Task NewEmployeeAsync()
    {
        var employee = new Employee { FullName = string.Empty };
        if (editor.Show(employee, "إضافة موظف")) await SaveEmployeeAsync(employee);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null) return;
        var editedEmployee = Copy(Selected);
        if (editor.Show(editedEmployee, "تعديل بيانات الموظف")) await SaveEmployeeAsync(editedEmployee);
    }

    private async Task SaveEmployeeAsync(Employee employee)
    {
        try { await employees.SaveAsync(employee); Status = "تم حفظ الموظف."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حفظ الموظف: {e.Message}"; }
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (Selected is null) return;
        if (Selected.Id == 0) { Items.Remove(Selected); Selected = null; return; }
        try { await employees.DeactivateAsync(Selected.Id); Status = "تم حذف الموظف (إيقاف حسابه)."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حذف الموظف: {e.Message}"; }
    }

    private static Employee Copy(Employee employee) => new()
    {
        Id = employee.Id, UserId = employee.UserId, FullName = employee.FullName, NationalId = employee.NationalId,
        Phone = employee.Phone, Email = employee.Email, Address = employee.Address, JobTitle = employee.JobTitle,
        HireDate = employee.HireDate, BasicSalary = employee.BasicSalary, IsActive = employee.IsActive,
        CreatedAt = employee.CreatedAt
    };
}

public partial class MasterDataViewModel(IMasterDataService service, IEditorDialogService editor) : ObservableObject
{
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public PageNavigator CategoryPager { get; } = new();
    public PageNavigator BrandPager { get; } = new();
    [ObservableProperty] private Category? selectedCategory;
    [ObservableProperty] private Brand? selectedBrand;
    [ObservableProperty] private string status = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        CategoryPager.Bind(LoadCategoriesAsync);
        BrandPager.Bind(LoadBrandsAsync);
        await Task.WhenAll(CategoryPager.LoadAsync(1), BrandPager.LoadAsync(1));
    }

    private async Task LoadCategoriesAsync(int page)
    {
        try
        {
            var result = await service.GetCategoryPageAsync(page, PageNavigator.DefaultPageSize);
            Categories.Clear();
            foreach (var item in result.Items) Categories.Add(item);
            CategoryPager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل الفئات: {e.Message}"; }
    }

    private async Task LoadBrandsAsync(int page)
    {
        try
        {
            var result = await service.GetBrandPageAsync(page, PageNavigator.DefaultPageSize);
            Brands.Clear();
            foreach (var item in result.Items) Brands.Add(item);
            BrandPager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل العلامات التجارية: {e.Message}"; }
    }

    [RelayCommand]
    private async Task NewCategoryAsync()
    {
        var category = new Category { Name = string.Empty, Code = $"CAT-{Guid.NewGuid():N}"[..12] };
        if (editor.Show(category, "إضافة فئة")) await SaveCategoryAsync(category);
    }

    [RelayCommand]
    private async Task EditCategoryAsync()
    {
        if (SelectedCategory is null) return;
        var category = Copy(SelectedCategory);
        if (editor.Show(category, "تعديل الفئة")) await SaveCategoryAsync(category);
    }

    private async Task SaveCategoryAsync(Category category)
    {
        try { await service.SaveCategoryAsync(category); Status = "تم حفظ الفئة."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حفظ الفئة: {e.Message}"; }
    }

    [RelayCommand]
    private async Task NewBrandAsync()
    {
        var brand = new Brand { Name = string.Empty };
        if (editor.Show(brand, "إضافة علامة تجارية")) await SaveBrandAsync(brand);
    }

    [RelayCommand]
    private async Task EditBrandAsync()
    {
        if (SelectedBrand is null) return;
        var brand = Copy(SelectedBrand);
        if (editor.Show(brand, "تعديل العلامة التجارية")) await SaveBrandAsync(brand);
    }

    private async Task SaveBrandAsync(Brand brand)
    {
        try { await service.SaveBrandAsync(brand); Status = "تم حفظ العلامة التجارية."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حفظ العلامة التجارية: {e.Message}"; }
    }

    [RelayCommand]
    private async Task DeactivateCategoryAsync()
    {
        if (SelectedCategory is null || SelectedCategory.Id == 0) return;
        try { await service.DeactivateCategoryAsync(SelectedCategory.Id); Status = "تم حذف الفئة."; SelectedCategory = null; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حذف الفئة: {e.Message}"; }
    }

    [RelayCommand]
    private async Task DeactivateBrandAsync()
    {
        if (SelectedBrand is null || SelectedBrand.Id == 0) return;
        try { await service.DeactivateBrandAsync(SelectedBrand.Id); Status = "تم حذف العلامة التجارية."; SelectedBrand = null; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر حذف العلامة التجارية: {e.Message}"; }
    }

    private static Category Copy(Category category) => new()
    {
        Id = category.Id, Name = category.Name, Code = category.Code, Description = category.Description,
        ColorHex = category.ColorHex, Icon = category.Icon, IsActive = category.IsActive
    };

    private static Brand Copy(Brand brand) => new()
    {
        Id = brand.Id, Name = brand.Name, Description = brand.Description,
        LogoPath = brand.LogoPath, IsActive = brand.IsActive
    };
}

public partial class SuppliersViewModel(
    ISupplierService suppliers,
    IPurchaseInvoiceService purchaseInvoices,
    IEditorDialogService editor,
    SessionManager session) : ObservableObject
{
    public Task<PurchaseInvoice?> GetPurchaseInvoiceDetailsAsync(long invoiceId) =>
        purchaseInvoices.GetByIdAsync(invoiceId);

    public ObservableCollection<Supplier> Items { get; } = [];
    public ObservableCollection<SupplierBalanceEntry> Ledger { get; } = [];
    public ObservableCollection<PurchaseInvoiceLookupRow> PurchaseInvoices { get; } = [];
    public PageNavigator Pager { get; } = new();
    public PageNavigator LedgerPager { get; } = new();
    public PageNavigator PurchaseInvoicePager { get; } = new();
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.CreditCard];
    public bool HasSelectedSupplier => Selected is not null;
    public int SupplierCount => Pager.TotalCount;
    public int SuppliersWithBalanceCount => Items.Count(x => x.CurrentBalance > 0);
    public decimal CurrentPageBalanceTotal => Items.Sum(x => x.CurrentBalance);
    [ObservableProperty] private Supplier? selected;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private decimal balanceAmount;
    [ObservableProperty] private PaymentMethod balancePaymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private string? balanceNote;

    partial void OnSelectedChanged(Supplier? value)
    {
        OnPropertyChanged(nameof(HasSelectedSupplier));
        LedgerPager.Bind(page => LoadLedgerPageAsync(value, page));
        _ = LedgerPager.LoadAsync(1);
        PurchaseInvoiceRowsChanged();
        if (value is not null)
        {
            PurchaseInvoicePager.Bind(LoadPurchaseInvoicePageAsync);
            _ = PurchaseInvoicePager.LoadAsync(1);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Pager.Bind(LoadPageAsync);
        await Pager.LoadAsync(1);
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await suppliers.GetPageAsync(SearchText, page, PageNavigator.DefaultPageSize);
            var selectedId = Selected?.Id;
            Items.Clear();
            foreach (var supplier in result.Items) Items.Add(supplier);
            Pager.Update(result.PageNumber, result.TotalCount);
            OnPropertyChanged(nameof(SupplierCount));
            OnPropertyChanged(nameof(SuppliersWithBalanceCount));
            OnPropertyChanged(nameof(CurrentPageBalanceTotal));
            Selected = Items.SingleOrDefault(x => x.Id == selectedId);
        }
        catch (Exception exception) { Status = $"تعذر تحميل الموردين: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var form = new SupplierEditorForm();
        if (editor.Show(form, "إضافة مورد"))
            await SaveAsync(form.Supplier, form.OpeningBalance);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null) return;
        var form = new SupplierEditorForm { Supplier = Copy(Selected) };
        if (editor.Show(form, "تعديل بيانات المورد"))
            await SaveAsync(form.Supplier, form.OpeningBalance);
    }

    private async Task SaveAsync(Supplier supplier, decimal openingBalance)
    {
        try
        {
            await suppliers.SaveAsync(supplier, openingBalance, session.Current?.UserId);
            Status = "تم حفظ بيانات المورد.";
            await Pager.LoadAsync(1);
            Selected = Items.SingleOrDefault(x => x.Id == supplier.Id);
        }
        catch (Exception exception) { Status = $"تعذر حفظ المورد: {exception.Message}"; }
    }

    private async Task LoadLedgerPageAsync(Supplier? supplier, int page)
    {
        Ledger.Clear();
        if (supplier is null) return;
        try
        {
            var result = await suppliers.GetLedgerPageAsync(supplier.Id, page, PageNavigator.DefaultPageSize);
            foreach (var entry in result.Items) Ledger.Add(entry);
            LedgerPager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception exception) { Status = $"تعذر تحميل كشف حساب المورد: {exception.Message}"; }
    }

    private void PurchaseInvoiceRowsChanged()
    {
        PurchaseInvoices.Clear();
        PurchaseInvoicePager.Update(1, 0);
    }

    private async Task LoadPurchaseInvoicePageAsync(int page)
    {
        if (Selected is null)
            return;
        var supplierId = Selected.Id;
        try
        {
            var result = await purchaseInvoices.GetPageAsync(supplierId, page, PageNavigator.DefaultPageSize);
            if (Selected?.Id != supplierId)
                return;
            PurchaseInvoices.Clear();
            foreach (var invoice in result.Items)
                PurchaseInvoices.Add(invoice);
            PurchaseInvoicePager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception exception)
        {
            Status = $"تعذر تحميل فواتير المورد: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RegisterBalancePaymentAsync()
    {
        if (Selected is null) { Status = "اختر مورداً أولاً."; return; }
        if (session.Current is null) { Status = "انتهت الجلسة؛ سجل الدخول مجدداً."; return; }
        var supplierId = Selected.Id;
        try
        {
            await suppliers.ChangeBalanceAsync(supplierId, BalanceAmount, SupplierBalanceEntryType.Payment,
                session.Current.UserId, BalancePaymentMethod, BalanceNote);
            BalanceAmount = 0;
            BalanceNote = string.Empty;
            Status = "تم تسجيل سداد الاستحقاق للمورد.";
            await Pager.LoadAsync(Pager.PageNumber);
            Selected = Items.SingleOrDefault(x => x.Id == supplierId);
            await LedgerPager.LoadAsync(1);
        }
        catch (Exception exception) { Status = $"تعذر تسجيل حركة المورد: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task RegisterBalanceChargeAsync()
    {
        if (Selected is null) { Status = "اختر مورداً أولاً."; return; }
        if (session.Current is null) { Status = "انتهت الجلسة؛ سجل الدخول مجدداً."; return; }
        var supplierId = Selected.Id;
        try
        {
            await suppliers.ChangeBalanceAsync(supplierId, BalanceAmount, SupplierBalanceEntryType.Charge,
                session.Current.UserId, BalancePaymentMethod, BalanceNote);
            BalanceAmount = 0;
            BalanceNote = string.Empty;
            Status = "تم إضافة استحقاق المورد.";
            await Pager.LoadAsync(Pager.PageNumber);
            Selected = Items.SingleOrDefault(item => item.Id == supplierId);
            await LedgerPager.LoadAsync(1);
        }
        catch (Exception exception)
        {
            Status = $"تعذر إضافة استحقاق المورد: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        try
        {
            await suppliers.DeleteAsync(Selected.Id);
            Status = "تم حذف المورد.";
            Selected = null;
            await Pager.LoadAsync(1);
        }
        catch (Exception exception) { Status = $"تعذر حذف المورد: {exception.Message}"; }
    }

    private static Supplier Copy(Supplier supplier) => new()
    {
        Id = supplier.Id, Name = supplier.Name, Phone = supplier.Phone, Email = supplier.Email,
        Address = supplier.Address, TaxNumber = supplier.TaxNumber, Notes = supplier.Notes,
        CurrentBalance = supplier.CurrentBalance, IsActive = supplier.IsActive, CreatedAt = supplier.CreatedAt
    };
}

public partial class InventoryViewModel(
    IStockAdjustmentService service,
    IProductService products,
    SessionManager session) : ObservableObject
{
    public ObservableCollection<StockAdjustment> Adjustments { get; } = [];
    public ObservableCollection<Product> Products { get; } = [];
    public IReadOnlyList<StockAdjustmentType> Types { get; } =
    [
        StockAdjustmentType.WarehouseIn,
        StockAdjustmentType.TransferToShop
    ];
    public PageNavigator Pager { get; } = new();
    public bool CanAdjust => session.Current?.CanManageCatalog == true;
    [ObservableProperty] private Product? selectedProduct;
    [ObservableProperty] private StockAdjustmentType selectedType = StockAdjustmentType.WarehouseIn;
    [ObservableProperty] private decimal quantity = 1;
    [ObservableProperty] private string? reason;
    [ObservableProperty] private string status = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var selectedProductId = SelectedProduct?.Id;
            Products.Clear();
            foreach (var product in await products.SearchAsync(new ProductSearch())) Products.Add(product);
            SelectedProduct = Products.FirstOrDefault(product => product.Id == selectedProductId);
            Pager.Bind(LoadPageAsync);
            await Pager.LoadAsync(1);
        }
        catch (Exception e) { Status = $"تعذر تحميل سجل المخزون: {e.Message}"; }
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await service.GetRecentPageAsync(page, PageNavigator.DefaultPageSize);
            Adjustments.Clear();
            foreach (var item in result.Items) Adjustments.Add(item);
            Pager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل سجل المخزون: {e.Message}"; }
    }

    [RelayCommand]
    private async Task AdjustAsync()
    {
        if (!CanAdjust) { Status = "تتطلب حركات المخزون صلاحية المدير أو مدير النظام."; return; }
        if (SelectedProduct is null) { Status = "اختر منتجاً."; return; }
        try
        {
            await service.AdjustAsync(SelectedProduct.Id, SelectedType, Quantity,
                session.Current?.EmployeeId, Reason);
            Status = "تم تسجيل حركة المخزون.";
            Quantity = 1;
            Reason = string.Empty;
            await LoadAsync();
        }
        catch (Exception e) { Status = $"تعذر تسجيل حركة المخزون: {e.Message}"; }
    }
}

public partial class UsersViewModel(
    IUserManagementService service,
    IEmployeeService employeeService,
    SessionManager session,
    IEditorDialogService editor) : ObservableObject
{
    public ObservableCollection<UserAccount> Items { get; } = [];
    public ObservableCollection<Employee> Employees { get; } = [];
    public Array Roles { get; } = Enum.GetValues<UserRole>();
    public bool CanManageUsers => session.Current?.Role == UserRole.Admin;
    public PageNavigator Pager { get; } = new();
    [ObservableProperty] private UserAccount? selected;
    [ObservableProperty] private string status = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Employees.Clear();
            foreach (var employee in await employeeService.GetAllAsync())
                if (employee.IsActive) Employees.Add(employee);
            Pager.Bind(LoadPageAsync);
            await Pager.LoadAsync(1);
        }
        catch (Exception e) { Status = $"تعذر تحميل المستخدمين: {e.Message}"; }
    }

    private async Task LoadPageAsync(int page)
    {
        try
        {
            var result = await service.GetPageAsync(page, PageNavigator.DefaultPageSize);
            Items.Clear();
            foreach (var user in result.Items) Items.Add(user);
            Pager.Update(result.PageNumber, result.TotalCount);
        }
        catch (Exception e) { Status = $"تعذر تحميل المستخدمين: {e.Message}"; }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!CanManageUsers) { Status = "إدارة المستخدمين متاحة لمدير النظام فقط."; return; }
        var form = new UserEditorForm();
        if (!editor.Show(form, "إضافة حساب مستخدم", employees: Employees)) return;
        try
        {
            await service.CreateAsync(form.Username, form.Password, form.Role, form.Employee?.Id);
            Status = "تم إنشاء حساب المستخدم.";
            await LoadAsync();
        }
        catch (Exception e) { Status = $"تعذر إنشاء الحساب: {e.Message}"; }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (!CanManageUsers || Selected is null)
        {
            Status = "اختر حساباً؛ يحق لمدير النظام فقط تعديل الحسابات.";
            return;
        }
        var form = new UserEditorForm
        {
            Id = Selected.Id,
            Username = Selected.Username,
            Role = Selected.Role,
            Employee = Employees.SingleOrDefault(x => x.UserId == Selected.Id),
            IsActive = Selected.IsActive
        };
        if (!editor.Show(form, "تعديل حساب المستخدم", employees: Employees)) return;
        if (form.Id == session.Current?.UserId && !form.IsActive)
        {
            Status = "لا يمكنك تعطيل حسابك الحالي.";
            return;
        }
        try
        {
            await service.UpdateAsync(form.Id, form.Username, form.Role, form.Employee?.Id,
                form.Password, form.IsActive);
            Status = "تم تحديث حساب المستخدم.";
            await LoadAsync();
            Selected = Items.SingleOrDefault(x => x.Id == form.Id);
        }
        catch (Exception e) { Status = $"تعذر تحديث الحساب: {e.Message}"; }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (!CanManageUsers || Selected is null) { Status = "اختر مستخدماً؛ يحق لمدير النظام فقط تغيير حالة الحساب."; return; }
        if (Selected.Id == session.Current?.UserId) { Status = "لا يمكنك تعطيل حسابك الحالي."; return; }
        try { await service.SetActiveAsync(Selected.Id, !Selected.IsActive); Status = "تم تحديث حالة الحساب."; await LoadAsync(); }
        catch (Exception e) { Status = $"تعذر تحديث الحساب: {e.Message}"; }
    }
}

public sealed partial class ShellViewModel(
    CashierViewModel cashier,
    ProductsViewModel products,
    PurchaseEntryViewModel purchaseEntry,
    PurchaseInvoicesViewModel purchaseInvoices,
    CustomersViewModel customers,
    SuppliersViewModel suppliers,
    EmployeesViewModel employees,
    MasterDataViewModel masterData,
    InventoryViewModel inventory,
    UsersViewModel users,
    InvoiceLookupViewModel invoiceLookup,
    ReportsViewModel reports,
    DailySheetViewModel dailySheet,
    IProductService productService,
    SessionManager session) : ObservableObject
{
    private static readonly AccentPaletteOption[] PaletteOptions =
    [
        new("blue", "أزرق", "#4F8CFF"),
        new("teal", "تركوازي", "#20BFA9"),
        new("violet", "بنفسجي", "#A78BFA"),
        new("amber", "كهرماني", "#F59E0B")
    ];

    [ObservableProperty] private bool isDarkTheme = true;
    [ObservableProperty] private AccentPaletteOption selectedAccentPalette = PaletteOptions[0];
    [ObservableProperty] private string lowStockStatus = string.Empty;
    public IReadOnlyList<AccentPaletteOption> AccentPalettes => PaletteOptions;
    public ObservableCollection<Product> LowStockProducts { get; } = [];
    public int LowStockCount => LowStockProducts.Count;
    public bool HasLowStock => LowStockCount > 0;
    public CashierViewModel Cashier { get; } = cashier;
    public ProductsViewModel Products { get; } = products;
    public PurchaseEntryViewModel PurchaseEntry { get; } = purchaseEntry;
    public PurchaseInvoicesViewModel PurchaseInvoices { get; } = purchaseInvoices;
    public CustomersViewModel Customers { get; } = customers;
    public SuppliersViewModel Suppliers { get; } = suppliers;
    public EmployeesViewModel Employees { get; } = employees;
    public MasterDataViewModel MasterData { get; } = masterData;
    public InventoryViewModel Inventory { get; } = inventory;
    public UsersViewModel Users { get; } = users;
    public InvoiceLookupViewModel InvoiceLookup { get; } = invoiceLookup;
    public ReportsViewModel Reports { get; } = reports;
    public DailySheetViewModel DailySheet { get; } = dailySheet;
    public string SignedInAs => $"{session.Current?.Role switch
    {
        UserRole.Admin => "مدير النظام",
        UserRole.Manager => "مدير",
        UserRole.Cashier => "كاشير",
        _ => string.Empty
    }} · {session.Current?.Username}";
    public bool CanManageAdministration => session.Current?.Role is UserRole.Admin or UserRole.Manager;
    public bool CanManageUsers => session.Current?.Role == UserRole.Admin;

    public async Task RefreshLowStockProductsAsync()
    {
        try
        {
            var lowStockProducts = await productService.GetLowStockProductsAsync();
            LowStockProducts.Clear();
            foreach (var product in lowStockProducts)
                LowStockProducts.Add(product);
            LowStockStatus = string.Empty;
        }
        catch (Exception exception)
        {
            LowStockProducts.Clear();
            LowStockStatus = $"تعذر تحميل تنبيه المخزون: {exception.Message}";
        }

        OnPropertyChanged(nameof(LowStockCount));
        OnPropertyChanged(nameof(HasLowStock));
    }

    partial void OnSelectedAccentPaletteChanged(AccentPaletteOption value) =>
        App.ApplyAccentPalette(value.Key);

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica, true);
        App.ApplyThemePalette(IsDarkTheme);
    }
}

public sealed record AccentPaletteOption(string Key, string Name, string ColorHex)
{
    public Brush Swatch => new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorHex));
}
