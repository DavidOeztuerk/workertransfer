using Girder.Infrastructure.Audit;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Girders Prüfspur wird nicht benutzt — und sagt es, statt zu schweigen.</summary>
/// <remarks>
/// <para><strong>Girders Prüfspur kommt im Bündel mit, ohne dass wir sie
/// wollen.</strong> <c>AddSovereignPlatform</c> bringt vier Dinge in einem
/// Modul: Egress-Grenze, Maskierung, Souveränitätsbericht und Prüfspur. Die
/// ersten drei sind der Grund, warum wir das Modul fahren; die vierte lässt
/// sich nicht abwählen.</para>
///
/// <para><strong>Sie darf hier nicht benutzt werden, und das ist keine
/// Geschmacksfrage.</strong> ADR-0012 verlangt, dass eine Prüfzeile in
/// <em>derselben Transaktion</em> entsteht wie die Änderung, die sie festhält.
/// Girders Rückfall-Senke schreibt in eine Liste im Prozess: sie überlebt kein
/// Rollback, sie überlebt keinen Neustart, und sie liegt neben unserer
/// Transaktion statt darin. <c>EfPruefspur</c> in resume-service tut, was der
/// ADR verlangt — eine zweite Spur daneben wäre schlechter als keine, weil
/// niemand mehr sagen könnte, welche vollständig ist.</para>
///
/// <para><strong>Warum eine werfende Senke und kein Kommentar.</strong> Ein
/// Kommentar hält niemanden auf. Diese Senke wird nie gerufen, solange niemand
/// <c>IAuditTrailService</c> benutzt — und wer es tut, bekommt sofort einen
/// Satz, der sagt wohin stattdessen. Ein stiller Speicher, der aussieht wie
/// eine Prüfspur, wäre die schlechtere Antwort: er würde erst auffallen, wenn
/// jemand die Spur braucht und sie leer ist.</para>
/// </remarks>
public sealed class VerweigerndePruefspur : ISovereignAuditSink
{
    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Immer.</exception>
    public Task WriteAsync<T>(
        AuditEvent<T> auditEvent, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "WorkerTransfer führt seine Prüfspur in derselben Transaktion wie die "
            + "Änderung (ADR-0012, EfPruefspur). Girders Prüfspur kommt nur im "
            + "Bündel von AddSovereignPlatform mit und hat hier keine Senke. Wer "
            + "eine Prüfzeile schreiben will, nimmt IPruefspur des eigenen "
            + "Dienstes.");
}
