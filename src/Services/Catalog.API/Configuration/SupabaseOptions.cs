namespace Catalog.API.Configuration;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = "https://fapfdazadqistvxewbjq.supabase.co";
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string DefaultRegion { get; set; } = "russia";
    public string DefaultStore { get; set; } = "ru_store";
    public string DefaultGameVersion { get; set; } = "0.0.36";
}
