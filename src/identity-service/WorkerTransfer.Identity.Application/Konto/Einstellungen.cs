using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Konto;

/// <summary>Was eine Person über sich entschieden hat — zum Anzeigen.</summary>
/// <remarks>
/// <strong>Ohne den Schlüssel.</strong> Zurück gehen nur <c>SchluesselDa</c> und
/// <c>SchluesselEndung</c>: das genügt, um ihn wiederzuerkennen, und mehr darf
/// über die Leitung nicht zurück. Ein Geheimnis, das man abrufen kann, ist
/// eines, das man abziehen kann.
/// </remarks>
/// <param name="LoeschungNachMonaten">Verfall, oder <c>null</c>.</param>
/// <param name="Anbieter">Der KI-Anbieter als Etikett.</param>
/// <param name="Adresse">Dessen Adresse.</param>
/// <param name="Modell">Dessen Modell.</param>
/// <param name="SchluesselDa">Ob ein Schlüssel hinterlegt ist.</param>
/// <param name="SchluesselEndung">Seine letzten vier Zeichen.</param>
/// <param name="KiProtokoll">Ob Anfragen protokolliert werden.</param>
public sealed record SettingsView(
    int? LoeschungNachMonaten,
    string Anbieter,
    string Adresse,
    string Modell,
    bool SchluesselDa,
    string SchluesselEndung,
    bool KiProtokoll);

/// <summary>Die eigenen Einstellungen lesen. Immer nur die eigenen.</summary>
/// <param name="Wer">Der Aufrufer.</param>
public sealed record SettingsQuery(SubjectId Wer) : IAbfrage<SettingsView>;

/// <inheritdoc cref="EinstellungenAbfrage" />
public sealed class SettingsHandler(IAccountSettingsStore store)
    : IRequestHandler<SettingsQuery, SettingsView>
{
    /// <inheritdoc />
    public async Task<SettingsView> Handle(
        SettingsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Wer, cancellationToken);
        return Ansicht(state);
    }

    /// <summary>Aus dem Aggregat wird die Ansicht — ohne das Geheimnis.</summary>
    internal static SettingsView Ansicht(AccountSettings state) =>
        new(state.LoeschungNachMonaten,
            Etikett(state.Anbieter),
            state.Adresse,
            state.Modell,
            state.SchluesselVerschluesselt.Length > 0,
            state.SchluesselEndung,
            state.KiProtokoll);

    internal static string Etikett(AiProvider provider) => provider switch
    {
        AiProvider.OpenAiKompatibel => "openai_compatible",
        AiProvider.Anthropic => "anthropic",
        _ => "none"
    };

    internal static AiProvider Anbieter(string? label) => label switch
    {
        "openai_compatible" => AiProvider.OpenAiKompatibel,
        "anthropic" => AiProvider.Anthropic,
        _ => AiProvider.None
    };
}

/// <summary>Die eigenen Einstellungen schreiben — ohne den Schlüssel.</summary>
/// <remarks>
/// Der Schlüssel hat einen eigenen Befehl. Zwei Gründe, und beide zählen: er
/// wird selten geändert und ginge sonst bei jedem Speichern über die Leitung;
/// und die Oberfläche bekommt ihn nie zurück, könnte ihn also gar nicht
/// mitschicken, ohne ihn jedes Mal neu abzufragen.
/// </remarks>
/// <param name="Wer">Der Aufrufer.</param>
/// <param name="LoeschungNachMonaten">Verfall, oder <c>null</c>.</param>
/// <param name="Anbieter">Der KI-Anbieter als Etikett.</param>
/// <param name="Adresse">Dessen Adresse.</param>
/// <param name="Modell">Dessen Modell.</param>
/// <param name="KiProtokoll">Ob Anfragen protokolliert werden.</param>
public sealed record SetSettingsCommand(
    SubjectId Wer,
    int? LoeschungNachMonaten,
    string Anbieter,
    string Adresse,
    string Modell,
    bool KiProtokoll) : IBefehl<SettingsView>;

/// <inheritdoc cref="EinstellungenSetzenBefehl" />
public sealed class SetSettingsHandler(IAccountSettingsStore store)
    : IRequestHandler<SetSettingsCommand, SettingsView>
{
    /// <inheritdoc />
    public async Task<SettingsView> Handle(
        SetSettingsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Wer, cancellationToken);

        state.SetzeDatenschutz(request.LoeschungNachMonaten);
        state.SetzeKi(
            SettingsHandler.Anbieter(request.Anbieter),
            request.Adresse,
            request.Modell,
            request.KiProtokoll);

        await store.SaveAsync(state, cancellationToken);

        return SettingsHandler.Ansicht(state);
    }
}

