using System.Text.Json;
using FluentAssertions;
using YamlDotNet.Serialization;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// The route map covers every route — and without a running stack.
/// </summary>
/// <remarks>
/// <c>docs/routenkarte.yml</c> records for every endpoint what it answers in
/// the four capacities. It is driven by <c>scripts/routenkarte.sh</c> against
/// the running stack — that is the check of the <em>answers</em>.
/// <para>
/// This suite checks something else and needs no stack for it: <strong>that the
/// map is complete.</strong> Without it the map would be correct exactly until
/// somebody adds a route — and the day that happens is the day nobody thinks of
/// it. A map that silently becomes incomplete is worse than none: it looks like
/// a promise.
/// </para>
/// </remarks>
public sealed class RouteMapTests
{
    /// <summary>One row of the map.</summary>
    /// <remarks>
    /// <strong>The aliases are the file's keys, and the file is not ours to
    /// rename.</strong> <c>docs/routenkarte.yml</c> is a German document, and
    /// <c>scripts/routenkarte.sh</c> reads the same keys (<c>pfad</c>,
    /// <c>ohne</c>, <c>firma</c>…). The classes are English; the keys stay what
    /// the document and the script say. Without an alias YAML would look for
    /// <c>path</c>, find nothing, and fill every column with <c>0</c> — which
    /// <see cref="Every_row_names_all_four_columns"/> would then report as a
    /// broken map rather than a broken binding.
    /// </remarks>
    private sealed class Entry
    {
        [YamlMember(Alias = "methode")]
        public string Method { get; init; } = "";

        [YamlMember(Alias = "pfad")]
        public string Path { get; init; } = "";

        /// <summary>No token.</summary>
        [YamlMember(Alias = "ohne")]
        public int Anonymous { get; init; }

        /// <summary>Signed in, acting as a person, no company.</summary>
        [YamlMember(Alias = "person")]
        public int Person { get; init; }

        /// <summary>
        /// Acting for a company, role <c>member</c>.
        /// </summary>
        /// <remarks>
        /// The fourth column, since PBI-2. It is the only one that CAN deviate
        /// from <see cref="Admin"/> at all without the tenant changing — and so
        /// the only one that makes "admin versus member" checkable in the first
        /// place. Before that the word stood only in a table column.
        /// </remarks>
        [YamlMember(Alias = "mitglied")]
        public int Member { get; init; }

        /// <summary>Acting for a company, role <c>admin</c>.</summary>
        [YamlMember(Alias = "firma")]
        public int Admin { get; init; }

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
        [YamlMember(Alias = "rumpf")]
        public string? Body { get; init; }
    }

    private sealed class Group
    {
        [YamlMember(Alias = "name")]
        public string Name { get; init; } = "";

        [YamlMember(Alias = "warum")]
        public string Reason { get; init; } = "";

        [YamlMember(Alias = "eintraege")]
        public List<Entry> Entries { get; init; } = [];
    }

    private static List<Group> Map()
    {
        var path = System.IO.Path.Combine(RepositoryRoot.Find(), "docs", "routenkarte.yml");
        var reader = new DeserializerBuilder().Build();
        var root = reader.Deserialize<Dictionary<string, List<Group>>>(File.ReadAllText(path));

        return root["gruppen"];
    }

    private static List<Entry> Entries() =>
        [.. Map().SelectMany(group => group.Entries)];

    private static IReadOnlyList<string> Routes()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var root = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;

