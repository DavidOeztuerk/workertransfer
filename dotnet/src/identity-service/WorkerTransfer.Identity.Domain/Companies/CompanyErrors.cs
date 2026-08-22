namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>A mass provider cannot be claimed as a company.</summary>
public sealed class PublicEmailDomainException(string domain)
    : Exception($"'{domain}' is a public email provider and cannot be claimed as a company")
{
    /// <summary>The domain that was refused.</summary>
    public string Domain { get; } = domain;
}

/// <summary>The domain already belongs to a company.</summary>
/// <remarks>
/// Only ever raised at confirmation, never at registration. Asked earlier it
/// would answer "is firma.de on this platform?" to anyone who guesses a domain.
/// </remarks>
public sealed class DomainAlreadyClaimedException(string domain)
    : Exception($"'{domain}' already belongs to a company")
{
    /// <summary>The domain that was already taken.</summary>
    public string Domain { get; } = domain;
}

/// <summary>The address is not confirmed, so it proves no domain.</summary>
public sealed class AccountNotConfirmedException()
    : Exception("Confirm your email address before creating a company");

/// <summary>A company needs a name.</summary>
public sealed class InvalidCompanyNameException()
    : Exception("A company name must not be empty");
