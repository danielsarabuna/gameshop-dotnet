using System.Text.Json;
using IntegrationEvents;

namespace Ordering.Domain.Tests;

public class IntegrationEventContractTests
{
    [Fact]
    public void OrderCompleted_roundtrips_through_json()
    {
        var original = new OrderCompleted(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OccurredAtUtc: new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero),
            OrderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            GameUserId: "user-1",
            Total: 19.99m,
            Currency: "EUR",
            PaymentId: "pi_abc",
            PaidAtUtc: new DateTimeOffset(2026, 4, 25, 12, 0, 5, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<OrderCompleted>(json);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void BasketCheckout_roundtrips_through_json()
    {
        var original = new BasketCheckout(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OccurredAtUtc: new DateTimeOffset(2026, 4, 25, 9, 30, 0, TimeSpan.Zero),
            UserId: "user-42",
            Items: new[]
            {
                new BasketCheckoutItem(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Diamonds 100", 4.99m, 2),
                new BasketCheckoutItem(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Premium Month", 9.99m, 1)
            },
            Total: 19.97m,
            Currency: "EUR");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<BasketCheckout>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.UserId, restored.UserId);
        Assert.Equal(original.Total, restored.Total);
        Assert.Equal(original.Currency, restored.Currency);
        Assert.Equal(original.Items.Count, restored.Items.Count);
        Assert.Equal(original.Items[0], restored.Items[0]);
        Assert.Equal(original.Items[1], restored.Items[1]);
    }
}
