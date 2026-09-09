using Girder.Core.Identity;
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
public sealed record Einstellungsansicht(
    int? LoeschungNachMonaten,
    string Anbieter,
    string Adresse,
    string Modell,
    bool SchluesselDa,
    string SchluesselEndung,
    bool KiProtokoll);

/// <summary>Die eigenen Einstellungen lesen. Immer nur die eigenen.</summary>
/// <param name="Wer">Der Aufrufer.</param>
public sealed record EinstellungenAbfrage(SubjectId Wer) : IAbfrage<Einstellungsansicht>;

/// <inheritdoc cref="EinstellungenAbfrage" />
public sealed class EinstellungenHandler(IKontoeinstellungen speicher)
    : IRequestHandler<EinstellungenAbfrage, Einstellungsansicht>
{
    /// <inheritdoc />
    public async Task<Einstellungsansicht> Handle(
        EinstellungenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stand = await speicher.HoleAsync(request.Wer, cancellationToken);
        return Ansicht(stand);
    }

    /// <summary>Aus dem Aggregat wird die Ansicht — ohne das Geheimnis.</summary>
    internal static Einstellungsansicht Ansicht(Kontoeinstellungen stand) =>
        new(stand.LoeschungNachMonaten,
            Etikett(stand.Anbieter),
            stand.Adresse,
            stand.Modell,
            stand.SchluesselVerschluesselt.Length > 0,
            stand.SchluesselEndung,
            stand.KiProtokoll);

    internal static string Etikett(KiAnbieter anbieter) => anbieter switch
    {
        KiAnbieter.OpenAiKompatibel => "openai_compatible",
        KiAnbieter.Anthropic => "anthropic",
        _ => "none"
    };

    internal static KiAnbieter Anbieter(string? etikett) => etikett switch
    {
        "openai_compatible" => KiAnbieter.OpenAiKompatibel,
        "anthropic" => KiAnbieter.Anthropic,
        _ => KiAnbieter.Keiner
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
public sealed record EinstellungenSetzenBefehl(
    SubjectId Wer,
    int? LoeschungNachMonaten,
    string Anbieter,
    string Adresse,
    string Modell,
    bool KiProtokoll) : IBefehl<Einstellungsansicht>;

/// <inheritdoc cref="EinstellungenSetzenBefehl" />
public sealed class EinstellungenSetzenHandler(IKontoeinstellungen speicher)
    : IRequestHandler<EinstellungenSetzenBefehl, Einstellungsansicht>
{
    /// <inheritdoc />
    public async Task<Einstellungsansicht> Handle(
        EinstellungenSetzenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stand = await speicher.HoleAsync(request.Wer, cancellationToken);

        stand.SetzeDatenschutz(request.LoeschungNachMonaten);
        stand.SetzeKi(
            EinstellungenHandler.Anbieter(request.Anbieter),
            request.Adresse,
            request.Modell,
            request.KiProtokoll);

        await speicher.SichereAsync(stand, cancellationToken);

        return EinstellungenHandler.Ansicht(stand);
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
    IKontoeinstellungen speicher, IGeheimnisse geheimnisse)
    : IRequestHandler<SchluesselSetzenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SchluesselSetzenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stand = await speicher.HoleAsync(request.Wer, cancellationToken);
        var klartext = request.Klartext.Trim();

        if (klartext.Length == 0)
        {
            stand.LoescheSchluessel();
        }
        else
        {
            stand.SetzeSchluessel(
                geheimnisse.Verschluessele(klartext), geheimnisse.Endung(klartext));
        }

        await speicher.SichereAsync(stand, cancellationToken);
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
public sealed record InternerKiZugang(
    string Anbieter, string Adresse, string Modell, string Schluessel);

/// <summary>Den KI-Zugang einer Person lesen — für andere Dienste, nicht für den Browser.</summary>
public sealed record InterneKiZugangAbfrage(SubjectId Wer) : IAbfrage<InternerKiZugang>;

/// <inheritdoc cref="InterneKiZugangAbfrage" />
public sealed class InterneKiZugangHandler(
    IKontoeinstellungen speicher, IGeheimnisse geheimnisse)
    : IRequestHandler<InterneKiZugangAbfrage, InternerKiZugang>
{
    /// <inheritdoc />
    public async Task<InternerKiZugang> Handle(
        InterneKiZugangAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stand = await speicher.HoleAsync(request.Wer, cancellationToken);

        return new InternerKiZugang(
            EinstellungenHandler.Etikett(stand.Anbieter),
            stand.Adresse,
            stand.Modell,
            geheimnisse.Entschluessele(stand.SchluesselVerschluesselt) ?? string.Empty);
    }
}
