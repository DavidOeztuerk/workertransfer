using System.Reflection;
using FluentAssertions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// What goes out to the model is a <strong>closed</strong> set (ADR-0024 §3).
/// </summary>
/// <remarks>
/// <para>ADR-0024 promises: "A data class and no free dictionary, because a
/// dictionary silently carries one more key at the next feature. <strong>A test
/// pins the field set.</strong>" And CLAUDE.md says the same for both sides:
/// "Tests pin both field sets."</para>
///
/// <para><strong>That test did not exist.</strong> What did exist checks the
/// finished prompt <em>string</em> for a subject id and an <c>@</c>
/// (<c>Der_Entwurf_traegt_nichts_ueber_die_Person_hinaus</c>). That is something
/// else and considerably weaker: a new field <c>Arbeitgeber</c> on
/// <see cref="DraftSituation"/>, filled with "Muster GmbH", contains neither a
/// Guid nor an <c>@</c> and would get through. So exactly the silent extension
/// the record was chosen against in the first place would have stayed
/// unchecked.</para>
///
/// <para>The check is made on the <em>type</em> and against a written-out set,
/// not against a forbidden list. A forbidden list answers "is this name
/// sensitive?" — a question one answers afresh, and possibly wrongly, at the
/// next field. A closed set instead demands that whoever adds a field writes it
/// down here: the same movement, but one that stands out.</para>
///
/// <para><strong>Two sets, not one.</strong> The two drafts are mirror images
/// and must never grow together: the profile context helps a person write about
/// themselves, the advert context helps a company write about its own job. A
/// shared type would be the place where "invent nothing about the person" would
/// one day apply to an advert — or worse, the other way round.</para>
/// </remarks>
public class EntwurfsgrenzeTests
{
    /// <summary>
    /// The profile context carries four fields — and the person wrote every one
    /// of them themselves.
    /// </summary>
    /// <remarks>
    /// No name, no email address, no <c>SubjectId</c>, no employer, no resume,
    /// no application, no market status. <c>Wish</c> is the only line the
    /// caller contributes at that moment.
    /// </remarks>
    [Fact]
    public void The_profile_context_carries_exactly_four_fields()
    {
        Felder(typeof(DraftSituation)).Should().BeEquivalentTo(
            ["Headline", "Text", "Skills", "Wish", "Prompt"],
            "ADR-0024 §3 nennt diese Menge, und wer sie erweitert, "
            + "schickt etwas Neues über eine Person hinaus");
    }

    /// <summary>
    /// The advert context carries five — and none of them says WHO is
    /// advertising.
    /// </summary>
    /// <remarks>
    /// No tenant and no company name: the advert is to be phrased, not the
    /// company described. A company name in the prompt would be the invitation
    /// to let the model say something about the employer that nobody checked.
    /// </remarks>
    [Fact]
    public void The_advert_context_carries_exactly_five_fields()
    {
        Felder(typeof(AdvertDraft)).Should().BeEquivalentTo(
            ["Title", "Description", "Skills", "Location", "Wish", "Prompt"],
            "ADR-0024 §3, Gegenstück — hier fehlt bewusst der Mandant");
    }

    /// <summary>
    /// And the two stay separate: no shared base type, no shared interface.
    /// </summary>
    /// <remarks>
    /// The merging never begins with a shared prompt but with "they both have
    /// skills and a wish, after all". After that an <c>if</c> stands in the
    /// rules, and that is the place where a rule applies to the wrong side.
    /// </remarks>
    [Fact]
    public void The_two_drafts_share_no_type()
    {
        var profile = typeof(DraftSituation);
        var anzeige = typeof(AdvertDraft);

        profile.BaseType.Should().Be<object>("kein gemeinsamer Basistyp");
        anzeige.BaseType.Should().Be<object>("kein gemeinsamer Basistyp");

        // Not "no interface at all": a record brings IEquatable<T> with it by
        // itself, and that is a different one per type. What is checked is the
        // intersection — the promise is that the two SHARE nothing.
        profile.GetInterfaces().Intersect(anzeige.GetInterfaces())
            .Should().BeEmpty("die beiden Entwuerfe teilen keine Schnittstelle");
    }

    /// <summary>The public instance properties of a record.</summary>
    /// <remarks>
    /// Static ones stay out: <c>Rules</c> is the system prompt and no field
    /// anybody fills.
    /// <para>
    /// <c>Prompt</c>, by contrast, counts along although it is derived — it is
    /// the string that really goes out. A new input field therefore shows up
    /// twice: as a field and, as soon as somebody wires it in, in what gets
    /// sent. Both are meant to stand out.
    /// </para>
    /// </remarks>
    private static string[] Felder(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .Select(e => e.Name)];
}
