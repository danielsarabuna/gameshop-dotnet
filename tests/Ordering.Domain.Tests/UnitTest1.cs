using Ordering.Domain.Orders;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;

namespace Ordering.Domain.Tests;

public class UnitTest1
{
    [Fact]
    public void Total_sums_line_totals()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), "A", ProductType.Item, 10m, 2, new Dictionary<string, string>()),
            new OrderItem(Guid.NewGuid(), "B", ProductType.Item, 5m, 1, new Dictionary<string, string>())
        };

        var order = new Order(Guid.NewGuid(), "buyer-1", PaymentMethod.Stripe, items, "EUR", 0m, null, DateTimeOffset.UtcNow);

        Assert.Equal(25m, order.Total);
    }
}
