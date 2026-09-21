using System.Data.Common;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
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
    /// <c>LoeschempfaengerTests</c>.</para>
    /// </remarks>
    /// <typeparam name="TKontext">The context whose database holds the ring.</typeparam>
    /// <param name="services">The container.</param>
    /// <returns>The container.</returns>
    public static IServiceCollection AddKeyRing<TKontext>(this IServiceCollection services)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd, because identity-service already registers it for the AI
        // access.
        services.TryAddSingleton<SecretStore>();

        services.AddSingleton<IXmlRepository, KeyRingStore<TKontext>>();
        services.AddSingleton<IXmlEncryptor, MasterKeyEnvelope>();
        services.AddSingleton<IXmlDecryptor, MasterKeyEnvelope>();
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
/// <typeparam name="TKontext">This service's context.</typeparam>
/// <param name="provider">The container.</param>
internal sealed class KeyRingStore<TKontext>(IServiceProvider provider) : IXmlRepository
    where TKontext : DbContext
{
    private const string Table = "dataprotection_schluessel";

    private readonly Lock _riegel = new();
    private bool _angelegt;

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elemente = new List<XElement>();

        WithConnection(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT inhalt FROM {Table}";

            using var leser = command.ExecuteReader();

            while (leser.Read())
            {
                elemente.Add(XElement.Parse(leser.GetString(0)));
            }
        });

        return elemente;
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
                $"INSERT INTO {Table} (name, inhalt) VALUES (@name, @inhalt) "
                + "ON CONFLICT (name) DO UPDATE SET inhalt = EXCLUDED.inhalt";

            Bind(command, "@name", name);
            Bind(command, "@inhalt", element.ToString(SaveOptions.DisableFormatting));
            command.ExecuteNonQuery();
        });
    }

    private void WithConnection(Action<DbConnection> work)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TKontext>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        Job(connection);
        work(connection);
    }

    /// <summary>Creates the table, if it does not exist yet.</summary>
    private void Job(DbConnection connection)
    {
        lock (_riegel)
        {
            if (_angelegt)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TABLE IF NOT EXISTS {Table} ("
                + "name text PRIMARY KEY, "
                + "inhalt text NOT NULL, "
                + "angelegt timestamptz NOT NULL DEFAULT now())";

            command.ExecuteNonQuery();
            _angelegt = true;
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

/// <summary>Encrypts the key ring under the operator's master key.</summary>
/// <remarks>
/// The same <see cref="SecretStore"/> as for a person's AI access: AES-GCM, one
/// key from the environment. A second encryption beside it would be a second
/// place where somebody would have to look for the master key.
/// </remarks>
/// <param name="store">This installation's master key.</param>
internal sealed class MasterKeyEnvelope(SecretStore store)
    : IXmlEncryptor, IXmlDecryptor
{
    private const string Envelope = "wtGeschuetzterSchluessel";

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var envelope = new XElement(
            Envelope,
            store.Encrypt(plaintextElement.ToString(SaveOptions.DisableFormatting)));

        return new EncryptedXmlInfo(envelope, typeof(MasterKeyEnvelope));
    }

    /// <inheritdoc />
    /// <remarks>
    /// An unreadable ring throws here and is not passed over in silence: a
    /// master key that was changed should be noticed at startup and not as "all
    /// tokens are suddenly invalid" at the first human being.
    /// </remarks>
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var plainText = store.Decrypt(encryptedElement.Value)
            ?? throw new InvalidOperationException(
                "Ein Schlüssel des Bundes lässt sich nicht entschlüsseln. Das "
                + $"passiert, wenn {SecretStore.Variable} gewechselt wurde.");

        return XElement.Parse(plainText);
    }
}
