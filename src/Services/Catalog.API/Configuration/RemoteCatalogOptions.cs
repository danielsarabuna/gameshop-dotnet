namespace Catalog.API.Configuration;

public sealed class RemoteCatalogOptions
{
    public const string SectionName = "Catalog:Remote";

    public string SupabaseBaseUrl { get; set; } = "https://fapfdazadqistvxewbjq.supabase.co";
    public string Bucket { get; set; } = "dev";
    public int PollIntervalSeconds { get; set; } = 30;
    public bool EnablePolling { get; set; } = true;
    public List<string> Regions { get; set; } = ["russia", "global"];
    public List<string> Stores { get; set; } = ["ru_store", "google_play", "app_store"];
    public List<string> GameVersions { get; set; } = ["0.0.36", "0.0.35", "0.0.33"];
}