        return [.. root.GetProperty("Routes").EnumerateArray()
            .Select(route => route.GetProperty("UpstreamPathTemplate").GetString()!)];
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
    private static bool Matches(string template, string path)
    {
        var withoutQuery = path.Split('?')[0];

        var templateParts = template.Trim('/').Split('/');
        var pathParts = withoutQuery.Trim('/').Split('/');

        // The last placeholder is greedy: `/auth/{rest}` also catches
        // `/auth/company/{id}`. That is exactly how Ocelot behaves.
        var greedy = templateParts.Length > 0
                     && templateParts[^1].StartsWith('{')
                     && templateParts[^1].EndsWith('}');

        if (greedy ? pathParts.Length < templateParts.Length : pathParts.Length != templateParts.Length)
        {
            return false;
        }

        for (var i = 0; i < templateParts.Length; i++)
        {
            if (templateParts[i].StartsWith('{'))
            {
                continue;
            }

            if (!string.Equals(templateParts[i], pathParts[i], StringComparison.Ordinal))
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
        var entries = Entries();

        foreach (var route in Routes())
        {
            entries.Should().Contain(
                row => Matches(route, row.Path),
                $"the route '{route}' stands in ocelot.json but in no row of "
                + "docs/routenkarte.yml — so nobody wrote down what it is meant "
                + "to answer");
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
        var routes = Routes();

        foreach (var entry in Entries())
        {
            var hit = routes.Any(candidate => Matches(candidate, entry.Path));

            if (hit)
            {
                continue;
            }

            entry.Anonymous.Should().Be(
                404,
                $"'{entry.Path}' has no route — then 404 is the only answer "
                + "that may stand there");
            entry.Person.Should().Be(404);
            entry.Member.Should().Be(404);
            entry.Admin.Should().Be(404);
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
        foreach (var entry in Entries())
        {
            foreach (var (column, value) in new (string, int)[]
                     {
                         ("ohne", entry.Anonymous),
                         ("person", entry.Person),
                         ("mitglied", entry.Member),
                         ("firma", entry.Admin)
                     })
            {
                value.Should().BeInRange(
                    100, 599,
                    $"'{entry.Method} {entry.Path}' names no status code for "
                    + $"'{column}' — YAML reads a missing column as 0");
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
        foreach (var group in Map())
        {
            group.Name.Should().NotBeNullOrWhiteSpace();
            group.Reason.Trim().Length.Should().BeGreaterThan(
                80, $"the group '{group.Name}' does not say why its rows look the way they do");
            group.Entries.Should().NotBeEmpty();
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
        string[] open =
        [
            "/auth/session",
            "/auth/resend-verification",
            "/companies/by-slug/gibt-es-nicht",
            "/companies/00000000-0000-0000-0000-000000000001/profile",
            "/jobs"
        ];

        foreach (var entry in Entries().Where(row => row.Anonymous is >= 200 and < 300))
        {
            // Without the query string: `/jobs?page=2` is the same ENDPOINT as
            // `/jobs`, and this list counts endpoints. A parameter makes nothing
            // public that was not already.
            open.Should().Contain(
                entry.Path.Split('?')[0],
                $"'{entry.Method} {entry.Path}' answers without a token with "
                + $"{entry.Anonymous} — that is a public endpoint, and that it is one "
                + "belongs expressly written down");
        }
    }

    /// <summary>
    /// The map really describes different cases and not the same one four times
    /// over.
    /// </summary>
    /// <remarks>
    /// If `person` and `firma` (<see cref="Entry.Admin"/>) were the same everywhere, the company column
    /// would be decoration and ADR-0017 would not be checked in this map.
    /// Measured, there are more than twenty rows today in which they come apart.
    /// </remarks>
    [Fact]
    public void The_middle_column_really_differs()
    {
        var entries = Entries();

        entries.Count(row => row.Person != row.Admin).Should().BeGreaterThan(
            15, "otherwise the map describes the same case twice");
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
    /// <c>CompanyJourneyTests</c> against a company that exists.</para>
    ///
    /// <para>The number is DELIBERATELY the exact one: whoever protects a sixth
    /// route this way or opens one of the five again should come past here and
    /// write it down. A "greater than zero" would let a silently withdrawn check
    /// pass.</para>
    /// </remarks>
    [Fact]
    public void The_fourth_column_distinguishes_admin_from_member()
    {
        var entries = Entries();

        var differing = entries
            .Where(row => row.Member != row.Admin)
            .ToList();

        differing.Select(row => $"{row.Method} {row.Path}").Should().BeEquivalentTo(
            [
                "POST /jobs/00000000-0000-0000-0000-000000000001/publish",
                "POST /jobs/00000000-0000-0000-0000-000000000001/close",
                "PUT /companies/me/profile",
                "POST /transfers/00000000-0000-0000-0000-000000000001/offer",
                "POST /transfers/00000000-0000-0000-0000-000000000001/complete"
            ],
            "exactly these five show the difference in the map; whoever protects "
            + "a sixth this way or opens one changes this list deliberately");

        differing.Should().OnlyContain(
            row => row.Member == 403,
            "a member gets 403 — a statement about the caller, not about the "
            + "matter");
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
        string[] administration =
        [
            "POST /companies/00000000-0000-0000-0000-000000000001/invitations",
            "DELETE /companies/00000000-0000-0000-0000-000000000001/invitations"
            + "/00000000-0000-0000-0000-000000000002",
            "DELETE /companies/00000000-0000-0000-0000-000000000001/members"
            + "/00000000-0000-0000-0000-000000000002"
        ];

        foreach (var name in administration)
        {
            var entry = Entries().Single(row => $"{row.Method} {row.Path}" == name);

            entry.Anonymous.Should().Be(401);
            entry.Person.Should().Be(403, $"'{name}' hangs on a policy");
            entry.Member.Should().Be(403);
            entry.Admin.Should().Be(403);
        }
    }
}
