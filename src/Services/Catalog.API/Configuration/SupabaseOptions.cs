namespace Catalog.API.Configuration;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = "https://fapfdazadqistvxewbjq.supabase.co";
    public string ServiceRoleKey { get; set; } = string.Empty;

    /// <summary>
    /// Fallbacks used when a request arrives WITHOUT region/store context
    /// (e.g. an anonymous visitor who did not come from the game deeplink).
    /// CatalogProvider resolves the highest published config version for the selected region/store.
    /// </summary>
    public string DefaultRegion { get; set; } = "global";
    public string DefaultStore { get; set; } = "global";
    public string DefaultGameVersion { get; set; } = "global";
}
