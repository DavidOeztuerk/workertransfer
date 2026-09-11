namespace WorkerTransfer.Jobs.Api.Berechtigung;

/// <summary>Die Rechte, die dieser Dienst kennt.</summary>
/// <remarks>
/// <para><strong>Die Linie liegt am Aushang, nicht am Text.</strong> Eine
/// Anzeige zu <em>schreiben</em> und zu ändern ist die Arbeit, für die jemand
/// eingeladen wird — solange sie ein Entwurf ist, steht sie niemandem
/// gegenüber. <em>Veröffentlichen</em> und <em>Schliessen</em> sind die beiden
/// Momente, in denen die Anzeige das Unternehmen nach aussen vertritt: das eine
/// stellt sie hin, das andere nimmt sie weg — und mit ihr die Möglichkeit, sich
/// zu bewerben.</para>
///
/// <para>„Ein <c>member</c> kann meine Stellen nicht löschen" heisst in dieser
/// Domäne genau <see cref="Schliessen"/>: gelöscht wird eine Anzeige nie, sie
/// wird geschlossen.</para>
///
/// <para>Die Namen sagen, <em>wozu</em> ein Endpunkt berechtigt sein will, und
/// nicht, <em>wer</em> das ist — wer es ist, entscheidet
/// <c>ServiceDefaults.Rollen.Adminrecht</c> aus der Mitgliedschaftstabelle von
/// identity-service.</para>
/// </remarks>
public static class Stellenrechte
{
    /// <summary>Eine Anzeige veröffentlichen.</summary>
    public const string Veroeffentlichen = "jobs.publish";

    /// <summary>Eine Anzeige schliessen.</summary>
    public const string Schliessen = "jobs.close";
}
