namespace WorkerTransfer.Resume.Api.Authorization;

/// <summary>What a company needs for a first contact here (ADR-0048).</summary>
public static class ResumePermissions
{
    /// <summary>Asking a person for their CV — at least a <c>recruiter</c>.</summary>
    public const string Request = "resume.request";
}
