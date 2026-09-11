using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>„Ich bin einverstanden."</summary>
public sealed record ZustimmenBefehl(Guid Id, SubjectId Wer) : IBefehl<Gespraech>;

/// <summary>
/// Die Zustimmung der Person — und nur sie macht eine Übergabe möglich.
/// </summary>
/// <remarks>
/// Die Einigung, aus der ein Vorgang wird, gehört dem Menschen, um den es geht.
/// Ein Gespräch, das ein Unternehmen allein in einen Transfer heben könnte,
/// wäre eine Einladung, die man nicht ausschlagen kann.
/// </remarks>
public sealed class ZustimmenHandler(IGespraechsspeicher speicher, TimeProvider uhr)
    : IRequestHandler<ZustimmenBefehl, Gespraech>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Menschen.</exception>
    /// <exception cref="UebergangNichtErlaubt">Von diesem Stand aus nicht.</exception>
    public async Task<Gespraech> Handle(ZustimmenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gespraech = await StufeFreigebenHandler.Meins(
            speicher, request.Id, request.Wer, cancellationToken);

        gespraech.Stimme_zu(request.Wer, uhr.GetUtcNow());

        await speicher.SichereAsync(gespraech, cancellationToken);

        return gespraech;
    }
}

/// <summary>„Macht einen Vorgang daraus."</summary>
public sealed record UebergebenBefehl(Guid Id, TenantId Firma) : IBefehl<Guid>;

/// <summary>
/// Die Übergabe an transfer-service — <strong>ein Aufruf, keine zweite
/// Zustandsmaschine.</strong>
/// </summary>
/// <remarks>
/// <para>ADR-0037 Entscheidung 4: ein Gespräch lebt hier, solange es um Stufen
/// und Mandat geht; erst die Einigung wird ein Vorgang. Der Dreieckskonsens
/// steht in transfer-service und wird <em>nicht</em> nachgebaut — ob der
/// Marktstatus diesem Unternehmen freigegeben ist, ob die Person ansprechbar
/// ist, ob eine Freigabe des jetzigen Arbeitgebers nötig ist: alles das prüft
/// die Tür, die hier gerufen wird.</para>
///
/// <para><strong>Eine Ablehnung von dort ist keine 500.</strong> Sie ist eine
/// Aussage über die Bedingungen dieses Vorgangs, und der Endpunkt macht daraus
/// 409. Ein Schweigen dagegen ist 503: „wir wissen es nicht" ist etwas anderes
/// als „geht nicht".</para>
///
/// <para><strong>Erst schreiben, wenn es geklappt hat.</strong> Der Stand geht
/// nach <c>handed_over</c>, nachdem der Vorgang wirklich angelegt ist —
/// andersherum stünde hier „übergeben" und dort nichts.</para>
/// </remarks>
public sealed class UebergebenHandler(
    IGespraechsspeicher speicher, IVorgangsuebergabe uebergabe, TimeProvider uhr)
    : IRequestHandler<UebergebenBefehl, Guid>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Unternehmen.</exception>
    /// <exception cref="UebergangNichtErlaubt">Die Person hat noch nicht zugestimmt.</exception>
    /// <exception cref="UebergabeAbgelehnt">transfer-service lehnt ab.</exception>
    public async Task<Guid> Handle(UebergebenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gespraech = await speicher.HoleAsync(request.Id, cancellationToken);

        if (gespraech is null || gespraech.Firma != request.Firma)
        {
            throw new KeinGespraech();
        }

        if (gespraech.Stand != Gespraechsstand.Zugestimmt)
        {
            throw new UebergangNichtErlaubt(gespraech.Stand, "handed over");
        }

        var vorgang = await uebergabe.UebergibAsync(
            gespraech.Wer, gespraech.Anlass, cancellationToken);

        gespraech.Uebergib(uhr.GetUtcNow());

        await speicher.SichereAsync(gespraech, cancellationToken);

        return vorgang;
    }
}

/// <summary>„Schluss."</summary>
/// <param name="Wer">Gesetzt, wenn die Person beendet.</param>
/// <param name="Firma">Gesetzt, wenn das Unternehmen beendet.</param>
public sealed record BeendenBefehl(Guid Id, SubjectId? Wer, TenantId? Firma) : IBefehl<Gespraech>;

/// <summary>Beide Seiten dürfen aufhören, aus jedem laufenden Stand.</summary>
/// <remarks>
/// Ein Verfahren, aus dem man nicht aussteigen kann, ist kein Verfahren,
/// sondern eine Falle — dieselbe Zeile steht in transfer-service, und sie gilt
/// hier genauso.
/// <para>
/// Die Stufen bleiben, wo sie sind. Ein Ende ist keine Rücknahme: wer auch die
/// Sichtbarkeit zurücknehmen will, widerruft sie — und das ist ein zweiter,
/// bewusster Schritt, keine Nebenwirkung.
/// </para>
/// </remarks>
public sealed class BeendenHandler(IGespraechsspeicher speicher, TimeProvider uhr)
    : IRequestHandler<BeendenBefehl, Gespraech>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört jemand anderem.</exception>
    /// <exception cref="UebergangNichtErlaubt">Es läuft nicht mehr.</exception>
    public async Task<Gespraech> Handle(BeendenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gespraech = await speicher.HoleAsync(request.Id, cancellationToken);

        var meins = gespraech is not null
                    && ((request.Wer is { } wer && gespraech.Wer == wer)
                        || (request.Firma is { } firma && gespraech.Firma == firma));

        if (gespraech is null || !meins)
        {
            throw new KeinGespraech();
        }

        gespraech.Beende(uhr.GetUtcNow());

        await speicher.SichereAsync(gespraech, cancellationToken);

        return gespraech;
    }
}
