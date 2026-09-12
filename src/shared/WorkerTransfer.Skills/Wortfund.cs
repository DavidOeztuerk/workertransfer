using System.Collections.Frozen;
using System.Text;

namespace WorkerTransfer.Skills;

/// <summary>Welche bekannten Wörter in einem Text vorkommen — und keins mehr.</summary>
/// <remarks>
/// <strong>Es sucht, was der <see cref="Wortschatz"/> kennt, und rät nie.</strong>
/// Das ist die ganze Grenze, und sie ist dieselbe wie eine Zeile weiter oben:
/// <c>Wortschatz</c> benennt um und folgert nicht, und diese Funktion wendet
/// genau das auf einen gefundenen Text an. Sie liest in einem Zeugnis die
/// Schreibweise <c>„schweissfachmann"</c> und bietet dafür den kanonischen
/// Namen <c>„Schweißfachmann"</c> an — eine Aussage über <em>Sprache</em>.
/// <para>
/// Was sie ausdrücklich <em>nicht</em> tut: aus der Form eines Wortes schliessen,
/// dass es eine Fähigkeit sei. „Grossgeschrieben", „steht hinter dem Doppelpunkt",
/// „kommt oft vor" — jede dieser Regeln wäre eine Vermutung über einen Menschen,
/// gebildet aus der Gestalt seines Dokuments. Wer ein Wort vermisst, erweitert
/// den Wortschatz per Pull Request; er wird nie aus den Unterlagen von Menschen
/// gelernt (ADR-0023).
/// </para>
/// <para>
/// Der Preis ist ehrlich und muss es bleiben: <strong>was der Wortschatz nicht
/// kennt, wird nicht gefunden.</strong> Eine Oberfläche, die das verschweigt,
/// behauptet stillschweigend, in einem Dokument stehe nichts — ADR-0022 §3.
/// </para>
/// <para>
/// Das Ergebnis ist ein <em>Vorschlag</em> und nie eine Nennung. Zwischen dem
/// gefundenen Wort und einer Aussage über den Menschen liegen zwei Handlungen:
/// ein Klick füllt das Formularfeld, und erst „Speichern" macht daraus eine
/// Nennung (ADR-0033).
/// </para>
/// </remarks>
public static class Wortfund
{
    /// <summary>Ab welcher Länge eine Schreibweise in jeder Schreibung gilt.</summary>
    /// <remarks>
    /// <strong>Kurze Kürzel nur in Grossbuchstaben, und das ist gemessen.</strong>
    /// „wig" ist im Englischen eine Perücke, „go" ein alltägliches Verb, „ts"
    /// und „py" stehen in jedem zweiten Dateinamen. In Kleinschreibung trifft
    /// ein Dreibuchstabenwort irgendwo in jedem längeren Zeugnis — und ein
    /// falscher Vorschlag ist hier teurer als ein fehlender, weil er der Person
    /// ein Wort über sich selbst anbietet, das in ihrem Dokument nie stand.
    /// <para>
    /// Die Regel greift nur bei Schreibweisen aus <em>reinen Buchstaben</em>:
    /// „C#", „C++" und „k8s" tragen ein Zeichen, das sie ohnehin unverwechselbar
    /// macht, und werden weiterhin in jeder Schreibung erkannt.
    /// </para>
    /// </remarks>
    public const int UnterDieserLaengeNurVersal = 4;

    /// <summary>Schreibweise → kanonischer Name, kleingeschrieben nachgeschlagen.</summary>
    /// <remarks>
    /// Aus <see cref="Wortschatz.Schreibweisen"/> gebaut und nicht daneben
    /// gepflegt: eine zweite Liste wäre die, die als Erste veraltet — und
    /// niemand merkte es, weil ein nicht gefundenes Wort wie ein Dokument ohne
    /// Inhalt aussieht.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> NachSchreibweise = Baue();

    /// <summary>Die längste Schreibweise — die Obergrenze jedes Vergleichsfensters.</summary>
    private static readonly int LaengsteSchreibweise =
        NachSchreibweise.Keys.Max(schreibweise => schreibweise.Length);

