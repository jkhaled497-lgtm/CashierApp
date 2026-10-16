using Cashier.Domain;

namespace Cashier.Application;

public sealed record PricedSaleLine(
    Product Product,
    decimal Quantity,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record SalePricing(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Rounding,
    decimal GrandTotal,
    IReadOnlyList<PricedSaleLine> Lines);

public static class SalePricingCalculator
{
    public static SalePricing Calculate(
        IReadOnlyList<(Product Product, CheckoutLine Line)> items,
        decimal globalDiscountPercent,
        decimal globalDiscountFixed)
    {
        if (items.Count == 0)
            return new(0, 0, 0, 0, 0, []);
        if (globalDiscountPercent is < 0 or > 100 || globalDiscountFixed < 0)
            throw new ArgumentOutOfRangeException(nameof(globalDiscountPercent), "يجب أن يكون الخصم العام بين صفر و١٠٠٪، وألا تكون قيمة الخصم الثابت سالبة.");

        var grossAmounts = new decimal[items.Count];
        var itemDiscounts = new decimal[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var (product, line) = items[index];
            if (line.Quantity <= 0 || line.DiscountPercent is < 0 or > 100 || line.DiscountFixed < 0)
                throw new ArgumentOutOfRangeException(nameof(items), "يجب أن تكون الكمية موجبة والخصومات صالحة.");
            grossAmounts[index] = product.SellingPrice * line.Quantity;
            itemDiscounts[index] = Math.Min(grossAmounts[index],
                Money(grossAmounts[index] * line.DiscountPercent / 100m + line.DiscountFixed));
        }

        var subtotalRaw = grossAmounts.Sum();
        var afterItemDiscount = subtotalRaw - itemDiscounts.Sum();
        var globalDiscount = Math.Min(afterItemDiscount,
            Money(afterItemDiscount * globalDiscountPercent / 100m + globalDiscountFixed));
        var globalAllocations = AllocateDiscount(grossAmounts, itemDiscounts, afterItemDiscount, globalDiscount);
        var lines = new List<PricedSaleLine>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var (product, line) = items[index];
            var netAmount = grossAmounts[index] - itemDiscounts[index] - globalAllocations[index];
            lines.Add(new PricedSaleLine(
                product, line.Quantity,
                Money(itemDiscounts[index] + globalAllocations[index]),
                0,
                Money(netAmount)));
        }

        return new SalePricing(
            Money(subtotalRaw),
            Money(itemDiscounts.Sum() + globalDiscount),
            Money(lines.Sum(x => x.TaxAmount)),
            0,
            Money(lines.Sum(x => x.LineTotal)),
            lines);
    }

    private static decimal[] AllocateDiscount(
        IReadOnlyList<decimal> grossAmounts,
        IReadOnlyList<decimal> itemDiscounts,
        decimal afterItemDiscount,
        decimal globalDiscount)
    {
        var allocations = new decimal[grossAmounts.Count];
        if (globalDiscount == 0 || afterItemDiscount == 0)
            return allocations;

        var remaining = globalDiscount;
        for (var index = 0; index < grossAmounts.Count; index++)
        {
            var net = grossAmounts[index] - itemDiscounts[index];
            allocations[index] = index == grossAmounts.Count - 1
                ? remaining
                : Math.Min(net, decimal.Floor(globalDiscount * net / afterItemDiscount * 100m) / 100m);
            remaining -= allocations[index];
        }
        return allocations;
    }

    private static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
