using System.Collections.ObjectModel;
using System.ComponentModel;
using Cashier.Application;
using Cashier.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public sealed partial class CartLineViewModel(Product product) : ObservableObject
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public string Sku => Product.Sku;
    public decimal UnitPrice => Product.SellingPrice;
    public decimal AvailableStock => Product.CurrentStock;
    public decimal QuantityStep => Product.Unit == ProductUnit.Kg ? 0.1m : 1m;
    [ObservableProperty] private decimal quantity = 1;
    [ObservableProperty] private decimal discountAmount;
    public decimal LineTotal
    {
        get
        {
            return decimal.Round(UnitPrice * Quantity - Math.Clamp(DiscountAmount, 0, UnitPrice * Quantity), 2, MidpointRounding.AwayFromZero);
        }
    }
    partial void OnQuantityChanged(decimal value)
    {
        if (DiscountAmount > UnitPrice * Quantity)
            DiscountAmount = UnitPrice * Quantity;
        else
            OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnDiscountAmountChanged(decimal value)
    {
        var cappedAmount = Math.Clamp(value, 0, Math.Max(0, UnitPrice * Quantity));
        if (cappedAmount != value)
            DiscountAmount = cappedAmount;
        else
            OnPropertyChanged(nameof(LineTotal));
    }
}

public sealed record HeldSale(
    Customer? Customer,
    IReadOnlyList<HeldSaleLine> Items);

public sealed record HeldSaleLine(int ProductId, decimal Quantity, decimal DiscountAmount);

