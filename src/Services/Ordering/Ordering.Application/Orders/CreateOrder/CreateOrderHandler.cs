using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler
{
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateOrderResult> HandleAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            throw new ArgumentException("BuyerId is required.", nameof(request));
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

            if (item.UnitPrice < 0)
            {
                throw new ArgumentException("UnitPrice cannot be negative.", nameof(request));
            }
        }

        var orderId = Guid.NewGuid();
        var orderItems = request.Items
            .Select(i => new OrderItem(i.ProductId, i.Title, i.UnitPrice, i.Quantity))
            .ToArray();

        var order = new Order(orderId, request.BuyerId, orderItems, DateTimeOffset.UtcNow);

        await _repository.AddAsync(order, cancellationToken);

        return new CreateOrderResult(order.Id, order.Total);
    }
}

