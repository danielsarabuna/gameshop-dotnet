using Ordering.Domain.Orders;

namespace Ordering.Domain.Tests;

public class UnitTest1
{
    [Fact]
    public void Total_sums_line_totals()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), "A", 10m, 2),
            new OrderItem(Guid.NewGuid(), "B", 5m, 1)
        };

        var order = new Order(Guid.NewGuid(), "buyer-1", items, DateTimeOffset.UtcNow);

        Assert.Equal(25m, order.Total);
    }
}
