namespace Catalog.API.Services;

internal static class CatalogAssetPath
{
    public static bool TryResolve(
        string cacheRoot,
        string region,
        string store,
        string version,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;

        if (!IsSafeSegment(region)
            || !IsSafeSegment(store)
            || !IsSafeSegment(version)
            || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var assetSegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (assetSegments.Length == 0 || assetSegments.Any(segment => !IsSafeSegment(segment)))
        {
            return false;
        }

        var root = Path.GetFullPath(cacheRoot);
        var candidate = Path.GetFullPath(Path.Combine([root, region, store, version, .. assetSegments]));
        var relativeToRoot = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relativeToRoot)
            || relativeToRoot.Equals("..", StringComparison.Ordinal)
            || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsSafeSegment(string value)
        => value.Length is > 0 and <= 128
            && value is not "." and not ".."
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
