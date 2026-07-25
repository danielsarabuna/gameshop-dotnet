using System.IdentityModel.Tokens.Jwt;
using BuildingBlocks.Auth;

namespace Catalog.API.Tests;

public sealed class GameTicketTokenIssuerTests
{
    [Fact]
    public void Recipient_token_is_short_lived_and_scoped_to_checkout()
    {
        var issuer = new GameTicketTokenIssuer(new GameTicketJwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            LifetimeMinutes = 15
        });

        var before = DateTimeOffset.UtcNow;
        var (accessToken, expiresAtUtc) = issuer.Issue(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "russia",
            "ru_store",
            "0.0.1",
            "recipient",
            2);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Equal("recipient", token.Claims.Single(claim => claim.Type == "webshop_session").Value);
        Assert.Equal("webshop.checkout", token.Claims.Single(claim => claim.Type == "scope").Value);
        Assert.Equal("2", token.Claims.Single(claim => claim.Type == "webshop_delivery_contract").Value);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", token.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.InRange(expiresAtUtc, before.AddMinutes(14), before.AddMinutes(16));
    }
}
