using System.Data.Common;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Where the framework's key ring lies and what it is protected with.</summary>
public static class KeyRing
{
    /// <summary>Separates this platform's ring from foreign ones on the same store.</summary>
    /// <remarks>
    /// One name for all fifteen and not one per service: the framework derives
    /// its purposes from it, and two services that are one day meant to verify
    /// the same thing could never do so under different names.
    /// </remarks>
    public const string ApplicationName = "workertransfer";

    /// <summary>Puts the key ring into the database and encrypts it.</summary>
    /// <remarks>
    /// <para><strong>Without this call ASP.NET writes the ring into the
    /// container's file system</strong> and warns twice at startup. Whatever is
    /// protected with it — a sign-in cookie, an anti-forgery token, a reset
    /// link — then no longer verifies as soon as the container is replaced, and
    /// a second instance does not verify at all what the first one issued.</para>
    ///
    /// <para><strong>The database and no in-process cache.</strong> Noelia's
    /// <c>UseDataProtection</c> puts the ring into the registered cache
    /// provider; the only one without Redis would be <c>Noelia.InMemory</c>,
    /// and that dies with the process. It would have made the check green
    /// without changing anything — two instances would still read different
    /// rings.</para>
    ///
    /// <para><strong>Through the context and not through Npgsql.</strong> This
    /// package deliberately holds no driver dependency; the statements below
    /// are PostgreSQL syntax, though, and a service on another database would
    /// have to replace them. The table comes into being here and not in a
    /// schema migration: it belongs to no domain model, and a <c>DbSet</c> for
    /// it would make it look like a SubjectKeyedTable in
    /// <c>ErasureRecipientTests</c>.</para>
    /// </remarks>
    /// <typeparam name="TContext">The context whose database holds the ring.</typeparam>
    /// <param name="services">The container.</param>
    /// <returns>The container.</returns>
    public static IServiceCollection AddKeyRing<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // A type of its own, because in identity-service SecretStore stands in
        // the same container: of two registrations of one type the last wins,
        // and the ring would lie under the master key again (ADR-0058).
        services.TryAddSingleton<KeyRingSecret>();

        services.AddSingleton<IXmlRepository, KeyRingStore<TContext>>();
        // Only the encryptor comes from the container. The decryptor is rebuilt
        // by the framework from the type name stored in each key — see
        // `MasterKeyEnvelope`.
        services.AddSingleton<IXmlEncryptor, MasterKeyEncryptor>();
        services.AddDataProtection().SetApplicationName(ApplicationName);

        // Out of the container and not through the builder: its
        // PersistKeysTo*/ProtectKeysWith* overloads take instances, and these
        // two need services that only stand after this call.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(
            provider => new KeyRingConnection(provider));

        return services;
    }

    private sealed class KeyRingConnection(IServiceProvider provider)
        : IConfigureOptions<KeyManagementOptions>
    {
        public void Configure(KeyManagementOptions options)
        {
            options.XmlRepository = provider.GetRequiredService<IXmlRepository>();
            options.XmlEncryptor = provider.GetRequiredService<IXmlEncryptor>();
        }
    }
}

