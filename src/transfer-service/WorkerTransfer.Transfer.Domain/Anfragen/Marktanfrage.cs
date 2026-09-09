using Girder.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Anfragen;

/// <summary>Wo eine Anfrage steht.</summary>
public enum Anfragestand
{
    /// <summary>Gestellt, unbeantwortet.</summary>
    Pending,

    /// <summary>Wurde einmal erteilt.</summary>
    Granted,

    /// <summary>Abgelehnt.</summary>
    Declined
}

/// <summary>Die Worte, mit denen ein Anfragestand auf der Leitung steht.</summary>
public static class Anfragestaende
{
    /// <summary>Das Wort zum Stand. Großgeschrieben, wie im Ledger.</summary>
    public static string Wort(Anfragestand stand) => stand switch
    {
        Anfragestand.Pending => "PENDING",
        Anfragestand.Granted => "GRANTED",
        Anfragestand.Declined => "DECLINED",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };

    /// <summary>Der Stand zum Wort, oder <c>null</c>.</summary>
    public static Anfragestand? Lies(string? wort) => wort switch
    {
        "PENDING" => Anfragestand.Pending,
        "GRANTED" => Anfragestand.Granted,
        "DECLINED" => Anfragestand.Declined,
        _ => null
    };
}

/// <summary>Nur die gefragte Person darf antworten.</summary>
public sealed class NichtDieGefragte() : Exception("Only the person asked may answer");

/// <summary>Diese Anfrage ist schon beantwortet.</summary>
public sealed class SchonBeantwortet(Anfragestand stand)
    : Exception($"This request is already {Anfragestaende.Wort(stand)}");

/// <summary>Die Anfrage eines Unternehmens nach einem Marktstatus.</summary>
/// <remarks>
/// Die Tür zum Transfermarkt. Ohne sie entsteht
/// <c>market.visibility:tenant:&lt;id&gt;</c> nirgends, und ein Vorgang könnte
/// nie beginnen.
/// <para>
/// <strong>Ein Vorgang, keine Berechtigung</strong> — dieselbe Trennung wie
/// beim Lebenslauf: <c>GRANTED</c> heißt „wurde einmal erteilt", nicht „gilt
/// gerade". Ob der Zugriff jetzt besteht, beantwortet ausschließlich der
/// Consent-Ledger, frisch bei jedem Zugriff (ADR-0013). Deshalb gibt es hier
/// weder ein <c>Aktiv</c> noch ein <c>WiderrufenAm</c>. Nach einem Widerruf
/// bleibt die Anfrage <c>GRANTED</c> und der Zugriff läuft trotzdem ins Leere —
/// kein Widerspruch, sondern die Trennung zwischen dem, was geschehen ist, und
/// dem, was gilt.
/// </para>
/// </remarks>
public sealed class Marktanfrage
{
    private Marktanfrage(
        Guid id,
        SubjectId wer,
        TenantId firma,
        SubjectId? frager,
        Anfragestand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset? beantwortetAm)
    {
        Id = id;
        Wer = wer;
        Firma = firma;
        Frager = frager;
        Stand = stand;
        AngelegtAm = angelegtAm;
        BeantwortetAm = beantwortetAm;
    }

    /// <summary>Welche Anfrage.</summary>
    public Guid Id { get; }

    /// <summary>Über wen.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen fragt.</summary>
    public TenantId Firma { get; }

    /// <summary>Wer im Unternehmen gefragt hat.</summary>
    /// <remarks>
    /// Das Unternehmen trägt die Berechtigung, die Person die Spur — ohne
    /// dieses Feld stünde im Protokoll nur „irgendwer bei Acme".
    /// <c>null</c> nach der Löschung des Kontos, das gefragt hat (ADR-0027 §2):
    /// der Vorgang bleibt dem Unternehmen, der Name der Person fällt weg. Es
    /// heißt <strong>nicht</strong> „niemand hat gefragt".
    /// </remarks>
    public SubjectId? Frager { get; }

    /// <summary>Wo sie steht.</summary>
    public Anfragestand Stand { get; private set; }

    /// <summary>Wann gefragt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary><c>null</c>, solange unbeantwortet.</summary>
    public DateTimeOffset? BeantwortetAm { get; private set; }

    /// <summary>Ein Unternehmen fragt.</summary>
    public static Marktanfrage Oeffne(
        SubjectId wer, TenantId firma, SubjectId frager, DateTimeOffset jetzt) =>
        new(Guid.CreateVersion7(), wer, firma, frager, Anfragestand.Pending, jetzt, null);

    /// <summary>Die Anfrage, wie eine Zeile sie hält.</summary>
    public static Marktanfrage Stelle_her(
        Guid id,
        SubjectId wer,
        TenantId firma,
        SubjectId? frager,
        Anfragestand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset? beantwortetAm) =>
        new(id, wer, firma, frager, stand, angelegtAm, beantwortetAm);

    /// <summary>Die Person gibt frei.</summary>
    /// <exception cref="NichtDieGefragte">Ein anderer wollte antworten.</exception>
    /// <exception cref="SchonBeantwortet">Sie ist bereits beantwortet.</exception>
    public void Erteile(SubjectId durch, DateTimeOffset jetzt)
    {
        Beantwortbar(durch);
        Stand = Anfragestand.Granted;
        BeantwortetAm = jetzt;
    }

    /// <summary>Die Person lehnt ab.</summary>
    /// <exception cref="NichtDieGefragte">Ein anderer wollte antworten.</exception>
    /// <exception cref="SchonBeantwortet">Sie ist bereits beantwortet.</exception>
    public void Lehne_ab(SubjectId durch, DateTimeOffset jetzt)
    {
        Beantwortbar(durch);
        Stand = Anfragestand.Declined;
        BeantwortetAm = jetzt;
    }

    private void Beantwortbar(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDieGefragte();
        }

        if (Stand is not Anfragestand.Pending)
        {
            // Ein zweites „erteile" nach einem „lehne ab" würde die Ablehnung
            // stillschweigend umdrehen. Und ein Widerruf gehört in den Ledger,
            // nicht in diesen Vorgang.
            throw new SchonBeantwortet(Stand);
        }
    }
}

/// <summary>Findet und speichert Marktanfragen.</summary>
public interface IAnfragenspeicher
{
    /// <summary>Eine Anfrage, oder <c>null</c>.</summary>
    Task<Marktanfrage?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die Anfrage dieses Unternehmens an diese Person, falls es sie gibt.</summary>
    Task<Marktanfrage?> HoleAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Marktanfrage anfrage, CancellationToken cancellationToken = default);

    /// <summary>Die Anfragen über diese Person.</summary>
    Task<IReadOnlyList<Marktanfrage>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Die Anfragen dieses Unternehmens.</summary>
    Task<IReadOnlyList<Marktanfrage>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);
}
