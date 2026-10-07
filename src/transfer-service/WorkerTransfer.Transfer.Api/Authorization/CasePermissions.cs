namespace WorkerTransfer.Transfer.Api.Authorization;

/// <summary>The permissions this service knows.</summary>
/// <remarks>
/// <para><strong>The line lies at the money, not at the conversation.</strong>
/// Showing interest, asking somebody for their market status, reading one's own
/// cases — that is approach work and the work somebody is invited for. An
/// <see cref="Offer">offer</see> names a start date and a placement fee, and
/// <see cref="Complete">completing</see> states that both hold. Both bind the
/// company.</para>
///
/// <para><strong><c>withdraw</c> deliberately does NOT stand here.</strong>
/// Whoever may begin a case has to be allowed to end it — otherwise an
/// invitation is a trap: one gets in and not out again. <c>/decline</c> carries
/// the same sentence for the person already.</para>
/// </remarks>
public static class CasePermissions
{
    /// <summary>Making an offer — date and fee.</summary>
    public const string Offer = "transfer.offer";

    /// <summary>Completing a case.</summary>
    public const string Complete = "transfer.complete";
}

/// <summary>What a first contact on the market needs (ADR-0048).</summary>
public static class MarketPermissions
{
    /// <summary>Asking a person for their market status — at least a <c>recruiter</c>.</summary>
    public const string Request = "market.request";
}
