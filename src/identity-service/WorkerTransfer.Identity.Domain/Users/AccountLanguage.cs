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
/// <strong>Called <c>AccountLanguage</c> and not <c>Language</c>, and that is not
/// a style choice.</strong> Under its German name the plain word was
/// <c>Sprache</c> — a parser-combinator package that arrives here transitively
/// and owns the top-level namespace of that name, so the plain name was a
/// <c>CS0118</c> at every use site. The longer name also says the thing that
/// matters: it is the language of the <em>account</em>, not of the request.
/// </para>
/// </remarks>
public enum AccountLanguage
{
    /// <summary>German. The source the other two are translated from.</summary>
    De,

    /// <summary>English.</summary>
    En,

    /// <summary>French.</summary>
    Fr
}

/// <summary>Reads a language out of what a caller offers.</summary>
public static class LanguageChoice
{
    /// <summary>What a person gets when nothing else is known.</summary>
    public const AccountLanguage Default = AccountLanguage.De;

    /// <summary>Turns a tag such as <c>fr-CA</c> into a language we have.</summary>
    /// <remarks>
    /// Only the primary subtag is read: <c>fr-CA</c> and <c>fr</c> get the same
    /// texts, because there is one French catalogue and pretending otherwise
    /// would be a promise we do not keep.
    /// <para>
    /// Anything unknown becomes <see cref="Default"/> rather than an error. A
    /// registration must not fail because a browser announced Icelandic.
    /// </para>
    /// </remarks>
    /// <param name="label">A BCP 47 tag, or anything at all.</param>
    /// <returns>The language to use.</returns>
    public static AccountLanguage From(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Default;
        }

        var primary = label.Trim().Split('-', ';', ',')[0].ToLowerInvariant();

        return primary switch
        {
            "de" => AccountLanguage.De,
            "en" => AccountLanguage.En,
            "fr" => AccountLanguage.Fr,
            _ => Default
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
    /// <param name="header">The raw header value, or <c>null</c>.</param>
    /// <returns>The first language we have, else <see cref="Default"/>.</returns>
    public static AccountLanguage FromHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return Default;
        }

        foreach (var part in header.Split(','))
        {
            var primary = part.Split(';')[0].Trim();
            if (primary.Length == 0 || primary == "*")
            {
                continue;
            }

            var chosen = From(primary);

            // `Aus` answers with the default for anything unknown, so an
            // unknown first entry would end the loop with German while French
            // stood second. Only an entry we actually recognise may stop it.
            if (Recognised(primary))
            {
                return chosen;
            }
        }

        return Default;
    }

    /// <summary>The tag as it is stored and sent.</summary>
    /// <param name="language">The language.</param>
    /// <returns>Two lowercase letters.</returns>
    public static string Label(AccountLanguage language) => language switch
    {
        AccountLanguage.En => "en",
        AccountLanguage.Fr => "fr",
        _ => "de"
    };

    /// <summary>Do we have texts for this tag?</summary>
    /// <remarks>
    /// Separate from <see cref="From"/> on purpose. <c>From</c> answers with the
    /// default for anything it does not know, which is right when a browser
    /// announces Icelandic and wrong when a person picked something: a choice
    /// that quietly becomes German is a choice that did not happen, and nobody
    /// is told.
    /// </remarks>
    /// <param name="label">A BCP 47 tag.</param>
    /// <returns><c>true</c> if a catalogue exists for it.</returns>
    public static bool Known(string? label) =>
        !string.IsNullOrWhiteSpace(label) && Recognised(label);

    private static bool Recognised(string label)
    {
        var primary = label.Split('-')[0].ToLowerInvariant();
        return primary is "de" or "en" or "fr";
    }
}