public partial class CashierViewModel(
    IProductService productService,
    ICustomerService customerService,
    IInvoiceService invoiceService,
    IPrintService printService,
    SessionManager sessionManager) : ObservableObject
{
    private readonly List<HeldSale> heldSales = [];
    private Invoice? lastInvoice;
    private Customer? walkInCustomer;
    private CancellationTokenSource? productSearchCancellation;
    private bool customerModeChosen;

    public event EventHandler? SaleCompleted;
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<CartLineViewModel> Cart { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = [PaymentMethod.Cash, PaymentMethod.CreditCard];
    public UserSession? Session => sessionManager.Current;
    public bool HasSelectedRegisteredCustomer => SelectedCustomer is { IsWalkIn: false };
    public decimal RemainingAmount => Math.Max(0, GrandTotal - PaymentAmount);

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool isProductDropdownOpen;
    [ObservableProperty] private string customerSearch = string.Empty;
    [ObservableProperty] private string quickCustomerName = string.Empty;
    [ObservableProperty] private string quickCustomerPhone = string.Empty;
    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private PaymentMethod selectedPaymentMethod = PaymentMethod.Cash;
    [ObservableProperty] private CartLineViewModel? selectedLine;
    [ObservableProperty] private decimal paymentAmount;
    [ObservableProperty] private string statusMessage = "النظام جاهز لاستقبال المبيعات.";
    [ObservableProperty] private decimal subtotal;
    [ObservableProperty] private decimal discountTotal;
    [ObservableProperty] private decimal grandTotal;
    [ObservableProperty] private decimal changeDue;
    [ObservableProperty] private bool showQuickCustomer;
    [ObservableProperty] private bool isCustomerPickerOpen;
    public decimal Rounding => 0;

    partial void OnPaymentAmountChanged(decimal value) => OnPropertyChanged(nameof(RemainingAmount));
    partial void OnSelectedCustomerChanged(Customer? value)
    {
        OnPropertyChanged(nameof(HasSelectedRegisteredCustomer));
        OnPropertyChanged(nameof(RemainingAmount));
        if (value is not null)
        {
            customerModeChosen = true;
            IsCustomerPickerOpen = false;
        }
    }

    partial void OnGrandTotalChanged(decimal value)
    {
        PaymentAmount = value;
        OnPropertyChanged(nameof(RemainingAmount));
    }

    partial void OnSearchTextChanged(string value)
    {
        IsProductDropdownOpen = true;
        _ = SearchProductsDebouncedAsync();
    }

    public void SelectProductFromSearch(Product product)
    {
        AddProduct(product);
        SearchText = string.Empty;
        IsProductDropdownOpen = false;
    }

    public async Task LoadAsync()
    {
        try
        {
            await LoadProductsAsync(string.Empty, CancellationToken.None);
            Customers.Clear();
            foreach (var customer in await customerService.SearchAsync("CUST-WALKIN"))
            {
                walkInCustomer = customer;
            }
            if (!customerModeChosen)
                SelectedCustomer = walkInCustomer;
            Customers.Clear();
            foreach (var customer in await customerService.SearchAsync(null))
                if (!customer.IsWalkIn)
                    Customers.Add(customer);
            if (walkInCustomer is not null)
                Customers.Add(walkInCustomer);
        }
        catch (Exception exception) { StatusMessage = $"تعذر تحميل شاشة البيع: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task SearchProductsAsync()
    {
        productSearchCancellation?.Cancel();
        productSearchCancellation?.Dispose();
        var cancellation = productSearchCancellation = new CancellationTokenSource();
        await LoadProductsAsync(SearchText, cancellation.Token);
    }

    private async Task SearchProductsDebouncedAsync()
    {
        productSearchCancellation?.Cancel();
        productSearchCancellation?.Dispose();
        var cancellation = productSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cancellation.Token);
            await LoadProductsAsync(SearchText, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task LoadProductsAsync(string searchText, CancellationToken cancellationToken)
    {
        try
        {
            var result = await productService.SearchAsync(new ProductSearch(searchText), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Products.Clear();
            foreach (var product in result)
                Products.Add(product);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { StatusMessage = $"تعذر البحث عن المنتجات: {exception.Message}"; }
    }

    [RelayCommand]
    private void AddProduct(Product? product)
    {
        if (product is null)
            return;
        var existing = Cart.FirstOrDefault(x => x.Product.Id == product.Id);
        if (existing is not null)
        {
            if (existing.Quantity + existing.QuantityStep > product.CurrentStock)
            {
                StatusMessage = $"الكمية المتاحة من {product.Name} هي {product.CurrentStock}.";
                return;
            }
            existing.Quantity += existing.QuantityStep;
            return;
        }
        if (product.CurrentStock <= 0)
        {
            StatusMessage = $"المنتج {product.Name} غير متوفر في المخزون.";
            return;
        }
        var line = new CartLineViewModel(product) { Quantity = Math.Min(1, product.CurrentStock) };
        line.PropertyChanged += OnCartLineChanged;
        Cart.Add(line);
        SelectedLine = line;
        RefreshTotals();
    }

    public async Task AddScannedBarcodeAsync(string barcode)
    {
        try
        {
            var matches = await productService.SearchAsync(new ProductSearch(barcode));
            var product = matches.SingleOrDefault(x => string.Equals(x.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                StatusMessage = $"لا يوجد منتج بهذا الباركود: {barcode}.";
                return;
            }
            AddProduct(product);
            StatusMessage = $"تمت إضافة {product.Name}.";
        }
        catch (Exception exception) { StatusMessage = $"تعذر قراءة الباركود: {exception.Message}"; }
    }

    [RelayCommand]
    private void IncreaseQuantity(CartLineViewModel? line)
    {
        if (line is null) return;
        if (line.Quantity + line.QuantityStep > line.AvailableStock) { StatusMessage = "الكمية المطلوبة تتجاوز المخزون المتاح."; return; }
        line.Quantity += line.QuantityStep;
    }

    [RelayCommand]
    private void DecreaseQuantity(CartLineViewModel? line)
    {
        if (line is null) return;
        if (line.Quantity <= line.QuantityStep) Cart.Remove(line);
        else line.Quantity -= line.QuantityStep;
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is not null) Cart.Remove(line);
        RefreshTotals();
    }

    [RelayCommand]
    private void AddLineDiscount()
    {
        if (SelectedLine is null) { StatusMessage = "اختر صنفاً من الفاتورة أولاً."; return; }
        SelectedLine.DiscountAmount = SelectedLine.DiscountAmount >= 5 ? 0 : 5;
        StatusMessage = $"تم ضبط خصم الصنف إلى {SelectedLine.DiscountAmount:C2}";
    }

    [RelayCommand]
    private void FocusQuantity() => StatusMessage = SelectedLine is null
        ? "اختر صنفاً من الفاتورة لتعديل كميته."
        : $"تم تحديد {SelectedLine.Name}؛ استخدم أزرار الكمية للتعديل.";

    [RelayCommand]
    private void HoldCart()
    {
        if (Cart.Count == 0) { StatusMessage = "لا توجد فاتورة لتعليقها."; return; }
        heldSales.Add(new HeldSale(
            SelectedCustomer,
            Cart.Select(x => new HeldSaleLine(x.Product.Id, x.Quantity, x.DiscountAmount)).ToArray()));
        Cart.Clear();
        SelectedLine = null;
        PaymentAmount = 0;
        SelectedCustomer = walkInCustomer;
        customerModeChosen = true;
        RefreshTotals();
        StatusMessage = $"تم تعليق الفاتورة. الفواتير المعلقة: {heldSales.Count}.";
    }

    [RelayCommand]
    private async Task RecallCartAsync()
    {
        if (heldSales.Count == 0) { StatusMessage = "لا توجد فواتير معلقة."; return; }
        if (Cart.Count > 0) { StatusMessage = "علّق الفاتورة الحالية أو امسحها قبل استعادة فاتورة أخرى."; return; }
        var held = heldSales[^1];
        var restoredLines = new List<(HeldSaleLine Line, Product Product)>();
        try
        {
            foreach (var item in held.Items)
            {
                var product = await productService.GetByIdAsync(item.ProductId);
                if (product is null)
                {
                    StatusMessage = $"تعذرت الاستعادة؛ المنتج رقم {item.ProductId} لم يعد متاحاً.";
                    return;
                }
                if (product.CurrentStock < item.Quantity)
                {
                    StatusMessage = $"تعذرت الاستعادة؛ المخزون المتاح من {product.Name} أصبح {product.CurrentStock}.";
                    return;
                }
                restoredLines.Add((item, product));
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"تعذر التحقق من المنتجات المعلقة: {exception.Message}";
            return;
        }

        heldSales.RemoveAt(heldSales.Count - 1);
        SelectedCustomer = held.Customer;
        foreach (var (item, product) in restoredLines)
        {
            var line = new CartLineViewModel(product) { Quantity = item.Quantity, DiscountAmount = item.DiscountAmount };
            line.PropertyChanged += OnCartLineChanged;
            Cart.Add(line);
        }
        RefreshTotals();
        StatusMessage = "تمت استعادة الفاتورة المعلقة.";
    }

    [RelayCommand]
    private void OpenQuickCustomer() => ShowQuickCustomer = !ShowQuickCustomer;

    [RelayCommand]
    private void ClearSelectedCustomer()
    {
        SelectedCustomer = null;
        CustomerSearch = string.Empty;
        IsCustomerPickerOpen = false;
        customerModeChosen = true;
        StatusMessage = "سيتم حفظ الفاتورة بدون ربطها بعميل.";
    }

    [RelayCommand]
    private void SelectWalkInCustomer()
    {
        SelectedCustomer = walkInCustomer;
        CustomerSearch = walkInCustomer?.FullName ?? string.Empty;
        IsCustomerPickerOpen = false;
        customerModeChosen = true;
        StatusMessage = "تم اختيار العميل النقدي.";
    }

    [RelayCommand]
    private async Task SelectRegisteredCustomerAsync()
    {
        CustomerSearch = string.Empty;
        await SearchCustomersAsync();
        if (Customers.Count == 0)
        {
            IsCustomerPickerOpen = false;
            customerModeChosen = true;
            StatusMessage = "لا يوجد عميل مسجل مطابق للبحث.";
            return;
        }
        SelectedCustomer = null;
        customerModeChosen = true;
        IsCustomerPickerOpen = true;
        StatusMessage = "اختر عميلاً مسجلاً من القائمة.";
    }

    [RelayCommand]
    private async Task CreateQuickCustomerAsync()
    {
        try
        {
            var customer = await customerService.CreateAsync(new Customer
            {
                CustomerCode = string.Empty,
                FullName = string.IsNullOrWhiteSpace(QuickCustomerName) ? "عميل جديد" : QuickCustomerName.Trim(),
                Phone = QuickCustomerPhone.Trim()
            });
            Customers.Add(customer);
            SelectedCustomer = customer;
            CustomerSearch = customer.FullName;
            ShowQuickCustomer = false;
            QuickCustomerName = string.Empty;
            QuickCustomerPhone = string.Empty;
            StatusMessage = $"تم إنشاء العميل {customer.CustomerCode}.";
        }
        catch (Exception exception) { StatusMessage = $"تعذرت إضافة العميل: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task SearchCustomersAsync()
    {
        try
        {
            Customers.Clear();
            foreach (var customer in await customerService.SearchAsync(CustomerSearch))
                if (!customer.IsWalkIn)
                    Customers.Add(customer);
            if (Customers.Count > 0 && IsCustomerPickerOpen)
                SelectedCustomer = Customers[0];
        }
        catch (Exception exception) { StatusMessage = $"تعذر البحث عن العملاء: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        try
        {
            if (Cart.Count == 0)
                throw new InvalidOperationException("أضف منتجاً واحداً على الأقل إلى الفاتورة.");
            if (Session is null)
                throw new UnauthorizedAccessException("انتهت جلسة المستخدم؛ سجل الدخول مجدداً.");
            var customer = SelectedCustomer;
            if (PaymentAmount < 0)
                throw new InvalidOperationException("قيمة الدفع لا يمكن أن تكون سالبة.");
            if (!HasSelectedRegisteredCustomer && PaymentAmount < GrandTotal)
                throw new InvalidOperationException("يجب دفع إجمالي الفاتورة بالكامل عند عدم اختيار عميل مسجل.");
            var tenders = new List<PaymentTender>();
            if (PaymentAmount > 0) tenders.Add(new(SelectedPaymentMethod, PaymentAmount));
            if (HasSelectedRegisteredCustomer && RemainingAmount > 0)
                tenders.Add(new(PaymentMethod.ClientAccount, RemainingAmount));
            lastInvoice = await invoiceService.CompleteSaleAsync(new CheckoutRequest(
                customer?.Id,
                Cart.Select(x => new CheckoutLine(x.Product.Id, x.Quantity, DiscountFixed: x.DiscountAmount)).ToArray(),
                0, 0, tenders,
                Session.UserId, Session.EmployeeId));
            ChangeDue = lastInvoice.ChangeReturned;
            Cart.Clear();
            SelectedLine = null;
            SelectedCustomer = walkInCustomer;
            CustomerSearch = walkInCustomer?.FullName ?? string.Empty;
            IsCustomerPickerOpen = false;
            RefreshTotals();
            StatusMessage = $"تم حفظ الفاتورة {lastInvoice.InvoiceNumber}. الإجمالي: {lastInvoice.GrandTotal:C2}. الباقي: {ChangeDue:C2}";
            await SearchProductsAsync();
            SaleCompleted?.Invoke(this, EventArgs.Empty);
            try
            {
                await printService.PrintReceiptAsync(lastInvoice);
            }
            catch (Exception exception)
            {
                StatusMessage = $"تم حفظ الفاتورة {lastInvoice.InvoiceNumber}، لكن تعذرت معاينتها للطباعة: {exception.Message}";
            }
        }
        catch (Exception exception) { StatusMessage = $"تعذر إتمام الفاتورة: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task PrintLastReceiptAsync()
    {
        if (lastInvoice is null) { StatusMessage = "أتمم عملية بيع قبل طباعة الإيصال."; return; }
        try { await printService.PrintReceiptAsync(lastInvoice); }
        catch (Exception exception) { StatusMessage = $"تعذرت طباعة الإيصال: {exception.Message}"; }
    }

    [RelayCommand]
    private void ClearCart()
    {
        Cart.Clear();
        SelectedLine = null;
        PaymentAmount = 0;
        RefreshTotals();
    }

    private void OnCartLineChanged(object? sender, PropertyChangedEventArgs e) => RefreshTotals();

    private void RefreshTotals()
    {
        var pricing = SalePricingCalculator.Calculate(
            Cart.Select(x => (x.Product, new CheckoutLine(x.Product.Id, x.Quantity, DiscountFixed: x.DiscountAmount))).ToArray(),
            0,
            0);
        Subtotal = pricing.Subtotal;
        DiscountTotal = pricing.DiscountTotal;
        GrandTotal = pricing.GrandTotal;
    }
}
