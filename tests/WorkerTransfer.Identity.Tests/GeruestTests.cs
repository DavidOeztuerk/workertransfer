using FluentAssertions;
using Girder.Core.Identity;
using Girder.Infrastructure.Security.Keys;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The scaffold carries Girder, and the layers point inwards.
/// </summary>
/// <remarks>
/// Deleted as soon as the first real test exists. Until then it is the only
/// thing standing between "it builds" and "it works", and the difference is
/// what an empty scaffold is worst at showing.
/// </remarks>
public class GeruestTests
{
    [Fact]
    public void Girders_identity_primitives_are_available()
    {
        var subject = SubjectId.New();
        var tenant = TenantId.New();

        Principal.Company(subject, tenant).Acting.Should().BeOfType<Capacity.ForCompany>();
        Principal.Person(subject).Acting.Should().BeOfType<Capacity.AsSelf>();
    }

    /// <summary>
    /// The property the whole key arrangement rests on, checked once here so
    /// that a wrong package reference is noticed now and not in step 1.
    /// </summary>
    [Fact]
    public void A_public_key_cannot_sign()
    {
        var pair = SigningKey.GenerateKeyPair(kid: "geruest");

        SigningKey.FromEcdsaPrivateKey(pair.PrivateKey, pair.Kid).CanSign.Should().BeTrue();
        SigningKey.FromEcdsaPublicKey(pair.PublicKey, pair.Kid).CanSign.Should().BeFalse();
    }
}
