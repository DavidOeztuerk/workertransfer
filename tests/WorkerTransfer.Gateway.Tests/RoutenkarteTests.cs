using System.Text.Json;
using FluentAssertions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// The route map covers every route — and without a running stack.
/// </summary>
/// <remarks>
/// <c>docs/routenkarte.yml</c> records for every endpoint what it answers in
/// the three capacities. It is driven by <c>scripts/routenkarte.sh</c> against
/// the running stack — that is the check of the <em>answers</em>.
/// <para>
/// This suite checks something else and needs no stack for it: <strong>that the
/// map is complete.</strong> Without it the map would be correct exactly until
/// somebody adds a route — and the day that happens is the day nobody thinks of
/// it. A map that silently becomes incomplete is worse than none: it looks like
/// a promise.
/// </para>
/// </remarks>
public sealed class RoutenkarteTests
{
    private sealed class Eintrag
    {
        public string Methode { get; init; } = "";
        public string Pfad { get; init; } = "";
        public int Ohne { get; init; }
        public int Person { get; init; }

        /// <summary>
        /// Acting for a company, role <c>member</c>.
        /// </summary>
        /// <remarks>
        /// The fourth column, since PBI-2. It is the only one that CAN deviate
        /// from <see cref="Firma"/> at all without the tenant changing — and so
        /// the only one that makes "admin versus member" checkable in the first
        /// place. Before that the word stood only in a table column.
        /// </remarks>
        public int Mitglied { get; init; }

        /// <summary>Acting for a company, role <c>admin</c>.</summary>
        public int Firma { get; init; }

        /// <summary>
        /// The body <c>scripts/routenkarte.sh</c> sends along.
        /// </summary>
        /// <remarks>
        /// Set almost nowhere: most endpoints do not look into it at all, and
        /// the run otherwise sends `{}`. Where an endpoint DOES check the body,
        /// the map would without this field measure the answer to an empty
        /// request and write it down as the endpoint's — `PUT
        /// /account/language`, for instance, answered 422 instead of 200, and
        /// the map would take that for the truth.
        /// </remarks>
        public string? Rumpf { get; init; }
    }

    private sealed class Gruppe
    {
        public string Name { get; init; } = "";
        public string Warum { get; init; } = "";
        public List<Eintrag> Eintraege { get; init; } = [];
    }

    private static List<Gruppe> Karte()
    {
        var path = Path.Combine(Postgres_Ersatz.Repowurzel(), "docs", "routenkarte.yml");
        // The map writes in lower case (`pfad`, `ohne`), the classes in upper.
        var leser = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var wurzel = leser.Deserialize<Dictionary<string, List<Gruppe>>>(File.ReadAllText(path));

        return wurzel["gruppen"];
    }

    private static List<Eintrag> Eintraege() =>
        [.. Karte().SelectMany(g => g.Eintraege)];

    private static IReadOnlyList<string> Routen()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var wurzel = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;

