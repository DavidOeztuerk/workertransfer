using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>The ring has a key of its own, and what lies under the master key stays open.</summary>
/// <remarks>
/// <para><strong>A fixed ciphertext, and no round trip.</strong>
/// <c>SecretStore.Decrypt</c> answers <c>null</c> on every failure, and the
/// interface then says "none stored". A change to the key derivation would pass
/// any round trip and silently lose every stored AI key. This value came out of
/// the code as it stood before the ring got its own key.</para>
///
/// <para><strong>Two types and not two registrations of one.</strong> In identity
/// both stand in the same container, the last registration of a type wins, and
/// the ring would lie under the master key again without anybody seeing it.</para>
/// </remarks>
public sealed class KeyRingSecretTests
{
    private const string MasterKey = "the-master-key-before-the-split";
    private const string StoredKey = "sk-stored-before-the-split";
    private const string SealedBeforeTheSplit =
        "R6cWBTygJY5beoc+v0BuIB9ZI9hS4g1XVmFdwREcY6NlGpDuOVfGpv/EvxswnbyP9VvTvbal";

    private static IConfiguration Settings(string variable, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [variable] = value })
            .Build();

    [Fact]
    public void An_ai_key_stored_before_the_split_still_opens() =>
        new SecretStore(Settings(SecretStore.Variable, MasterKey))
            .Decrypt(SealedBeforeTheSplit)
            .Should().Be(StoredKey);

    [Fact]
    public void The_ring_key_opens_nothing_that_lies_under_the_master_key() =>
        new KeyRingSecret(Settings(KeyRingSecret.Variable, "the-ring-key"))
            .Decrypt(SealedBeforeTheSplit)
            .Should().BeNull();

    [Fact]
    public void The_ring_opens_what_it_sealed()
    {
        var ring = new KeyRingSecret(Settings(KeyRingSecret.Variable, "the-ring-key"));

        ring.Decrypt(ring.Encrypt("a key of the ring")).Should().Be("a key of the ring");
    }

    /// <summary>The master key is no fallback, or it would stay in every service.</summary>
    [Fact]
    public void The_ring_does_not_fall_back_on_the_master_key()
    {
        var reading = () => new KeyRingSecret(Settings(SecretStore.Variable, MasterKey));

        reading.Should().Throw<MasterKeyMissing>()
            .WithMessage($"*{KeyRingSecret.Variable}*");
    }

    /// <summary>Fourteen services must not need the master key to come up.</summary>
    [Fact]
    public void The_ring_brings_no_master_key_along()
    {
        var services = new ServiceCollection();

        services.AddKeyRing<DbContext>();

        services.Should().Contain(service => service.ServiceType == typeof(KeyRingSecret));
        services.Should().NotContain(service => service.ServiceType == typeof(SecretStore));
    }
}
