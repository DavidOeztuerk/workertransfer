using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Application.Requests;

/// <summary>The company's own requests. Without a "does it still hold" field.</summary>
/// <remarks>
/// The company already has the answer in the form of the data it does or does
/// not get; a field here would be a second statement about the same fact — and
/// one that could be polled without ever reading a résumé.
/// </remarks>
public sealed record CompanyRequestsQuery(TenantId Company)
    : IQueryMessage<IReadOnlyList<ResumeRequest>>;

/// <inheritdoc cref="CompanyRequestsQuery" />
public sealed class CompanyRequestsHandler(IRequestStore store)
    : IRequestHandler<CompanyRequestsQuery, IReadOnlyList<ResumeRequest>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ResumeRequest>> Handle(
        CompanyRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Company, cancellationToken);
    }
}
