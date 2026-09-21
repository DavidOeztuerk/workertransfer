namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Der Identity-Dienst (interner Endpunkt) hat nicht geantwortet.</summary>
/// <remarks>
/// Wird geworfen, wenn der interne Draht zu identity-service nicht erreichbar
/// ist, das Geheimnis nicht stimmt oder die Antwort unbrauchbar ist. Wird von
/// den Endpunktfiltern in einen 503 übersetzt, damit der Aufrufer erfährt,
/// dass dieser Dienst gerade nicht antworten kann — nicht, dass die Firma
/// nicht existiert.
/// </remarks>
public sealed class CompanyLookupSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);
