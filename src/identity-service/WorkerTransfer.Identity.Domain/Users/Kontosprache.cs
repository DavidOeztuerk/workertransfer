namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>The language a person is written to in.</summary>
/// <remarks>
/// <strong>A column on the account, deliberately, and not the request's
/// <c>Accept-Language</c>.</strong> The mails this service sends are not
/// answers to a request. The deletion confirmation goes out when the last of
/// eight services has acknowledged, which may be days later and is triggered by
/// a dispatcher, not by a browser; the news mail is the same. At that moment
/// there is no request and no header — only the row. Reading the header would
/// mean every asynchronous mail falls back to a default, and the person who
/// chose French would be written to in German at exactly the moments that
/// matter most.
/// <para>
/// The outbox stays free of it (ADR-0025): it holds a user id and a kind and
/// nothing else, and the language is read from the account when the mail is
/// written. A language in the outbox would be a copy that goes stale the moment
/// somebody changes it.
/// </para>
/// <para>
/// Three languages, and the set is closed. An open set would mean a mail in a
/// language for which no text exists — which is a mail nobody can read, sent in
/// the belief that it was localised.
/// </para>
/// <para>
/// <strong>Called <c>Kontosprache</c> and not <c>Sprache</c>, and that is not
/// a style choice.</strong> <c>Sprache</c> is a parser-combinator package that
/// arrives here transitively and owns the top-level namespace of that name, so
/// the plain name is a <c>CS0118</c> at every use site. The longer name also
/// says the thing that matters: it is the language of the <em>account</em>, not
/// of the request.
/// </para>
/// </remarks>
public enum Kontosprache
{
    /// <summary>German. The source the other two are translated from.</summary>
    De,

    /// <summary>English.</summary>
    En,

    /// <summary>French.</summary>
    Fr
}

/// <summary>Reads a language out of what a caller offers.</summary>
public static class Sprachwahl
{
    /// <summary>What a person gets when nothing else is known.</summary>
    public const Kontosprache Vorgabe = Kontosprache.De;

    /// <summary>Turns a tag such as <c>fr-CA</c> into a language we have.</summary>
    /// <remarks>
    /// Only the primary subtag is read: <c>fr-CA</c> and <c>fr</c> get the same
    /// texts, because there is one French catalogue and pretending otherwise
    /// would be a promise we do not keep.
    /// <para>
    /// Anything unknown becomes <see cref="Vorgabe"/> rather than an error. A
    /// registration must not fail because a browser announced Icelandic.
    /// </para>
    /// </remarks>
    /// <param name="etikett">A BCP 47 tag, or anything at all.</param>
    /// <returns>The language to use.</returns>
    public static Kontosprache Aus(string? etikett)
    {
        if (string.IsNullOrWhiteSpace(etikett))
        {
            return Vorgabe;
        }

        var haupt = etikett.Trim().Split('-', ';', ',')[0].ToLowerInvariant();

        return haupt switch
        {
            "de" => Kontosprache.De,
            "en" => Kontosprache.En,
            "fr" => Kontosprache.Fr,
            _ => Vorgabe
        };
    }

    /// <summary>Reads the first acceptable language out of an Accept-Language header.</summary>
    /// <remarks>
    /// Quality values are ignored on purpose. The header arrives in preference
    /// order in every browser, and weighing <c>q=</c> here would be a parser we
    /// would have to keep correct for a gain nobody can perceive. What matters
    /// is that a header naming French before German yields French — and a
    /// header naming a language we do not have skips past it instead of falling
    /// straight to the default.
    /// </remarks>
    /// <param name="kopf">The raw header value, or <c>null</c>.</param>
    /// <returns>The first language we have, else <see cref="Vorgabe"/>.</returns>
    public static Kontosprache AusKopf(string? kopf)
    {
        if (string.IsNullOrWhiteSpace(kopf))
        {
            return Vorgabe;
        }

        foreach (var teil in kopf.Split(','))
        {
            var haupt = teil.Split(';')[0].Trim();
            if (haupt.Length == 0 || haupt == "*")
            {
                continue;
            }

            var gewaehlt = Aus(haupt);

            // `Aus` answers with the default for anything unknown, so an
            // unknown first entry would end the loop with German while French
            // stood second. Only an entry we actually recognise may stop it.
            if (Erkannt(haupt))
            {
                return gewaehlt;
            }
        }

        return Vorgabe;
    }

    /// <summary>The tag as it is stored and sent.</summary>
    /// <param name="sprache">The language.</param>
    /// <returns>Two lowercase letters.</returns>
    public static string Etikett(Kontosprache sprache) => sprache switch
    {
        Kontosprache.En => "en",
        Kontosprache.Fr => "fr",
        _ => "de"
    };

    /// <summary>Do we have texts for this tag?</summary>
    /// <remarks>
    /// Separate from <see cref="Aus"/> on purpose. <c>Aus</c> answers with the
    /// default for anything it does not know, which is right when a browser
    /// announces Icelandic and wrong when a person picked something: a choice
    /// that quietly becomes German is a choice that did not happen, and nobody
    /// is told.
    /// </remarks>
    /// <param name="etikett">A BCP 47 tag.</param>
    /// <returns><c>true</c> if a catalogue exists for it.</returns>
    public static bool Kennen(string? etikett) =>
        !string.IsNullOrWhiteSpace(etikett) && Erkannt(etikett);

    private static bool Erkannt(string etikett)
    {
        var haupt = etikett.Split('-')[0].ToLowerInvariant();
        return haupt is "de" or "en" or "fr";
    }
}
