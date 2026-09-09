using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Infrastructure.Security;

/// <summary>Reads the correlation id Girder's middleware put on the request.</summary>
public sealed class HttpKorrelation(IHttpContextAccessor zugriff) : IKorrelation, IKorrelationsanhang
{
    /// <inheritdoc />
    public string? Aktuell =>
        zugriff.HttpContext?.Items.TryGetValue("CorrelationId", out var wert) == true
            ? wert?.ToString()
            : null;

    /// <inheritdoc />
    public Pruefeintrag Eintrag(
        Pruefhandlung handlung,
        DateTimeOffset geschehen,
        SubjectId? akteur = null,
        TenantId? firma = null,
        SubjectId? betroffene = null) =>
        new(handlung, geschehen, akteur, firma, betroffene, Aktuell);
}
