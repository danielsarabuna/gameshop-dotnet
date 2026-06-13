using Catalog.Grpc;
using Google.Protobuf;

namespace WebShop.IntegrationTests;

/// <summary>
/// Smoke tests proving that the generated proto3 contract remains
/// forward- and backward-compatible across schema evolution.
/// See docs/grpc-versioning.md for the rules these tests enforce.
/// </summary>
public sealed class CatalogGrpcCompatibilityTests
{
    [Fact]
    public void Forward_compat_unknown_fields_are_ignored_by_parser()
    {
        // Simulate a NEW server that emitted a CatalogProduct with an
        // additional field number 999 unknown to the current schema.
        var known = new CatalogProduct
        {
            Id = Guid.NewGuid().ToString("D"),
            Title = "Diamonds",
            Description = "100 diamonds",
            Type = Catalog.Grpc.CatalogProductType.Currency,
            Price = "4.99",
            Currency = "EUR",
            IsActive = true,
            ImageUrl = "https://cdn/diamonds.png"
        };

        using var ms = new MemoryStream();
        // Serialize the known portion.
        known.WriteTo(ms);

        // Append an unknown field: number=999, wire type=Varint, value=42.
        var output = new CodedOutputStream(ms, leaveOpen: true);
        output.WriteTag(999, WireFormat.WireType.Varint);
        output.WriteInt32(42);
        output.Flush();

        var bytes = ms.ToArray();

        // A current client must not throw; unknown fields are preserved
        // (or skipped) silently by the proto3 runtime.
        var roundtrip = CatalogProduct.Parser.ParseFrom(bytes);

        Assert.Equal(known.Id, roundtrip.Id);
        Assert.Equal(known.Title, roundtrip.Title);
        Assert.Equal(known.Description, roundtrip.Description);
        Assert.Equal(known.Type, roundtrip.Type);
        Assert.Equal(known.Price, roundtrip.Price);
        Assert.Equal(known.Currency, roundtrip.Currency);
        Assert.Equal(known.IsActive, roundtrip.IsActive);
        Assert.Equal(known.ImageUrl, roundtrip.ImageUrl);
    }

    [Fact]
    public void Backward_compat_missing_optional_fields_decode_to_proto3_defaults()
    {
        // Simulate an OLDER server that only set a couple of fields.
        var partial = new CatalogProduct
        {
            Id = Guid.NewGuid().ToString("D"),
            Title = "Old payload"
        };

        var bytes = partial.ToByteArray();
        var parsed = CatalogProduct.Parser.ParseFrom(bytes);

        Assert.Equal(partial.Id, parsed.Id);
        Assert.Equal(partial.Title, parsed.Title);

        // Unset proto3 scalars decode as their type defaults — no exceptions.
        Assert.Equal(string.Empty, parsed.Description);
        Assert.Equal(Catalog.Grpc.CatalogProductType.Unspecified, parsed.Type);
        Assert.Equal(string.Empty, parsed.Price);
        Assert.Equal(string.Empty, parsed.Currency);
        Assert.False(parsed.IsActive);
        Assert.Equal(string.Empty, parsed.ImageUrl);
        Assert.Empty(parsed.Metadata);
    }

    [Fact]
    public void Unknown_enum_value_does_not_break_parser()
    {
        // Construct CatalogProduct bytes manually: only the Type field set
        // to an integer value that is NOT in CatalogProductType enum.
        // Field number 4 (Type), wire type Varint.
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);
        output.WriteTag(4, WireFormat.WireType.Varint);
        output.WriteInt32(99); // not a known enum value
        output.Flush();

        var bytes = ms.ToArray();
        var parsed = CatalogProduct.Parser.ParseFrom(bytes);

        // proto3 returns the raw integer cast to the enum; client code
        // must defensively map this via a default branch (see GrpcCatalogClient.MapType).
        Assert.Equal(99, (int)parsed.Type);
    }

    [Fact]
    public void Repeated_serialization_is_deterministic()
    {
        // Sanity: same fields → same bytes. Catches accidental introduction
        // of non-deterministic features (timestamps, randomized order, etc.).
        var product = new CatalogProduct
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Title = "Stable",
            Price = "1.00",
            Currency = "EUR",
            IsActive = true
        };

        var first = product.ToByteArray();
        var second = product.ToByteArray();

        Assert.Equal(first, second);
    }
}
