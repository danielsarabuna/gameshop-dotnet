using Ordering.Application.Abstractions;

namespace Ordering.Application.PromoCodes;

public sealed class ApplyPromoCodeHandler
{
    private readonly ICatalogClient _catalog;
    private readonly IPromoCodeStore _promoCodes;

    public ApplyPromoCodeHandler(ICatalogClient catalog, IPromoCodeStore promoCodes)
    {
        _catalog = catalog;
        _promoCodes = promoCodes;
    }

    public async Task<ApplyPromoCodeResult> HandleAsync(ApplyPromoCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Invalid("Promo code is required.");
        }

        if (request.Items.Count == 0)
        {
            return Invalid("At least one item is required.");
        }

        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                return Invalid("Quantity must be positive.");
            }
        }

        var products = new List<(CatalogProduct product, int quantity)>(request.Items.Count);
        foreach (var line in request.Items)
        {
            var product = await _catalog.GetProductAsync(line.ProductId, cancellationToken);
            if (product is null)
            {
                return Invalid("Product not found.");
            }

            if (!product.IsActive)
            {
                return Invalid("Product is not active.");
            }

            products.Add((product, line.Quantity));
        }

        var currency = products[0].product.Currency;
        if (products.Any(p => !string.Equals(p.product.Currency, currency, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid("All products must use the same currency.");
        }

        var subtotal = products.Sum(p => p.product.Price * p.quantity);

        var promo = _promoCodes.Get(request.Code.Trim());
        if (promo is null)
        {
            return Invalid("Invalid promo code.");
        }

        if (!promo.IsActive)
        {
            return Invalid("Promo code is not active.");
        }

        var now = DateTimeOffset.UtcNow;
        if (promo.StartsAtUtc is not null && promo.StartsAtUtc.Value > now)
        {
            return Invalid("Promo code is not active yet.");
        }

        if (promo.ExpiresAtUtc is not null && promo.ExpiresAtUtc.Value < now)
        {
            return Invalid("Promo code has expired.");
        }

        if (promo.MaxUses > 0 && promo.UsedCount >= promo.MaxUses)
        {
            return Invalid("Promo code usage limit reached.");
        }

        var eligibleSubtotal = promo.ProductIds.Count == 0
            ? subtotal
            : products
                .Where(p => promo.ProductIds.Contains(p.product.Id))
                .Sum(p => p.product.Price * p.quantity);

        if (eligibleSubtotal <= 0)
        {
            return Invalid("Promo code is not applicable to selected items.");
        }

        var discountAmount = promo.Type switch
        {
            DiscountType.Percent => eligibleSubtotal * (promo.Value / 100m),
            DiscountType.FixedAmount => string.Equals(promo.Currency, currency, StringComparison.OrdinalIgnoreCase)
                ? Math.Min(promo.Value, eligibleSubtotal)
                : 0m,
            _ => 0m
        };

        if (discountAmount <= 0)
        {
            return Invalid("Promo code is not applicable for this currency.");
        }

        var total = Math.Max(0, subtotal - discountAmount);
        return new ApplyPromoCodeResult(true, null, subtotal, discountAmount, total, currency);
    }

    private static ApplyPromoCodeResult Invalid(string error) => new(false, error, 0m, 0m, 0m, string.Empty);
}

