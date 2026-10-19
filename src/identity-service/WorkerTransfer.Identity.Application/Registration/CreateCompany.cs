using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Registration;

/// <summary>What creating a company produced.</summary>
public abstract record CompanyResult
{
    private CompanyResult() { }

    /// <summary>Created, and the creator is its administrator.</summary>
    public sealed record Created(Company Company) : CompanyResult;

    /// <summary>Refused, with a code the interface can act on.</summary>
    /// <param name="Code">
    /// <c>public_email_domain</c>, <c>domain_already_claimed</c>,
    /// <c>account_not_confirmed</c> or <c>invalid_company_name</c>.
    /// </param>
    public sealed record Refused(string Code) : CompanyResult;
}

/// <summary>Creates a company whose domain is already proven.</summary>
/// <remarks>
/// Not a mediator command, because it is never the whole of a request: it runs
/// inside confirming an address, in that transaction, and a nested dispatch
/// would put a second logging and validation pass around half a request.
/// <para>
/// The domain comes from the creator's confirmed address, so it is not in the
/// request and cannot be forged (ADR-0017/0019). Because it is proven before
/// the company exists, there is no unverified company state that every later
/// read path would have to check.
/// </para>
/// </remarks>
public sealed class CreateCompany(
    ICompanyRepository companies,
    IMembershipRepository memberships,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
{
    /// <param name="account">
    /// The account creating it, as the caller holds it.
    /// </param>
    /// <param name="name">What it is to be called.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <remarks>
    /// Takes the aggregate rather than an id, and that is not convenience.
    /// Confirming an address activates the account and creates the company in
    /// one transaction; reading the account again here would read the
    /// <em>row</em>, and the activation is still in the change tracker — so the
    /// company would be refused for an account that was just confirmed.
    /// </remarks>
    public async Task<CompanyResult> ExecuteOneAsync(
        User account,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Status != AccountStatus.Active)
        {
            // An unconfirmed address proves no domain.
            return new CompanyResult.Refused("account_not_confirmed");
        }

        var domain = EmailDomain.FromEmail(account.Email);

        if (await companies.FindByDomainAsync(domain, cancellationToken) is not null)
        {
            return new CompanyResult.Refused("domain_already_claimed");
        }

        Company company;

        try
        {
            company = Company.Create(name, domain);
        }
        catch (PublicEmailDomainException)
        {
            return new CompanyResult.Refused("public_email_domain");
        }
        catch (InvalidCompanyNameException)
        {
            return new CompanyResult.Refused("invalid_company_name");
        }

        await companies.AddAsync(company, cancellationToken);
        await memberships.AddAsync(
            account.Id, company.Id, MembershipRole.Admin, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.CompanyCreated,
                clock.GetUtcNow(),
                actor: account.Id,
                // Unlike a personal act, this entry carries a tenant: it is
                // about a company (ADR-0017).
                tenant: company.Id,
                correlationId: correlation.Current),
            cancellationToken);

        return new CompanyResult.Created(company);
    }
}