/// <summary>The key ring in this service's database.</summary>
/// <remarks>
/// It takes the <c>IServiceProvider</c> and opens a scope of its own: the
/// framework resolves the ring as a singleton, and a <c>DbContext</c> inside it
/// would be a captive dependency that lives for ever.
/// </remarks>
/// <typeparam name="TContext">This service's context.</typeparam>
/// <param name="provider">The container.</param>
internal sealed class KeyRingStore<TContext>(IServiceProvider provider) : IXmlRepository
    where TContext : DbContext
{
    private const string Table = "dataprotection_keys";

    private readonly Lock _lock = new();
    private bool _created;

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elements = new List<XElement>();

        WithConnection(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT content FROM {Table}";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                elements.Add(XElement.Parse(reader.GetString(0)));
            }
        });

        return elements;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One row per key and not one document: two instances creating one at the
    /// same moment would otherwise write the same document over each other and
    /// one would lose its key — which shows up much later as "the token
    /// verifies here but not there".
    /// </remarks>
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        var name = string.IsNullOrWhiteSpace(friendlyName)
            ? Guid.NewGuid().ToString()
            : friendlyName;

        WithConnection(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {Table} (name, content) VALUES (@name, @content) "
                + "ON CONFLICT (name) DO UPDATE SET content = EXCLUDED.content";

            Bind(command, "@name", name);
            Bind(command, "@content", element.ToString(SaveOptions.DisableFormatting));
            command.ExecuteNonQuery();
        });
    }

    private void WithConnection(Action<DbConnection> work)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        EnsureTable(connection);
        work(connection);
    }

    /// <summary>Creates the table, if it does not exist yet.</summary>
    private void EnsureTable(DbConnection connection)
    {
        lock (_lock)
        {
            if (_created)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TABLE IF NOT EXISTS {Table} ("
                + "name text PRIMARY KEY, "
                + "content text NOT NULL, "
                + "created_at timestamptz NOT NULL DEFAULT now())";

            command.ExecuteNonQuery();
            _created = true;
        }
    }

    private static void Bind(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>The key this service's ring lies under, and nothing else does.</summary>
/// <remarks>
/// <para><strong>Not the master key, and one per service.</strong> Fourteen
/// services needed the master key for this alone, and a service taken over could
/// open every person's AI key with it (ADR-0058). No fallback onto it either: it
/// would stay in every service for the sake of entries nobody needs.</para>
///
/// <para>A changed key costs what is protected with the ring, today a sign-in
/// with a foreign provider that is under way. It costs no stored data.</para>
/// </remarks>
public sealed class KeyRingSecret
{
    /// <summary>The environment variable the key stands in.</summary>
    public const string Variable = "WORKERTRANSFER_KEYRING_KEY";

    private readonly SealedText _sealed;

    /// <summary>Reads the key out of the configuration.</summary>
    /// <param name="configuration">The service's configuration.</param>
    /// <exception cref="MasterKeyMissing">It is not set.</exception>
    public KeyRingSecret(IConfiguration configuration) =>
        _sealed = new SealedText(configuration, Variable);

    /// <summary>Encrypts a key of the ring.</summary>
    /// <param name="plainText">The key.</param>
    /// <returns>What is stored.</returns>
    public string Encrypt(string plainText) => _sealed.Encrypt(plainText);

    /// <summary>Decrypts a key of the ring.</summary>
    /// <param name="stored">What was stored.</param>
    /// <returns>The key, or <c>null</c> if it does not open.</returns>
    public string? Decrypt(string? stored) => _sealed.Decrypt(stored);
}

/// <summary>Encrypts the key ring under its own key.</summary>
/// <param name="store">This service's ring key.</param>
internal sealed class MasterKeyEncryptor(KeyRingSecret store) : IXmlEncryptor
{
    /// <summary>The element that wraps an encrypted key.</summary>
    internal const string Envelope = "wtProtectedKey";

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var envelope = new XElement(
            Envelope,
            store.Encrypt(plaintextElement.ToString(SaveOptions.DisableFormatting)));

        return new EncryptedXmlInfo(envelope, typeof(MasterKeyEnvelope));
    }
}

/// <summary>Opens a key of the ring again.</summary>
/// <remarks>
/// <para><strong>The framework builds this type itself, from the name stored in
/// the key</strong>, and only through a constructor taking an
/// <c>IServiceProvider</c> or nothing. A registration as <c>IXmlDecryptor</c> is
/// never asked. With a typed constructor every start logged
/// <c>MissingMethodException</c>, discarded the stored keys and made new ones —
/// the ring lay in the database and was never read back.</para>
///
/// <para>The name stays <c>MasterKeyEnvelope</c> because the keys already
/// stored name it; renaming it would orphan them.</para>
/// </remarks>
/// <param name="services">The container the framework hands over.</param>
internal sealed class MasterKeyEnvelope(IServiceProvider services) : IXmlDecryptor
{
    /// <inheritdoc />
    /// <remarks>
    /// This throw does not stop a start: the framework logs the entry as an
    /// error, skips it and makes a new key (measured, ADR-0058). What it leaves
    /// is one error per unreadable entry at every start.
    /// </remarks>
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var plainText = services.GetRequiredService<KeyRingSecret>().Decrypt(encryptedElement.Value)
            ?? throw new InvalidOperationException(
                "A key of the ring cannot be decrypted. That happens when "
                + $"{KeyRingSecret.Variable} was changed.");

        return XElement.Parse(plainText);
    }
}
