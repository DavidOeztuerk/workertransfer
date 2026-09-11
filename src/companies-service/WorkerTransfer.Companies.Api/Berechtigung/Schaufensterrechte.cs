namespace WorkerTransfer.Companies.Api.Berechtigung;

/// <summary>Die Rechte, die dieser Dienst kennt.</summary>
/// <remarks>
/// <para>Das Arbeitgeberprofil IST das Unternehmen nach aussen: Name,
/// Beschreibung, Anschrift, das Kürzel, unter dem die Karriereseite
/// öffentlich steht. Wer es schreibt, spricht für alle anderen mit — und zwar
/// an einer Adresse, die ohne Anmeldung erreichbar ist.</para>
///
/// <para><em>Lesen</em> steht bewusst nicht hier: das eigene Schaufenster darf
/// jedes Mitglied sehen, es steht ohnehin im Netz.</para>
/// </remarks>
public static class Schaufensterrechte
{
    /// <summary>Das eigene Arbeitgeberprofil schreiben.</summary>
    public const string Schreiben = "company.profile.write";
}
