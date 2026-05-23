namespace Catalog.API.Storage;

public sealed class MongoCatalogOptions
{
    public string? ConnectionString { get; set; }
    public string? Database { get; set; }
    public string? Collection { get; set; }
    public bool? SeedOnStart { get; set; }
}
