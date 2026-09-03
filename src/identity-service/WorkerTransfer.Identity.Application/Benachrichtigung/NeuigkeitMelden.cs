using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Benachrichtigung;

/// <summary>„Sag dieser Person, dass es etwas Neues gibt."</summary>
/// <remarks>
/// Kommt von notification-service, das entschieden hat, <em>ob</em> etwas
/// hinausgeht. Der Befehl trägt <strong>nur eine Kennung</strong> — keine Art,
/// keinen Text: die Adresse liegt hier, der Satz auch, und ein Aufrufer, der
/// etwas beisteuern könnte, wäre der Anfang von „nur diese eine Zeile noch".
/// </remarks>
public sealed record NeuigkeitMeldenBefehl(SubjectId Wer) : IBefehl<bool>;

/// <summary>Löst die Adresse auf und legt die eine Mail in den Postkorb.</summary>
/// <remarks>
/// Gibt zurück, ob etwas eingelegt wurde — für Tests und Protokoll. Der
/// Endpunkt gibt diese Auskunft <strong>nicht</strong> weiter: sonst wäre er
/// ein Orakel darüber, ob es diese Person gibt und ob ihr Konto bestätigt ist.
/// </remarks>
public sealed class NeuigkeitMeldenHandler(IUserRepository benutzer, IPostkorb postkorb)
    : IRequestHandler<NeuigkeitMeldenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        NeuigkeitMeldenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);

        // Ein unbestätigtes Konto bekommt keine Post über Vorgänge — die
        // Adresse ist noch nicht als seine erwiesen.
        if (konto is null || konto.Status is not AccountStatus.Active)
        {
            return false;
        }

        postkorb.Neuigkeit(konto.Email, konto.Id, konto.Kontosprache);

        return true;
    }
}
