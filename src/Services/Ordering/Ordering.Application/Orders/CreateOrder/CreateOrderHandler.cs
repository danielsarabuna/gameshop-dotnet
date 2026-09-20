using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Products;

namespace Ordering.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler
{
    private readonly IOrderRepository _repository;
    private readonly ICatalogClient _catalog;
    private readonly IPromoCodeStore _promoCodes;
    private readonly IPlayerIdentityVerifier _identity;

    public CreateOrderHandler(
        IOrderRepository repository,
        ICatalogClient catalog,
        IPromoCodeStore promoCodes,
        IPlayerIdentityVerifier identity)
    {
        _repository = repository;
        _catalog = catalog;
        _promoCodes = promoCodes;
        _identity = identity;
    }

    public async Task<CreateOrderResult> HandleAsync(CreateOrderRequest request, CancellationToken cancellationToken)
        => await HandleAsync(request, CatalogScope.Default, cancellationToken);

    public async Task<CreateOrderResult> HandleAsync(
        CreateOrderRequest request,
        CatalogScope catalogScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GameUserId))
        {
            throw new ArgumentException("GameUserId is required.", nameof(request));
        }

        // Single choke point for identity enforcement: covers the REST endpoint and
        // event-driven order creation (BasketCheckout) alike.
        if (!await _identity.UserExistsAsync(request.GameUserId.Trim(), cancellationToken))
        {
            throw new UnauthorizedAccessException("Unknown game account.");
        }

        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one item.", nameof(request));
        }

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be positive.", nameof(request));
            }
        }

        var products = new List<(CatalogProduct product, int quantity)>(request.Items.Count);
        foreach (var line in request.Items)
        {
            var product = await _catalog.GetProductAsync(line.ProductId, catalogScope, cancellationToken);
            if (product is null)
            {
                throw new ArgumentException("Product not found.", nameof(request));
            }

            if (!product.IsActive)
            {
                throw new ArgumentException("Product is not active.", nameof(request));
            }

            products.Add((product, line.Quantity));
        }

        var currency = products[0].product.Currency;
        if (products.Any(p => !string.Equals(p.product.Currency, currency, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("All products must use the same currency.", nameof(request));
        }

        if (catalogScope.DeliveryContractVersion < 2
            && products.Select(p => p.product.Type).Distinct().Count() > 1)
        {
            throw new ArgumentException(
                "Currency packs and subscriptions must be purchased in separate orders.",
                nameof(request));
        }

        var orderItems = products
            .Select(p => new OrderItem(p.product.Id, p.product.Title, p.product.Type, p.product.Price, p.quantity, p.product.Metadata))
            .ToArray();

        var subtotal = orderItems.Sum(i => i.LineTotal);

        var promoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim();
        var discountAmount = promoCode is null ? 0m : await CalculateDiscountAsync(promoCode, orderItems, subtotal, currency, cancellationToken);

        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            request.GameUserId.Trim(),
            request.PaymentMethod,
            orderItems,
            currency,
            discountAmount,
            promoCode,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(order, cancellationToken);

        return new CreateOrderResult(order.Id, order.Status, order.Subtotal, order.DiscountAmount, order.Total, order.Currency);
    }

    private async Task<decimal> CalculateDiscountAsync(string code, IReadOnlyList<OrderItem> items, decimal subtotal, string currency, CancellationToken cancellationToken)
    {
        var promo = await _promoCodes.GetAsync(code, cancellationToken);
        if (promo is null)
        {
            throw new ArgumentException("Invalid promo code.", nameof(code));
        }

        if (!promo.IsActive)
        {
            throw new ArgumentException("Promo code is not active.", nameof(code));
        }

        var now = DateTimeOffset.UtcNow;
        if (promo.StartsAtUtc is not null && promo.StartsAtUtc.Value > now)
        {
            throw new ArgumentException("Promo code is not active yet.", nameof(code));
        }

        if (promo.ExpiresAtUtc is not null && promo.ExpiresAtUtc.Value < now)
        {
            throw new ArgumentException("Promo code has expired.", nameof(code));
        }

        if (promo.MaxUses > 0 && promo.UsedCount >= promo.MaxUses)
        {
            throw new ArgumentException("Promo code usage limit reached.", nameof(code));
        }

        var eligibleSubtotal = promo.ProductIds.Count == 0
            ? subtotal
            : items.Where(i => promo.ProductIds.Contains(i.ProductId)).Sum(i => i.LineTotal);

        if (eligibleSubtotal <= 0)
        {
            throw new ArgumentException("Promo code is not applicable to selected items.", nameof(code));
        }

        var discount = promo.Type switch
        {
            PromoCodes.DiscountType.Percent => eligibleSubtotal * (promo.Value / 100m),
            PromoCodes.DiscountType.FixedAmount => string.Equals(promo.Currency, currency, StringComparison.OrdinalIgnoreCase)
                ? Math.Min(promo.Value, eligibleSubtotal)
                : throw new ArgumentException("Promo code is not applicable for this currency.", nameof(code)),
            _ => 0m
        };
        return decimal.Round(discount, 2, MidpointRounding.AwayFromZero);
    }
}
