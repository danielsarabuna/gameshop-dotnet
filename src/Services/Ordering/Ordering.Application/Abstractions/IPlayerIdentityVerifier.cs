namespace Ordering.Application.Abstractions;

/// <summary>
/// Validates that an order's target game account actually exists in the game backend.
/// Prevents crediting arbitrary/spoofed GameUserId values.
/// </summary>
public interface IPlayerIdentityVerifier
{
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Dev fallback used when no game backend is configured — allows everything.</summary>
public sealed class AllowAllIdentityVerifier : IPlayerIdentityVerifier
{
    public static readonly AllowAllIdentityVerifier Instance = new();

    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(true);
}
