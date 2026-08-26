using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

/// <summary>Die Adresse der Karriere-Seite.</summary>
/// <remarks>
/// <strong>Abgeleitet und nicht eingegeben</strong>: ein freies Feld lädt zum
/// Besetzen fremder Namen ein.
/// </remarks>
public static partial class Kuerzel
{
    /// <summary>Wie lang ein Kürzel höchstens wird.</summary>
    public const int Hoechstlaenge = 60;

    /// <summary>Wenn vom Namen nichts Verwendbares übrig bleibt.</summary>
    /// <remarks>
    /// Etwa bei einem rein chinesischen Namen. Der Zähler beim Speichern macht
    /// daraus <c>unternehmen-2</c> und so weiter; eine leere Adresse wäre
    /// schlimmer als eine unpersönliche.
    /// </remarks>
    public const string Rueckfall = "unternehmen";

    /// <summary>Leitet ein Kürzel aus dem Anzeigenamen ab.</summary>
    /// <remarks>
    /// Umlaute werden zerlegt und ihre Grundbuchstaben behalten — „Grün" wird
    /// <c>gruen</c> nur mit einer Ersetzungstabelle, <c>grun</c> ohne;
    /// letzteres ist ehrlicher als eine Tabelle, die bei der nächsten Sprache
    /// falsch liegt.
    /// </remarks>
    public static string Aus(string? anzeigename)
    {
        var zerlegt = (anzeigename ?? string.Empty)
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormKD);

        var nurAscii = new StringBuilder(zerlegt.Length);

        foreach (var zeichen in zerlegt)
        {
            // Verbindende Zeichen fallen weg, alles andere jenseits von ASCII
            // ebenso — der Trennstrich unten macht daraus ohnehin eine Lücke.
            if (zeichen < 128
                && CharUnicodeInfo.GetUnicodeCategory(zeichen)
                    != UnicodeCategory.NonSpacingMark)
            {
                nurAscii.Append(zeichen);
            }
        }

        var getrennt = NichtErlaubtes()
            .Replace(nurAscii.ToString(), "-")
            .Trim('-');

        if (getrennt.Length > Hoechstlaenge)
        {
            getrennt = getrennt[..Hoechstlaenge].Trim('-');
        }

        return getrennt.Length == 0 ? Rueckfall : getrennt;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NichtErlaubtes();
}
