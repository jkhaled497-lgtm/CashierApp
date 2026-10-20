using System.Globalization;
using Cashier.Application;
using Cashier.Domain;

namespace Cashier.UI;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool visible && !visible ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool enabled && !enabled;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool enabled && !enabled;
}

public sealed class PositiveStockConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        decimal.TryParse(value?.ToString(), out var stock) && stock > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NotNullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DateOnlyToDateTimeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime date ? DateOnly.FromDateTime(date) : Binding.DoNothing;
}

public sealed class EgyptDateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            DateTimeOffset timestamp =>
                BusinessTime.ToEgyptLocal(timestamp).ToString("dd-MM-yyyy hh:mm tt", CultureInfo.GetCultureInfo("ar-EG")),
            null => string.Empty,
            _ => Binding.DoNothing
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ArabicEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        UserRole.Admin => "مدير النظام",
        UserRole.Manager => "مدير",
        UserRole.Cashier => "كاشير",
        PaymentMethod.Cash => "نقداً",
        PaymentMethod.DebitCard => "بطاقة مدى",
        PaymentMethod.CreditCard => "بطاقة ائتمان",
        PaymentMethod.ClientAccount => "على حساب العميل",
        SupplierBalanceEntryType.OpeningBalance => "رصيد افتتاحي",
        SupplierBalanceEntryType.Charge => "إضافة مستحق",
        SupplierBalanceEntryType.Payment => "تسديد",
        StockAdjustmentType.StockIn => "زيادة رصيد المحل",
        StockAdjustmentType.StockOut => "إخراج من المحل",
        StockAdjustmentType.Damaged => "تالف من المحل",
        StockAdjustmentType.Expired => "منتهي الصلاحية",
        StockAdjustmentType.WarehouseIn => "إضافة إلى المخزن",
        StockAdjustmentType.WarehouseOut => "إخراج من المخزن",
        StockAdjustmentType.TransferToShop => "إخراج من المخزن",
        CustomerLedgerEntryType.InvoiceCharge => "مبلغ آجل من فاتورة",
        CustomerLedgerEntryType.InvoicePayment => "دفعة فاتورة",
        CustomerLedgerEntryType.BalancePayment => "سداد رصيد",
        CustomerLedgerEntryType.BalanceCharge => "إضافة استحقاق",
        DailyJournalType.SalesInvoice => "فاتورة بيع",
        DailyJournalType.PurchaseInvoice => "فاتورة شراء",
        DailyJournalType.DailySale => "بيع من الشيت اليومي",
        DailyJournalType.WarehouseEntry => "إضافة إلى المخزن",
        DailyJournalType.WarehouseTransfer => "تحويل من المخزن للمحل",
        DailyJournalType.WarehouseExit => "إخراج من المخزن",
        DailyJournalType.CustomerCharge => "استحقاق عميل",
        DailyJournalType.CustomerPayment => "سداد عميل",
        DailyJournalType.SupplierCharge => "استحقاق مورد",
        DailyJournalType.SupplierPayment => "سداد مورد",
        DailyJournalType.Expense => "مصروف",
        PurchaseInvoiceStatus.Unpaid => "غير مدفوعة",
        PurchaseInvoiceStatus.Partial => "مدفوعة جزئياً",
        PurchaseInvoiceStatus.Paid => "مدفوعة",
        ProductUnit.Piece => "قطعة",
        ProductUnit.Kg => "كيلوغرام",
        ProductUnit.Box => "صندوق",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