    /// <summary>Die kanonischen Namen, die in diesem Text vorkommen.</summary>
    /// <param name="text">Der gefundene Volltext. Wird nirgends festgehalten.</param>
    /// <returns>
    /// Jeden Namen einmal, in der Reihenfolge seines ersten Vorkommens im Text.
    /// Die Reihenfolge ordnet <em>Wörter</em> in <em>einem Dokument</em> und
    /// nie Menschen; eine Zahl entsteht daraus nirgends.
    /// </returns>
    public static IReadOnlyList<string> Finde(string? text)
    {
        var geputzt = Verdichte(text);

        if (geputzt.Length == 0)
        {
            return [];
        }

        var klein = geputzt.ToLowerInvariant();
        var gefunden = new List<string>();
        var schon = new HashSet<string>(StringComparer.Ordinal);

        for (var start = 0; start < klein.Length; start++)
        {
            if (!BeginntEinWort(klein, start))
            {
                continue;
            }

            // Vom längsten Fenster abwärts: „Microsoft SQL Server" muss sich
            // gegen „MySQL" durchsetzen, und „gesundheits- und krankenpfleger"
            // gegen jedes kürzere Stück darin. Andersherum gewänne das erste
            // Teilwort und der längere Name käme nie zum Zug.
            var laengste = Math.Min(LaengsteSchreibweise, klein.Length - start);

            for (var laenge = laengste; laenge > 0; laenge--)
            {
                if (!EndetEinWort(klein, start + laenge))
                {
                    continue;
                }

                var fenster = klein.Substring(start, laenge);

                if (!NachSchreibweise.TryGetValue(fenster, out var name))
                {
                    continue;
                }

                if (laenge < UnterDieserLaengeNurVersal
                    && fenster.All(char.IsLetter)
                    && !IstVersal(geputzt, start, laenge))
                {
                    continue;
                }

                if (schon.Add(name))
                {
                    gefunden.Add(name);
                }

                // Über den Treffer hinweg, nicht mitten hinein: „MIG/MAG" darf
                // nicht ein zweites Mal als sein eigener Bestandteil zählen.
                start += laenge - 1;
                break;
            }
        }

        return gefunden;
    }

    /// <summary>Ein Zeichen, das kein Wort fortsetzt — oder der Rand des Textes.</summary>
    /// <remarks>
    /// Die Wortgrenze ist der Unterschied zwischen „SPS" in „SPS-Programmierung"
    /// und „sps" in einem beliebigen Wortinneren. Ein Bindestrich ist eine
    /// Grenze, ein Buchstabe ist keine.
    /// </remarks>
    private static bool BeginntEinWort(string text, int stelle) =>
        stelle == 0 || !char.IsLetterOrDigit(text[stelle - 1]);

    private static bool EndetEinWort(string text, int stelle) =>
        stelle >= text.Length || !char.IsLetterOrDigit(text[stelle]);

    /// <summary>Steht dieses Stück im Text ohne einen einzigen Kleinbuchstaben da?</summary>
    private static bool IstVersal(string text, int start, int laenge)
    {
        for (var stelle = start; stelle < start + laenge; stelle++)
        {
            if (char.IsLower(text[stelle]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Jede Folge von Leerraum wird ein einzelnes Leerzeichen.
    /// </summary>
    /// <remarks>
    /// Ohne das fände „Erste Hilfe" sich selbst nicht, sobald ein Zeilenumbruch
    /// dazwischen steht — und in einem gesetzten Dokument steht er irgendwann
    /// zwischen jedem Wortpaar.
    /// </remarks>
    private static string Verdichte(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var bau = new StringBuilder(text.Length);
        var leerraum = false;

        foreach (var zeichen in text)
        {
            if (char.IsWhiteSpace(zeichen))
            {
                leerraum = true;
                continue;
            }

            if (leerraum && bau.Length > 0)
            {
                bau.Append(' ');
            }

            leerraum = false;
            bau.Append(zeichen);
        }

        return bau.ToString();
    }

    private static FrozenDictionary<string, string> Baue()
    {
        var tabelle = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, schreibweisen) in Wortschatz.Schreibweisen)
        {
            // Der kanonische Name zeigt auf sich selbst: wer „Kubernetes"
            // schreibt, hat Kubernetes geschrieben.
            tabelle[name.ToLowerInvariant()] = name;

            foreach (var schreibweise in schreibweisen)
            {
                tabelle[schreibweise.ToLowerInvariant()] = name;
            }
        }

        return tabelle.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
