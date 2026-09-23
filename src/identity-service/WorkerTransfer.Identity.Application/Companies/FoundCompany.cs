using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Registration;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Create a company for the signed-in person.</summary>
/// <remarks>
/// The domain is <em>not</em> in the body. It is derived from the creator's
/// confirmed address; the client names only the name. What it cannot send, it
/// cannot forge (ADR-0017/0019).
/// <para>
/// This route stays open even though registering can carry the intention. Who
/// signs up privately and founds a company later has no other way in — an
/// invitation presupposes colleagues who do not exist yet.
/// </para>
/// </remarks>
public sealed record FoundCompanyCommand(SubjectId Subject, string Name)
    : ICommandMessage<CompanyResult>;

/// <inheritdoc cref="FoundCompanyCommand" />
public sealed class FoundCompanyHandler(
    IUserRepository users,
    CreateCompany companies)
    : IRequestHandler<FoundCompanyCommand, CompanyResult>
{
    /// <inheritdoc />
    public async Task<CompanyResult> Handle(
        FoundCompanyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);

        return account is null
            ? new CompanyResult.Refused("account_not_confirmed")
            : await companies.ExecuteOneAsync(account, request.Name, cancellationToken);
    }
}
