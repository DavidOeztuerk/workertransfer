using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Requests;

/// <summary>Where a market request stands.</summary>
public enum RequestState
{
    /// <summary>Asked, unanswered.</summary>
    Pending,

    /// <summary>Was granted once.</summary>
    Granted,

    /// <summary>Refused.</summary>
    Declined
}

/// <summary>The words a request state travels under on the wire.</summary>
public static class RequestStates
{
    /// <summary>The word for a state. Upper case, as in the ledger.</summary>
    public static string Word(RequestState state) => state switch
    {
        RequestState.Pending => "PENDING",
        RequestState.Granted => "GRANTED",
        RequestState.Declined => "DECLINED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The state for a word, or <c>null</c>.</summary>
    public static RequestState? Read(string? word) => word switch
    {
        "PENDING" => RequestState.Pending,
        "GRANTED" => RequestState.Granted,
        "DECLINED" => RequestState.Declined,
        _ => null
    };
}

/// <summary>Only the person asked may answer.</summary>
public sealed class NotTheOneAsked() : Exception("Only the person asked may answer");

/// <summary>This request is already answered.</summary>
public sealed class AlreadyAnswered(RequestState state)
    : Exception($"This request is already {RequestStates.Word(state)}");

/// <summary>A company's request for a market status.</summary>
/// <remarks>
/// The door to the transfer market. Without it
/// <c>market.visibility:tenant:&lt;id&gt;</c> comes into being nowhere, and a
/// case could never begin.
/// <para>
/// <strong>A case, not a permission</strong> — the same separation as with the
/// CV: <c>GRANTED</c> means "was granted once", not "holds now". Whether the
/// access exists now is answered exclusively by the consent ledger, freshly at
/// every access (ADR-0013). So there is neither an <c>Active</c> nor a
/// <c>RevokedAt</c> here. After a revocation the request stays <c>GRANTED</c>
/// and the access still comes up empty — no contradiction but the separation
/// between what has happened and what holds.
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
        DateTimeOffset createdAt,
        DateTimeOffset? beantwortetAm)
    {
        Id = id;
        Subject = subject;
        Company = company;
        Frager = frager;
        State = state;
        CreatedAt = createdAt;
        BeantwortetAm = beantwortetAm;
    }

    /// <summary>Which request.</summary>
    public Guid Id { get; }

    /// <summary>About whom.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which company is asking.</summary>
    public TenantId Company { get; }

    /// <summary>Who at the company asked.</summary>
    /// <remarks>
    /// The company carries the permission, the person the trace — without this
    /// field the log would say only "somebody at Acme". <c>null</c> after the
    /// erasure of the account that asked (ADR-0027 §2): the case stays with the
    /// company, the person's name falls away. It does <strong>not</strong> mean
    /// "nobody asked".
    /// </remarks>
    public SubjectId? Frager { get; }

    /// <summary>Where it stands.</summary>
    public RequestState State { get; private set; }

    /// <summary>When it was asked.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary><c>null</c> for as long as it is unanswered.</summary>
    public DateTimeOffset? BeantwortetAm { get; private set; }

    /// <summary>A company asks.</summary>
    public static MarketRequest Oeffne(
        SubjectId subject, TenantId company, SubjectId frager, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, frager, RequestState.Pending, now, null);

    /// <summary>The request as a row holds it.</summary>
    public static MarketRequest Restore(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? frager,
        RequestState state,
        DateTimeOffset createdAt,
        DateTimeOffset? beantwortetAm) =>
        new(id, subject, company, frager, state, createdAt, beantwortetAm);

    /// <summary>The person releases.</summary>
    /// <exception cref="NotTheOneAsked">Somebody else tried to answer.</exception>
    /// <exception cref="AlreadyAnswered">It is already answered.</exception>
    public void Erteile(SubjectId durch, DateTimeOffset now)
    {
        Beantwortbar(durch);
        State = RequestState.Granted;
        BeantwortetAm = now;
    }

    /// <summary>The person refuses.</summary>
    /// <exception cref="NotTheOneAsked">Somebody else tried to answer.</exception>
    /// <exception cref="AlreadyAnswered">It is already answered.</exception>
    public void Lehne_ab(SubjectId durch, DateTimeOffset now)
    {
        Beantwortbar(durch);
        State = RequestState.Declined;
        BeantwortetAm = now;
    }

    private void Beantwortbar(SubjectId principal)
    {
        if (principal != Subject)
        {
            throw new NotTheOneAsked();
        }

        if (State is not RequestState.Pending)
        {
            // A second "grant" after a "refuse" would silently reverse the
            // refusal. And a revocation belongs in the ledger, not in this case.
            throw new AlreadyAnswered(State);
        }
    }
}

/// <summary>Finds and stores market requests.</summary>
public interface IRequestStore
{
    /// <summary>One request, or <c>null</c>.</summary>
    Task<MarketRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This company's request to this person, if there is one.</summary>
    Task<MarketRequest?> GetAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(MarketRequest request, CancellationToken cancellationToken = default);

    /// <summary>The requests about this person.</summary>
    Task<IReadOnlyList<MarketRequest>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>This company's requests.</summary>
    Task<IReadOnlyList<MarketRequest>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);
}
