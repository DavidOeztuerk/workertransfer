using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;

namespace WorkerTransfer.Resume.Infrastructure.Security;

/// <summary>Reads the correlation id Noelia's middleware put on the request.</summary>
public sealed class HttpCorrelation(IHttpContextAccessor accessor) : ICorrelation, IAuditEntryFactory
{
    /// <inheritdoc />
    public string? Current =>
        accessor.HttpContext?.Items.TryGetValue("CorrelationId", out var value) == true
            ? value?.ToString()
            : null;

    /// <inheritdoc />
    public AuditEntry Create(
        AuditAction action,
        DateTimeOffset occurredAt,
        SubjectId? actor = null,
        TenantId? company = null,
        SubjectId? target = null) =>
        new(action, occurredAt, actor, company, target, Current);
}
