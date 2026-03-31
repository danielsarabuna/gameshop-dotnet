namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private static readonly IReadOnlyList<CatalogItem> Items =
    [
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000060"),
            Title: "60 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 1.23m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "60" },
            ImageUrl: "/images/diamonds_60.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000120"),
            Title: "120 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 1.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "120" },
            ImageUrl: "/images/diamonds_120.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000350"),
            Title: "350 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 4.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string>
            {
                ["diamonds"] = "350",
                ["badgeKey"] = "best",
                ["badgeKind"] = "purple"
            },
            ImageUrl: "/images/diamonds_350.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000800"),
            Title: "800 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 9.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "800" },
            ImageUrl: "/images/diamonds_800.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000002000"),
            Title: "2000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 19.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string>
            {
                ["diamonds"] = "2000",
                ["badgeKey"] = "top",
                ["badgeKind"] = "gold"
            },
            ImageUrl: "/images/diamonds_2000.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000004500"),
            Title: "4500 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 39.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "4500" },
            ImageUrl: "/images/diamonds_4500.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000009000"),
            Title: "9000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 69.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string>
            {
                ["diamonds"] = "9000",
                ["badgeKey"] = "hot",
                ["badgeKind"] = "purple"
            },
            ImageUrl: "/images/diamonds_9000.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000015000"),
            Title: "15000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 99.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "15000" },
            ImageUrl: "/images/diamonds_15000.png"
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000025000"),
            Title: "25000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 149.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string>
            {
                ["diamonds"] = "25000",
                ["badgeKey"] = "mega",
                ["badgeKind"] = "gold"
            },
            ImageUrl: "/images/diamonds_25000.png"
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000001"),
            Title: "Premium — 1 month",
            Description: "Premium subscription (1 month).",
            Type: CatalogProductType.Subscription,
            Price: 6.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["months"] = "1" },
            ImageUrl: "/images/premium_1.png"
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000003"),
            Title: "Premium — 3 months",
            Description: "Premium subscription (3 months).",
            Type: CatalogProductType.Subscription,
            Price: 17.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["months"] = "3" },
            ImageUrl: "/images/premium_3.png"
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000012"),
            Title: "Premium — 12 months",
            Description: "Premium subscription (12 months).",
            Type: CatalogProductType.Subscription,
            Price: 59.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string>
            {
                ["months"] = "12",
                ["badgeKey"] = "best",
                ["badgeKind"] = "gold"
            },
            ImageUrl: "/images/premium_12.png"
        )
    ];

    public IReadOnlyList<CatalogItem> GetAll() => Items;

    public CatalogItem? GetById(Guid id) => Items.FirstOrDefault(item => item.Id == id);
}
