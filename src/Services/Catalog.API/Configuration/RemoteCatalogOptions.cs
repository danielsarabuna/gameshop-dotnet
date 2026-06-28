namespace Catalog.API.Configuration;

public sealed class RemoteCatalogOptions
{
    public const string SectionName = "Catalog:Remote";

    public string SupabaseBaseUrl { get; set; } = "https://fapfdazadqistvxewbjq.supabase.co";
    public string Bucket { get; set; } = "dev";

    /// <summary>How long a cached catalog config is considered fresh before ETag revalidation.</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>Local directory where downloaded catalog assets (diamonds/premium images) are cached.</summary>
    public string AssetCacheDir { get; set; } = "App_Data/catalog-assets";
}
