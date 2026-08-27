using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Girder.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Wo eine Seite weitergeht.</summary>
/// <param name="GeaendertAm">Der Zeitstempel der letzten gezeigten Zeile.</param>
/// <param name="Wer">Deren Person.</param>
/// <remarks>
/// Beide Sortierschlüssel, und das ist der ganze Punkt: <c>GeaendertAm</c>
/// allein reicht nicht, weil zwei Profile in derselben Sekunde sich beim
/// Blättern gegenseitig überspringen oder doppelt erscheinen würden.
/// <para>
/// Ein Zeiger und kein Seitenversatz (<c>OFFSET</c>): der Versatz verschiebt
/// sich, sobald jemand zwischen zwei Seitenaufrufen sein Profil ändert — und
/// hier ändert der Zeitstempel die Sortierung, also passiert genau das
/// dauernd.
/// </para>
/// </remarks>
public sealed record Seitenzeiger(DateTimeOffset GeaendertAm, SubjectId Wer)
{
    /// <summary>Die Textform, wie sie in der URL steht.</summary>
    /// <remarks>
    /// Base64 nicht als Verschlüsselung — sie ist keine — sondern als Zeichen an
    /// den Aufrufer, dass die Form uns gehört und sich ändern darf.
    /// </remarks>
    public string Schreibe() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{GeaendertAm.ToString("O", CultureInfo.InvariantCulture)}|{Wer.Value}"));

    /// <summary>Liest die Textform zurück — oder gibt <c>null</c>.</summary>
    /// <remarks>
    /// Ein unlesbarer Zeiger ist kein Fehler, sondern der Anfang. Er kommt aus
    /// einer URL und wird kopiert, gekürzt und weitergereicht; darauf mit einem
    /// 400 zu antworten hilft niemandem und macht einen geteilten Link zu einer
    /// Fehlermeldung.
    /// </remarks>
    /// <param name="text">Was in der Anfrage stand.</param>
    public static Seitenzeiger? Lies(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Span<byte> puffer = new byte[text.Length];

        if (!Convert.TryFromBase64String(text, puffer, out var geschrieben))
        {
            return null;
        }

        var roh = Encoding.UTF8.GetString(puffer[..geschrieben]);
        var trenner = roh.IndexOf('|', StringComparison.Ordinal);

        if (trenner < 0)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
                   roh[..trenner], CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var stempel)
               && Guid.TryParse(roh[(trenner + 1)..], out var wer)
            ? new Seitenzeiger(stempel, new SubjectId(wer))
            : null;
    }
}
