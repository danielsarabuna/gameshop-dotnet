namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private static readonly IReadOnlyList<CatalogItem> Items =
    [
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000060"),
            Title: "60 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 0.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000120"),
            Title: "120 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 1.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000350"),
            Title: "350 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 4.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000000800"),
            Title: "800 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 9.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000002000"),
            Title: "2000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 19.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000004500"),
            Title: "4500 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 39.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000009000"),
            Title: "9000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 69.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000015000"),
            Title: "15000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 99.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("d1a00000-0000-0000-0000-000000025000"),
            Title: "25000 diamonds",
            Description: "In‑game currency pack (diamonds).",
            Price: 149.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000001"),
            Title: "Premium — 1 month",
            Description: "Premium subscription (1 month).",
            Price: 6.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000003"),
            Title: "Premium — 3 months",
            Description: "Premium subscription (3 months).",
            Price: 17.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("9aa00000-0000-0000-0000-000000000012"),
            Title: "Premium — 12 months",
            Description: "Premium subscription (12 months).",
            Price: 59.99m,
            ImageUrl: null
        )
    ];

    public IReadOnlyList<CatalogItem> GetAll() => Items;

    public CatalogItem? GetById(Guid id) => Items.FirstOrDefault(item => item.Id == id);
}
