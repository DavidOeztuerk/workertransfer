using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Accounts for tests, built the way the repository builds them.</summary>
/// <remarks>
/// Through <see cref="User.Restore"/> and not an object initialiser, because
/// that is the only entrance an existing account has. A test that could set the
/// fields directly could also build a state the aggregate refuses.
/// </remarks>
public static class Konten
{
    /// <summary>An account as it stands in the database.</summary>
    public static User Bestehend(
        SubjectId id,
        string email = "anna@example.com",
        string passwortEintrag = "$2b$12$abcdefghijklmnopqrstuv",
        AccountStatus status = AccountStatus.Active,
        string? offeneFirma = null) =>
        User.Restore(id, email, passwortEintrag, "Anna", status, ["user"], offeneFirma);
}
