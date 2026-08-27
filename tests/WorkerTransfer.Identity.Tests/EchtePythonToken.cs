namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Tokens the Python identity-service really issued, kept verbatim.
/// </summary>
/// <remarks>
/// Produced by <c>worker_auth.TokenManager.create_access_token</c> with the
/// secret below and a lifetime running to 2106, so they stay usable as
/// fixtures. A rebuilt token would prove less: the point is the exact wire
/// form, including <c>"tenant_id": null</c> for a person.
/// <para>
/// They leave with Ü-2 in <c>docs/uebergang-python-dotnet.md</c>.
/// </para>
/// </remarks>
internal static class EchtePythonToken
{
    internal const string Secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>Subject of both tokens.</summary>
    internal static readonly Guid Anna = new("00000000-0000-0000-0000-000000000001");

    /// <summary>The company <see cref="Firma"/> acts for.</summary>
    internal static readonly Guid Firma = new("00000000-0000-0000-0000-000000000002");

    /// <summary>Acting as a person — carries <c>"tenant_id": null</c>.</summary>
    internal const string AlsPerson =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJzdWIiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJ0ZW5hbnRfaWQiOm51bGws" +
        "InJvbGVzIjpbInVzZXIiXSwicGVybWlzc2lvbnMiOltdLCJleHAiOjQzMTAxMTQ5MzIsImlhdCI6MTc4NzIz" +
        "NDkzMiwidHlwZSI6ImFjY2VzcyIsImp0aSI6ImJjOTc5MjhlLTJjMDctNDEwYy04MDI5LTQ4MzM3ODI4MTg2" +
        "OCJ9.l7UUN-DcYfSA9VU6nCG8j0BmIjNq7_kYi_A22enF8sQ";

    /// <summary>Acting for a company — carries <c>tenant_id</c>, never <c>tenant</c>.</summary>
    internal const string FuerDieFirma =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJzdWIiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJ0ZW5hbnRfaWQiOiIwMDAw" +
        "MDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDIiLCJyb2xlcyI6WyJ1c2VyIl0sInBlcm1pc3Npb25z" +
        "IjpbXSwiZXhwIjo0MzEwMTE0OTMyLCJpYXQiOjE3ODcyMzQ5MzIsInR5cGUiOiJhY2Nlc3MiLCJqdGkiOiIx" +
        "MWQ5ZmRlYS1kNTJmLTQzNjgtODczYi0zZTkzYTI3MTRlMGIifQ.SkV0LkXljX4C0L0SthOjLCaufz9AeRRvDDEPaYosg0g";
}
