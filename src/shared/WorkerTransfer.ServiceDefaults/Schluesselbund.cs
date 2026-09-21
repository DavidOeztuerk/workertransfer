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

/// <summary>Wo der Schlüsselbund des Gerüsts liegt und womit er geschützt ist.</summary>
public static class Schluesselbund
{
    /// <summary>Trennt den Bund dieser Plattform von fremden auf demselben Speicher.</summary>
    /// <remarks>
    /// Ein Name für alle fünfzehn und nicht je Dienst einer: aus ihm leitet das
    /// Gerüst die Verwendungszwecke ab, und zwei Dienste, die einmal dasselbe
    /// prüfen sollen, könnten das mit verschiedenen Namen nie.
    /// </remarks>
    public const string Anwendungsname = "workertransfer";

    /// <summary>Legt den Schlüsselbund in die Datenbank und verschlüsselt ihn.</summary>
    /// <remarks>
    /// <para><strong>Ohne diesen Aufruf schreibt ASP.NET den Bund in das
    /// Dateisystem des Behälters</strong> und warnt zweimal beim Start. Was
    /// damit geschützt ist — ein Anmeldecookie, ein Fälschungsschutz-Token, ein
    /// Rücksetzlink — prüft dann nicht mehr, sobald der Behälter ersetzt wird,
    /// und eine zweite Instanz prüft gar nicht erst, was die erste ausgestellt
    /// hat.</para>
    ///
    /// <para><strong>Die Datenbank und kein Zwischenspeicher im Prozess.</strong>
    /// Noelias <c>UseDataProtection</c> legt den Bund in den registrierten
    /// Cache-Anbieter; der einzige ohne Redis wäre <c>Noelia.InMemory</c>, und
    /// der stirbt mit dem Prozess. Das machte die Prüfung grün, ohne etwas zu
    /// ändern — zwei Instanzen läsen weiterhin verschiedene Bünde.</para>
    ///
    /// <para><strong>Über den Kontext und nicht über Npgsql.</strong> Dieses
    /// Paket hält bewusst keine Treiberabhängigkeit; die Anweisungen unten sind
    /// allerdings PostgreSQL-Syntax, und ein Dienst auf einer anderen Datenbank
    /// müsste sie ersetzen. Die Tabelle entsteht dabei hier und nicht in einer
    /// Wanderung: sie gehört keinem Fachmodell, und ein <c>DbSet</c> dafür
    /// ließe sie in <c>LoeschempfaengerTests</c> wie eine Personenzeile
    /// aussehen.</para>
    /// </remarks>
    /// <typeparam name="TKontext">Der Kontext, dessen Datenbank den Bund hält.</typeparam>
    /// <param name="services">Der Container.</param>
    /// <returns>Der Container.</returns>
    public static IServiceCollection AddSchluesselbund<TKontext>(this IServiceCollection services)
        where TKontext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd, weil identity-service ihn für den KI-Zugang schon registriert.
        services.TryAddSingleton<Geheimnisspeicher>();

        services.AddSingleton<IXmlRepository, Schluesselablage<TKontext>>();
        services.AddSingleton<IXmlEncryptor, Hauptschluesselhuelle>();
        services.AddSingleton<IXmlDecryptor, Hauptschluesselhuelle>();
        services.AddDataProtection().SetApplicationName(Anwendungsname);

        // Aus dem Container und nicht über den Baumeister: dessen
        // PersistKeysTo*/ProtectKeysWith*-Überladungen nehmen Instanzen, und
        // diese beiden brauchen Dienste, die erst nach diesem Aufruf stehen.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(
            anbieter => new Schluesselstellung(anbieter));

        return services;
    }

    private sealed class Schluesselstellung(IServiceProvider anbieter)
        : IConfigureOptions<KeyManagementOptions>
    {
        public void Configure(KeyManagementOptions optionen)
        {
            optionen.XmlRepository = anbieter.GetRequiredService<IXmlRepository>();
            optionen.XmlEncryptor = anbieter.GetRequiredService<IXmlEncryptor>();
        }
    }
}