        return [.. wurzel.GetProperty("Routes").EnumerateArray()
            .Select(r => r.GetProperty("UpstreamPathTemplate").GetString()!)];
    }

    /// <summary>
    /// Does a path match a route template? <c>/auth/{rest}</c> covers
    /// <c>/auth/login</c>, but not <c>/auth</c> itself.
    /// </summary>
    /// <remarks>
    /// The query string is cut off before the comparison. Ocelot routes by the
    /// PATH; an entry may name <c>/jobs?page=2</c> all the same, because the map
    /// records what a particular REQUEST answers — and a second page is a
    /// different request from the first.
    /// </remarks>
    private static bool Passt(string template, string path)
    {
        var ohneAbfrage = path.Split('?')[0];

        var teileV = template.Trim('/').Split('/');
        var teileP = ohneAbfrage.Trim('/').Split('/');

        // The last placeholder is greedy: `/auth/{rest}` also catches
        // `/auth/company/{id}`. That is exactly how Ocelot behaves.
        var gierig = teileV.Length > 0
                     && teileV[^1].StartsWith('{')
                     && teileV[^1].EndsWith('}');

        if (gierig ? teileP.Length < teileV.Length : teileP.Length != teileV.Length)
        {
            return false;
        }

        for (var i = 0; i < teileV.Length; i++)
        {
            if (teileV[i].StartsWith('{'))
            {
                continue;
            }

            if (!string.Equals(teileV[i], teileP[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// THE TEST THAT TURNS A NEW ROUTE RED.
    /// </summary>
    /// <remarks>
    /// Every line in <c>ocelot.json</c> has to be touched by at least one entry
    /// of the map. Whoever adds a route and does not extend the map has an
    /// endpoint nobody wrote down an expected answer for — and it is then
    /// noticed first by somebody who finds it.
    /// </remarks>
    [Fact]
    public void Every_route_has_at_least_one_entry()
    {
        var entries = Eintraege();

        foreach (var route in Routen())
        {
            entries.Should().Contain(
                e => Passt(route, e.Pfad),
                $"die Route '{route}' steht in ocelot.json, aber in keiner Zeile "
                + "von docs/routenkarte.yml — es ist also nicht aufgeschrieben, "
                + "was sie antworten soll");
        }
    }

    /// <summary>
    /// The reverse direction: no entry describes a path that does not exist.
    /// </summary>
    /// <remarks>
    /// Except for the three service-to-service doors — those stand in there on
    /// purpose, precisely BECAUSE they have no route, and their expected answer
    /// is 404. Without that exception the promise "not reachable from outside"
    /// could not be written down.
    /// </remarks>
    [Fact]
    public void No_entry_describes_a_path_without_a_route()
    {
        var routen = Routen();

        foreach (var entry in Eintraege())
        {
            var getroffen = routen.Any(r => Passt(r, entry.Pfad));

            if (getroffen)
            {
                continue;
            }

            entry.Ohne.Should().Be(
                404,
                $"'{entry.Pfad}' hat keine Route — dann ist 404 die einzige "
                + "Antwort, die dort stehen darf");
            entry.Person.Should().Be(404);
            entry.Mitglied.Should().Be(404);
            entry.Firma.Should().Be(404);
        }
    }

    /// <summary>
    /// Every row names a status code in ALL FOUR columns.
    /// </summary>
    /// <remarks>
    /// The test that turns a forgotten fourth column red. YAML answers a missing
    /// field with the type's default value — here <c>0</c>, and the script would
    /// then measure "expected 0" against a real answer. Without this suite the
    /// map would be complete exactly until somebody adds a row and overlooks the
    /// new column.
    /// </remarks>
    [Fact]
    public void Every_row_names_all_four_columns()
    {
        foreach (var entry in Eintraege())
        {
            foreach (var (column, value) in new (string, int)[]
                     {
                         ("ohne", entry.Ohne),
                         ("person", entry.Person),
                         ("mitglied", entry.Mitglied),
                         ("firma", entry.Firma)
                     })
            {
                value.Should().BeInRange(
                    100, 599,
                    $"'{entry.Methode} {entry.Pfad}' nennt fuer '{column}' keinen "
                    + "Statuscode — eine fehlende Spalte liest YAML als 0");
            }
        }
    }

    /// <summary>Every group says why its rows look the way they do.</summary>
    /// <remarks>
    /// A number without a reason is a measurement, not a promise — and at the
    /// next rebuild somebody changes it because it is in the way.
    /// </remarks>
    [Fact]
    public void Every_group_carries_a_reason()
    {
        foreach (var group in Karte())
        {
            group.Name.Should().NotBeNullOrWhiteSpace();
            group.Warum.Trim().Length.Should().BeGreaterThan(
                80, $"die Gruppe '{group.Name}' sagt nicht, warum ihre Zeilen so aussehen");
            group.Eintraege.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Without a token the answer is never 200 — except where it is written
    /// down.
    /// </summary>
    /// <remarks>
    /// The sharpest line of the map, and that is why it stands as a test of its
    /// own. Four endpoints answer anonymously with 2xx, and each for a reason
    /// that stands in its group: <c>/auth/session</c> answers the question "am I
    /// signed in?", <c>/auth/resend-verification</c> answers identically for
    /// known and unknown addresses, the careers page is public — and
    /// <c>GET /jobs</c> is public since the same decision: the careers page
    /// fetches its jobs over exactly this endpoint, and as long as it gave a
    /// 401, the page was only half public.
    /// <para>
    /// Whoever adds a fourth has to touch this list — and in doing so notice
    /// that they are making something public.
    /// </para>
    /// </remarks>
    [Fact]
    public void Anonymously_only_what_is_expressly_open_answers()
    {
        string[] offen =
        [
            "/auth/session",
            "/auth/resend-verification",
            "/companies/by-slug/gibt-es-nicht",
            "/companies/00000000-0000-0000-0000-000000000001/profile",
            "/jobs"
        ];

        foreach (var entry in Eintraege().Where(e => e.Ohne is >= 200 and < 300))
        {
            // Without the query string: `/jobs?page=2` is the same ENDPOINT as
            // `/jobs`, and this list counts endpoints. A parameter makes nothing
            // public that was not already.
            offen.Should().Contain(
                entry.Pfad.Split('?')[0],
                $"'{entry.Methode} {entry.Pfad}' antwortet ohne Token mit "
                + $"{entry.Ohne} — das ist ein oeffentlicher Endpunkt, und "
                + "dass er einer ist, gehoert ausdruecklich hingeschrieben");
        }
    }

    /// <summary>
    /// The map really describes different cases and not the same one four times
    /// over.
    /// </summary>
    /// <remarks>
    /// If `person` and `firma` were the same everywhere, the company column
    /// would be decoration and ADR-0017 would not be checked in this map.
    /// Measured, there are more than twenty rows today in which they come apart.
    /// </remarks>
    [Fact]
    public void The_middle_column_really_differs()
    {
        var entries = Eintraege();

        entries.Count(e => e.Person != e.Firma).Should().BeGreaterThan(
            15, "sonst beschreibt die Karte zweimal denselben Fall");
    }

    /// <summary>
    /// And the fourth differs from the third — that is PBI-2.
    /// </summary>
    /// <remarks>
    /// <para>If `mitglied` and `firma` were the same everywhere, that would
    /// mean: not a single route distinguishes `admin` from `member`, and that
    /// was exactly the state before PBI-2 — in the whole tree there stood ZERO
    /// <c>RequirePermission</c> outside identity-service. The navigation hid
    /// company entries; the server answered 403 only where somebody had thought
    /// of it. Hiding is not access control.</para>
    ///
    /// <para><strong>Five and not eight, and the difference is
    /// instructive.</strong> Eight routes are protected; only five become
    /// visible here. The three from identity-service
    /// (<c>/companies/{id}/invitations</c>, <c>…/invitations/{id}</c>,
    /// <c>…/members/{id}</c>) name the company in the PATH, and the id there
    /// DOES NOT EXIST — even the owner account is no administrator in that
    /// invented company and gets a 403. Both columns therefore show the same
    /// number for two different reasons. That the policy there really does
    /// distinguish between the roles is evidenced by
    /// <c>UnternehmensreiseTests</c> against a company that exists.</para>
    ///
    /// <para>The number is DELIBERATELY the exact one: whoever protects a sixth
    /// route this way or opens one of the five again should come past here and
    /// write it down. A "greater than zero" would let a silently withdrawn check
    /// pass.</para>
    /// </remarks>
    [Fact]
    public void The_fourth_column_distinguishes_admin_from_member()
    {
        var entries = Eintraege();

        var abweichend = entries
            .Where(e => e.Mitglied != e.Firma)
            .ToList();

        abweichend.Select(e => $"{e.Methode} {e.Pfad}").Should().BeEquivalentTo(
            [
                "POST /jobs/00000000-0000-0000-0000-000000000001/publish",
                "POST /jobs/00000000-0000-0000-0000-000000000001/close",
                "PUT /companies/me/profile",
                "POST /transfers/00000000-0000-0000-0000-000000000001/offer",
                "POST /transfers/00000000-0000-0000-0000-000000000001/complete"
            ],
            "genau diese fuenf zeigen den Unterschied in der Karte; wer eine "
            + "sechste so schuetzt oder eine oeffnet, aendert diese Liste bewusst mit");

        abweichend.Should().OnlyContain(
            e => e.Mitglied == 403,
            "ein Mitglied bekommt 403 — eine Aussage ueber den Aufrufer, keine "
            + "ueber die Sache");
    }

    /// <summary>
    /// The three administration routes of identity-service answer 403 in ALL
    /// three signed-in columns.
    /// </summary>
    /// <remarks>
    /// The other half of the suite above. Here the difference between the
    /// columns does not show, because the id in the path does not exist — and
    /// that is the promise that comes out of it: the policy answers 403
    /// regardless of whether the company exists, so it gives nothing away. A 404
    /// beside it would be the disclosure "this company exists".
    /// </remarks>
    [Fact]
    public void The_company_administration_reveals_the_company_in_no_column()
    {
        string[] verwaltung =
        [
            "POST /companies/00000000-0000-0000-0000-000000000001/invitations",
            "DELETE /companies/00000000-0000-0000-0000-000000000001/invitations"
            + "/00000000-0000-0000-0000-000000000002",
            "DELETE /companies/00000000-0000-0000-0000-000000000001/members"
            + "/00000000-0000-0000-0000-000000000002"
        ];

        foreach (var name in verwaltung)
        {
            var entry = Eintraege().Single(e => $"{e.Methode} {e.Pfad}" == name);

            entry.Ohne.Should().Be(401);
            entry.Person.Should().Be(403, $"'{name}' haengt an einer Richtlinie");
            entry.Mitglied.Should().Be(403);
            entry.Firma.Should().Be(403);
        }
    }
}

/// <summary>Finds the repository root without inheriting from a Testcontainers class.</summary>
internal static class Postgres_Ersatz
{
    public static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "WorkerTransfer.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
               ?? throw new InvalidOperationException("Repowurzel nicht gefunden.");
    }
}
