namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private static readonly IReadOnlyList<CatalogItem> Items =
    [
        new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title: "Space Raiders",
            Description: "Arcade shooter with roguelike elements.",
            Price: 19.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title: "Dungeon Builder",
            Description: "Strategy game about building the perfect dungeon.",
            Price: 29.99m,
            ImageUrl: null
        ),
        new(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Title: "Racing Neon",
            Description: "Futuristic racing with synthwave vibes.",
            Price: 14.99m,
            ImageUrl: null
        )
    ];

    public IReadOnlyList<CatalogItem> GetAll() => Items;

    public CatalogItem? GetById(Guid id) => Items.FirstOrDefault(item => item.Id == id);
}

