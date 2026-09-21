using Microsoft.AspNetCore.Http;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Infrastructure.Security;

/// <summary>Reads the correlation id Noelia's middleware attached.</summary>
public sealed class HttpCorrelation(IHttpContextAccessor accessor) : ICorrelation
{
    /// <inheritdoc />
    public string? Current =>
        accessor.HttpContext?.Items.TryGetValue("CorrelationId", out var value) == true
            ? value?.ToString()
            : null;
}
