using Girder.Core.Identity;

namespace WorkerTransfer.Assessment.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.</summary>
/// <remarks>
/// Ausdrücklich <em>kein</em> <c>false</c>. „Ich weiss es nicht" ist etwas
/// anderes als „nichts freigegeben", und die beiden gleich zu behandeln hiesse,
/// einen Ausfall in eine Aussage über einen Menschen zu verwandeln. Der
/// Endpunkt macht daraus 503 — nicht 404, denn das wäre die Behauptung, es gebe
/// nichts, und nicht „zeigen", denn das wäre die umgekehrte (ADR-0020 §3).
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class EinwilligungSchweigt(string grund) : Exception(grund);

/// <summary>Fragt den Ledger, ob ein Unternehmen diesen Menschen sehen darf.</summary>
/// <remarks>
/// <para><strong>Synchron, je Anfrage, ohne jeden Zwischenspeicher</strong>
/// (ADR-0013). Ein Widerruf muss beim nächsten Lesen wirken, nicht beim
/// übernächsten.</para>
///
/// <para><strong>Es ist dieselbe Fähigkeit wie Stufe 1 eines Gesprächs</strong>
/// — <c>profile.visibility:tenant:&lt;id&gt;</c> oder
/// <c>profile.visibility:public</c> — und ausdrücklich <em>keine eigene</em>.
/// Eine <c>assessment.*</c> wäre eine zweite Wahrheit über dieselbe Frage, und
/// sie liefe beim ersten Widerruf auseinander (ADR-0042, ADR-0037
/// Entscheidung 2).</para>
///
/// <para><strong>Und sie bewacht nur die Firmenseite.</strong> Die Person liest
/// ihren eigenen Vorgang immer — auch nachdem sie widerrufen hat. Eine
/// Bewertung, aus der man sich aussperren kann, indem man Sichtbarkeit
/// zurücknimmt, wäre eine Beurteilung, die der Beurteilte nie liest (ADR-0042
/// §2). Deshalb gibt es hier <em>eine</em> Methode und keine für „darf die
/// Person?".</para>
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Darf dieses Unternehmen diesen Menschen sehen?</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<bool> DarfSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Dieselbe Frage für eine ganze Seite, in einer Runde.</summary>
    /// <remarks>
    /// Als Sammelfrage (ADR-0030): eine Frage je Zeile wären bei einer Liste
    /// von vierzig Vorgängen vierzig Verbindungsaufbauten. Die Antworten kommen
    /// in der Reihenfolge der Fragen, und eine abweichende Länge ist ein
    /// <em>Fehler</em> und kein Anlass zu raten — sie falsch zuzuordnen hiesse,
    /// die Freigabe des falschen Menschen zu lesen.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<IReadOnlyList<bool>> DuerfenSehenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default);
}

/// <summary>Die Fähigkeiten, an denen eine Arbeitsprobe steht.</summary>
/// <remarks>
/// Zwei, und beide sind <strong>bestehende</strong>. Die zweite ist die
/// weitere: wer öffentlich sichtbar ist, ist es auch für dieses Unternehmen —
/// sonst behauptete dieser Dienst eine Verborgenheit, die es nicht gibt.
/// </remarks>
public static class Sichtbarkeiten
{
    /// <summary>„Für alle Unternehmen" — der Schalter auf der Profilseite.</summary>
    public const string ProfilOeffentlich = "profile.visibility:public";

    /// <summary>Das Profil, für dieses eine Unternehmen.</summary>
    public static string Profil(TenantId firma) => $"profile.visibility:tenant:{firma.Value}";
}
