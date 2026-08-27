using Microsoft.AspNetCore.Http;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Infrastructure.Security;

/// <summary>Liest die Korrelations-Id, die Girders Middleware angehängt hat.</summary>
public sealed class HttpKorrelation(IHttpContextAccessor zugriff) : IKorrelation
{
    /// <inheritdoc />
    public string? Aktuell =>
        zugriff.HttpContext?.Items.TryGetValue("CorrelationId", out var wert) == true
            ? wert?.ToString()
            : null;
}