/// <summary>Einen Schlüssel hinterlegen oder entfernen.</summary>
/// <param name="Wer">Der Aufrufer.</param>
/// <param name="Klartext">
/// Der Schlüssel. Leer heisst ENTFERNEN und nicht „unverändert lassen": ein
/// Feld, das bei leer nichts tut, hat keinen Weg zurück zu „keiner".
/// </param>
public sealed record SchluesselSetzenBefehl(SubjectId Wer, string Klartext) : IBefehl<bool>;

/// <inheritdoc cref="SchluesselSetzenBefehl" />
public sealed class SchluesselSetzenHandler(
    IAccountSettingsStore store, ISecrets geheimnisse)
    : IRequestHandler<SchluesselSetzenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SchluesselSetzenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Wer, cancellationToken);
        var klartext = request.Klartext.Trim();

        if (klartext.Length == 0)
        {
            state.LoescheSchluessel();
        }
        else
        {
            state.SetzeSchluessel(
                geheimnisse.Encrypt(klartext), geheimnisse.Endung(klartext));
        }

        await store.SaveAsync(state, cancellationToken);
        return true;
    }
}

/// <summary>
/// Der KI-Zugang einer Person — intern, mit Klartextschlüssel.
/// </summary>
/// <remarks>
/// Nur über <c>/internal/account/{id}/ai</c>. Der Browser bekommt diese
/// Gestalt nie: <c>GET /account/settings</c> trägt den Schlüssel absichtlich
/// nicht.
/// </remarks>
public sealed record InternalAiAccess(
    string Anbieter, string Adresse, string Modell, string Schluessel);

/// <summary>Den KI-Zugang einer Person lesen — für andere Dienste, nicht für den Browser.</summary>
public sealed record InternalAiAccessQuery(SubjectId Wer) : IAbfrage<InternalAiAccess>;

/// <inheritdoc cref="InterneKiZugangAbfrage" />
public sealed class InternalAiAccessHandler(
    IAccountSettingsStore store, ISecrets geheimnisse)
    : IRequestHandler<InternalAiAccessQuery, InternalAiAccess>
{
    /// <inheritdoc />
    public async Task<InternalAiAccess> Handle(
        InternalAiAccessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = await store.GetAsync(request.Wer, cancellationToken);

        return new InternalAiAccess(
            SettingsHandler.Etikett(state.Anbieter),
            state.Adresse,
            state.Modell,
            geheimnisse.Decrypt(state.SchluesselVerschluesselt) ?? string.Empty);
    }
}

/// <summary>
/// Klarname und Kontakt einer Person — intern, und ausdrücklich ohne PostalAddress.
/// </summary>
/// <remarks>
/// <para><strong>Zwei Felder, und mehr dürfen es nie werden.</strong> Der
/// bürgerliche Name und die Adresse, unter der man antworten kann. None
/// Postanschrift, kein Telefon: ADR-0038 legt die PostalAddress als
/// <em>ResumeTemplate</em> für einen Briefkopf ab, und sie gehört zu einer JobApplication,
/// die die Person selbst sendet — nicht in die Ansicht eines Unternehmens.</para>
/// </remarks>
public sealed record InternalPerson(string Name, string Email);

/// <summary>
/// Wie ein Mensch heisst — für advisor-service, hinter dem Geheimnis.
/// </summary>
/// <remarks>
/// <para><strong>Das Geheimnis ist hier nicht die ganze Erlaubnis.</strong> Es
/// beweist, dass ein Dienst fragt, und nicht, dass er fragen darf. Die
/// Erlaubnis steht im Ledger, und advisor-service holt sie <em>vor</em> diesem
/// Aufruf: Stufe 3 ist die Fähigkeit <c>advisor.identity:tenant:&lt;id&gt;</c>
/// (ADR-0037 Entscheidung 2). Wer diese Tür für etwas anderes benutzt, holt
/// sich die Prüfung dazu — sonst gibt er den Klarnamen eines Menschen heraus,
/// der ihn niemandem gezeigt hat.</para>
///
/// <para>Deshalb ist die Tür so eng: sie antwortet auf genau eine Kennung, mit
/// genau zwei Feldern, und sie kennt keine Liste.</para>
/// </remarks>
public sealed record InternalPersonQuery(SubjectId Wer) : IAbfrage<InternalPerson?>;

/// <inheritdoc cref="InternePersonAbfrage" />
public sealed class InternalPersonHandler(IUserRepository konten)
    : IRequestHandler<InternalPersonQuery, InternalPerson?>
{
    /// <inheritdoc />
    public async Task<InternalPerson?> Handle(
        InternalPersonQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await konten.FindByIdAsync(request.Wer, cancellationToken);

        // `Klarname` faellt auf den Anzeigenamen zurueck, wenn kein
        // buergerlicher hinterlegt ist — dasselbe, was unter einem Anschreiben
        // steht. Leer bleibt leer, und der Aufrufer laesst das Feld dann ganz
        // weg: nicht freigegeben und nicht vorhanden sehen gleich aus.
        return konto is null ? null : new InternalPerson(konto.Klarname, konto.Email);
    }
}