/// <summary>Der Schlüsselbund in der Datenbank dieses Dienstes.</summary>
/// <remarks>
/// Sie nimmt den <c>IServiceProvider</c> und öffnet ihren eigenen Bereich: das
/// Gerüst löst den Bund als Singleton auf, und ein <c>DbContext</c> darin wäre
/// eine gefangene Abhängigkeit, die ewig lebt.
/// </remarks>
/// <typeparam name="TKontext">Der Kontext dieses Dienstes.</typeparam>
/// <param name="anbieter">Der Container.</param>
internal sealed class Schluesselablage<TKontext>(IServiceProvider anbieter) : IXmlRepository
    where TKontext : DbContext
{
    private const string Tabelle = "dataprotection_schluessel";

    private readonly Lock _riegel = new();
    private bool _angelegt;

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elemente = new List<XElement>();

        Mit(verbindung =>
        {
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = $"SELECT inhalt FROM {Tabelle}";

            using var leser = befehl.ExecuteReader();

            while (leser.Read())
            {
                elemente.Add(XElement.Parse(leser.GetString(0)));
            }
        });

        return elemente;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Eine Zeile je Schlüssel und kein Dokument: zwei Instanzen, die im selben
    /// Moment einen anlegen, schrieben sonst dasselbe Dokument übereinander und
    /// eine verlöre ihren — was viel später als „Token prüft hier, aber nicht
    /// dort" auffällt.
    /// </remarks>
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        var name = string.IsNullOrWhiteSpace(friendlyName)
            ? Guid.NewGuid().ToString()
            : friendlyName;

        Mit(verbindung =>
        {
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText =
                $"INSERT INTO {Tabelle} (name, inhalt) VALUES (@name, @inhalt) "
                + "ON CONFLICT (name) DO UPDATE SET inhalt = EXCLUDED.inhalt";

            Gib(befehl, "@name", name);
            Gib(befehl, "@inhalt", element.ToString(SaveOptions.DisableFormatting));
            befehl.ExecuteNonQuery();
        });
    }

    private void Mit(Action<DbConnection> was)
    {
        using var bereich = anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<TKontext>();
        var verbindung = kontext.Database.GetDbConnection();

        if (verbindung.State != System.Data.ConnectionState.Open)
        {
            verbindung.Open();
        }

        Stelle(verbindung);
        was(verbindung);
    }

    /// <summary>Legt die Tabelle an, falls es sie noch nicht gibt.</summary>
    private void Stelle(DbConnection verbindung)
    {
        lock (_riegel)
        {
            if (_angelegt)
            {
                return;
            }

            using var befehl = verbindung.CreateCommand();
            befehl.CommandText =
                $"CREATE TABLE IF NOT EXISTS {Tabelle} ("
                + "name text PRIMARY KEY, "
                + "inhalt text NOT NULL, "
                + "angelegt timestamptz NOT NULL DEFAULT now())";

            befehl.ExecuteNonQuery();
            _angelegt = true;
        }
    }

    private static void Gib(DbCommand befehl, string name, string wert)
    {
        var parameter = befehl.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = wert;
        befehl.Parameters.Add(parameter);
    }
}

/// <summary>Verschlüsselt den Schlüsselbund unter dem Hauptschlüssel des Betreibers.</summary>
/// <remarks>
/// Derselbe <see cref="Geheimnisspeicher"/> wie für den KI-Zugang einer Person:
/// AES-GCM, ein Schlüssel aus der Umgebung. Eine zweite Verschlüsselung daneben
/// wäre eine zweite Stelle, an der jemand den Hauptschlüssel suchen müsste.
/// </remarks>
/// <param name="speicher">Der Hauptschlüssel dieses Betriebs.</param>
internal sealed class Hauptschluesselhuelle(Geheimnisspeicher speicher)
    : IXmlEncryptor, IXmlDecryptor
{
    private const string Huelle = "wtGeschuetzterSchluessel";

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var huelle = new XElement(
            Huelle,
            speicher.Verschluessele(plaintextElement.ToString(SaveOptions.DisableFormatting)));

        return new EncryptedXmlInfo(huelle, typeof(Hauptschluesselhuelle));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ein unlesbarer Bund wirft hier und wird nicht still übergangen: ein
    /// gewechselter Hauptschlüssel soll beim Start auffallen und nicht als
    /// „alle Token sind plötzlich ungültig" beim ersten Menschen.
    /// </remarks>
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var klartext = speicher.Entschluessele(encryptedElement.Value)
            ?? throw new InvalidOperationException(
                "Ein Schlüssel des Bundes lässt sich nicht entschlüsseln. Das "
                + $"passiert, wenn {Geheimnisspeicher.Variable} gewechselt wurde.");

        return XElement.Parse(klartext);
    }
}
