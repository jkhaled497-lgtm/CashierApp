using Cashier.Application;
using Cashier.Domain;

namespace Cashier.Tests;

public sealed class SalePricingCalculatorTests
{
    [Fact]
    public void Calculate_AllocatesGlobalDiscountWithoutApplyingTax()
    {
        var first = Product(1, "A", 10m, 10m);
        var second = Product(2, "B", 5m, 20m);
        var items = new[]
        {
            (first, new CheckoutLine(first.Id, 1m, DiscountPercent: 10m)),
            (second, new CheckoutLine(second.Id, 2m))
        };

        var result = SalePricingCalculator.Calculate(items, globalDiscountPercent: 10m, globalDiscountFixed: 0m);

        Assert.Equal(20m, result.Subtotal);
        Assert.Equal(2.90m, result.DiscountTotal);
        Assert.Equal(0m, result.TaxTotal);
        Assert.Equal(17.10m, result.GrandTotal);
        Assert.Equal(17.10m, result.Lines.Sum(x => x.LineTotal));
        Assert.Equal(1.90m, result.Lines[0].DiscountAmount);
        Assert.Equal(0m, result.Lines[0].TaxAmount);
    }

    [Fact]
    public void Calculate_CapsFixedDiscountAtLineAndCartValue()
    {
        var product = Product(1, "A", 2m, 0m);
        var result = SalePricingCalculator.Calculate(
            [(product, new CheckoutLine(product.Id, 1m, DiscountFixed: 5m))],
            globalDiscountPercent: 0m,
            globalDiscountFixed: 9m);

        Assert.Equal(2m, result.Subtotal);
        Assert.Equal(2m, result.DiscountTotal);
        Assert.Equal(0m, result.GrandTotal);
    }

    [Fact]
    public void Calculate_AppliesLineDiscountAsCurrencyAmount()
    {
        var product = Product(1, "A", 10m, 0m);

        var result = SalePricingCalculator.Calculate(
            [(product, new CheckoutLine(product.Id, 2m, DiscountFixed: 3m))],
            globalDiscountPercent: 0m,
            globalDiscountFixed: 0m);

        Assert.Equal(3m, result.Lines[0].DiscountAmount);
        Assert.Equal(17m, result.Lines[0].LineTotal);
        Assert.Equal(17m, result.GrandTotal);
    }

    [Fact]
    public void Calculate_RejectsInvalidQuantity()
    {
        var product = Product(1, "A", 2m, 0m);

        Assert.Throws<ArgumentOutOfRangeException>(() => SalePricingCalculator.Calculate(
            [(product, new CheckoutLine(product.Id, 0m))], 0m, 0m));
    }

    private static Product Product(int id, string name, decimal price, decimal taxRate) =>
        new() { Id = id, Sku = name, Name = name, SellingPrice = price, TaxRate = taxRate };
}
