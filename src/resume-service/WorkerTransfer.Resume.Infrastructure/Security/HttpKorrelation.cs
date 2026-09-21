using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Infrastructure.Security;

/// <summary>Reads the correlation id Noelia's middleware put on the request.</summary>
public sealed class HttpKorrelation(IHttpContextAccessor zugriff) : IKorrelation, IKorrelationsanhang
{
    /// <inheritdoc />
    public string? Aktuell =>
        zugriff.HttpContext?.Items.TryGetValue("CorrelationId", out var value) == true
            ? value?.ToString()
            : null;

    /// <inheritdoc />
    public AuditEntry Eintrag(
        AuditAction action,
        DateTimeOffset geschehen,
        SubjectId? akteur = null,
        TenantId? company = null,
        SubjectId? betroffene = null) =>
        new(action, geschehen, akteur, company, betroffene, Aktuell);
}
