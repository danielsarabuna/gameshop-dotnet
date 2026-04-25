namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private static readonly IReadOnlyList<CatalogItem> Items =
    [
        new(
            Id: Guid.Parse("6b3f7e39-cac9-408e-a9f9-c109007cc921"),
            Title: "30 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 0.49m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "30" },
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000060"),
            Title: "60 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 1.23m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "60" },
            ImageUrl: null
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
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000350"),
            Title: "350 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 4.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "350" },
            ImageUrl: null
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
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000002000"),
            Title: "2000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 19.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "2000" },
            ImageUrl: null
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
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000009000"),
            Title: "9000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 69.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "9000" },
            ImageUrl: null
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
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000025000"),
            Title: "25000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Type: CatalogProductType.Currency,
            Price: 149.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["diamonds"] = "25000" },
            ImageUrl: null
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
            ImageUrl: null
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
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000012"),
            Title: "Premium — 12 months",
            Description: "Premium subscription (12 months).",
            Type: CatalogProductType.Subscription,
            Price: 59.99m,
            Currency: "EUR",
            IsActive: true,
            Metadata: new Dictionary<string, string> { ["months"] = "12" },
            ImageUrl: null
        )
    ];

    public IReadOnlyList<CatalogItem> GetAll() => Items;

    public CatalogItem? GetById(Guid id) => Items.FirstOrDefault(item => item.Id == id);
}
