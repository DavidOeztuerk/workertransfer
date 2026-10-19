namespace WorkerTransfer.Advisor.Api.Authorization;

/// <summary>What a company needs for a first contact here (ADR-0048).</summary>
public static class AdvisorPermissions
{
    /// <summary>Opening a conversation with a person — at least a <c>recruiter</c>.</summary>
    public const string Open = "advisor.open";
}
