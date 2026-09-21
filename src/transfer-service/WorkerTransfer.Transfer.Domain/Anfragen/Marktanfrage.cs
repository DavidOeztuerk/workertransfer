using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Anfragen;

/// <summary>Wo eine ResumeRequest steht.</summary>
public enum RequestState
{
    /// <summary>Gestellt, unbeantwortet.</summary>
    Pending,

    /// <summary>Wurde einmal erteilt.</summary>
    Granted,

    /// <summary>Abgelehnt.</summary>
    Declined
}

/// <summary>Die Worte, mit denen ein RequestState auf der Leitung steht.</summary>
public static class RequestStates
{
    /// <summary>Das Wort zum State. Großgeschrieben, wie im Ledger.</summary>
    public static string Wort(RequestState state) => state switch
    {
        RequestState.Pending => "PENDING",
        RequestState.Granted => "GRANTED",
        RequestState.Declined => "DECLINED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>Der State zum Wort, oder <c>null</c>.</summary>
    public static RequestState? Lies(string? word) => word switch
    {
        "PENDING" => RequestState.Pending,
        "GRANTED" => RequestState.Granted,
        "DECLINED" => RequestState.Declined,
        _ => null
    };
}

/// <summary>Nur die gefragte Person darf antworten.</summary>
public sealed class NichtDieGefragte() : Exception("Only the person asked may answer");

/// <summary>Diese ResumeRequest ist schon beantwortet.</summary>
public sealed class AlreadyAnswered(RequestState state)
    : Exception($"This request is already {RequestStates.Wort(state)}");

/// <summary>Die ResumeRequest eines Unternehmens nach einem MarketStatus.</summary>
/// <remarks>
/// Die Tür zum TransferMarket. Ohne sie entsteht
/// <c>market.visibility:tenant:&lt;id&gt;</c> nirgends, und ein Case könnte
/// nie beginnen.
/// <para>
/// <strong>Ein Case, keine Berechtigung</strong> — dieselbe Trennung wie
/// beim Resume: <c>GRANTED</c> heißt „wurde einmal erteilt", nicht „gilt
/// gerade". Ob der Zugriff jetzt besteht, beantwortet ausschließlich der
/// Consent-Ledger, frisch bei jedem Zugriff (ADR-0013). Deshalb gibt es hier
/// weder ein <c>Aktiv</c> noch ein <c>WiderrufenAm</c>. Nach einem Revocation
/// bleibt die ResumeRequest <c>GRANTED</c> und der Zugriff läuft trotzdem ins Leere —
/// kein Widerspruch, sondern die Trennung zwischen dem, was geschehen ist, und
/// dem, was gilt.
/// </para>
/// </remarks>
public sealed class MarketRequest
{
    private MarketRequest(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? frager,
        RequestState state,
        DateTimeOffset angelegtAm,
        DateTimeOffset? beantwortetAm)
    {
        Id = id;
        Wer = subject;
        Firma = company;
        Frager = frager;
        State = state;
        AngelegtAm = angelegtAm;
        BeantwortetAm = beantwortetAm;
    }

    /// <summary>Welche ResumeRequest.</summary>
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
    /// der Case bleibt dem Unternehmen, der Name der Person fällt weg. Es
    /// heißt <strong>nicht</strong> „niemand hat gefragt".
    /// </remarks>
    public SubjectId? Frager { get; }

    /// <summary>Wo sie steht.</summary>
    public RequestState State { get; private set; }

    /// <summary>Wann gefragt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary><c>null</c>, solange unbeantwortet.</summary>
    public DateTimeOffset? BeantwortetAm { get; private set; }

    /// <summary>Ein Unternehmen fragt.</summary>
    public static MarketRequest Oeffne(
        SubjectId subject, TenantId company, SubjectId frager, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, frager, RequestState.Pending, now, null);

    /// <summary>Die ResumeRequest, wie eine Zeile sie hält.</summary>
    public static MarketRequest Stelle_her(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? frager,
        RequestState state,
        DateTimeOffset angelegtAm,
        DateTimeOffset? beantwortetAm) =>
        new(id, subject, company, frager, state, angelegtAm, beantwortetAm);

    /// <summary>Die Person gibt frei.</summary>
    /// <exception cref="NichtDieGefragte">Ein anderer wollte antworten.</exception>
    /// <exception cref="SchonBeantwortet">Sie ist bereits beantwortet.</exception>
    public void Erteile(SubjectId durch, DateTimeOffset now)
    {
        Beantwortbar(durch);
        State = RequestState.Granted;
        BeantwortetAm = now;
    }

    /// <summary>Die Person lehnt ab.</summary>
    /// <exception cref="NichtDieGefragte">Ein anderer wollte antworten.</exception>
    /// <exception cref="SchonBeantwortet">Sie ist bereits beantwortet.</exception>
    public void Lehne_ab(SubjectId durch, DateTimeOffset now)
    {
        Beantwortbar(durch);
        State = RequestState.Declined;
        BeantwortetAm = now;
    }

    private void Beantwortbar(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDieGefragte();
        }

        if (State is not RequestState.Pending)
        {
            // Ein zweites „erteile" nach einem „lehne ab" würde die Ablehnung
            // stillschweigend umdrehen. Und ein Revocation gehört in den Ledger,
            // nicht in diesen Case.
            throw new AlreadyAnswered(State);
        }
    }
}

/// <summary>Findet und speichert Marktanfragen.</summary>
public interface IRequestStore
{
    /// <summary>Eine ResumeRequest, oder <c>null</c>.</summary>
    Task<MarketRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die ResumeRequest dieses Unternehmens an diese Person, falls es sie gibt.</summary>
    Task<MarketRequest?> GetAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(MarketRequest request, CancellationToken cancellationToken = default);

    /// <summary>Die Anfragen über diese Person.</summary>
    Task<IReadOnlyList<MarketRequest>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Die Anfragen dieses Unternehmens.</summary>
    Task<IReadOnlyList<MarketRequest>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);
}
