using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>The place table and the distance, without service and without database.</summary>
/// <remarks>
/// The place names and phrases below stay German: they are what companies write
/// into the location field, and German matching is what is tested.
/// </remarks>
public class PlaceLookupTests
{
    /// <summary>A place name becomes a point — read generously.</summary>
    /// <remarks>
    /// What companies write into the field is free text: "Berlin, Mitte",
    /// "Hamburg / Remote", "Raum Stuttgart", "MÜNCHEN". A table that hits only
    /// the exact lower case would miss the majority of adverts and let them drop
    /// silently out of every radius search.
    /// </remarks>
    [Theory]
    [InlineData("Berlin")]
    [InlineData("berlin")]
    [InlineData("BERLIN")]
    [InlineData("  Berlin  ")]
    [InlineData("Berlin, Mitte")]
    [InlineData("Berlin / Remote")]
    [InlineData("Berlin (hybrid)")]
    [InlineData("Raum Berlin")]
    public void A_place_name_is_read_generously(string written)
    {
        var point = PlaceLookup.Find(written);

        point.Should().NotBeNull();
        point!.Value.Latitude.Should().BeApproximately(52.52, 0.01);
        point.Value.Longitude.Should().BeApproximately(13.405, 0.01);
    }

    /// <summary>Umlauts hit in both spellings.</summary>
    [Theory]
    [InlineData("München")]
    [InlineData("Muenchen")]
    [InlineData("MÜNCHEN")]
    public void Umlauts_hit_in_both_spellings(string written) =>
        PlaceLookup.Find(written).Should().NotBeNull();

    /// <summary>The longer name wins.</summary>
    /// <remarks>
    /// "Frankfurt am Main" contains "Frankfurt". If the shorter hit won, the
    /// enumeration order of a dictionary would decide where a job lies — and
    /// that is no promise.
    /// </remarks>
    [Fact]
    public void The_longer_name_wins()
    {
        var longer = PlaceLookup.Find("Frankfurt am Main");
        var shorter = PlaceLookup.Find("Frankfurt");

        longer.Should().NotBeNull();
        longer.Should().Be(shorter);
    }

    /// <summary>The embedded table is there at all.</summary>
    /// <remarks>
    /// Without this probe a missing <c>PostalCodes.txt</c> could not be told
    /// apart from "no place known" — the radius search would then find nothing
    /// and not complain. The numbers are rough and bounded from below, so that
    /// a new data release does not turn them red.
    /// </remarks>
    [Fact]
    public void The_table_is_shipped()
    {
        var (postalCodes, places) = PlaceLookup.Size();

        postalCodes.Should().BeGreaterThan(10000);
        places.Should().BeGreaterThan(10000);
    }

    /// <summary>A postal code hits more precisely than a name.</summary>
    /// <remarks>
    /// The reason the advert has a field of its own for it: there are several
    /// places called "Walldorf", but only one "69190".
    /// </remarks>
    [Theory]
    [InlineData("10115", 52.53, 13.38)]
    [InlineData("80331", 48.13, 11.57)]
    [InlineData("69190", 49.30, 8.64)]
    [InlineData("D-10115", 52.53, 13.38)]
    [InlineData(" 10115 ", 52.53, 13.38)]
    public void A_postal_code_hits(string postalCode, double latitude, double longitude)
    {
        var point = PlaceLookup.Find(postalCode, "");

        point.Should().NotBeNull();
        point!.Value.Latitude.Should().BeApproximately(latitude, 0.05);
        point.Value.Longitude.Should().BeApproximately(longitude, 0.05);
    }

    /// <summary>A bulk-recipient postal code names no place.</summary>
    /// <remarks>
    /// <c>10875</c> has thirty-six rows with company names in the raw data set,
    /// with coordinates from Stuttgart via Berlin to Bautzen. An average of them
    /// would lie in the middle of nowhere — the row is thrown out when the table
    /// is generated, and the advert counts as "place unknown".
    /// </remarks>
    [Fact]
    public void A_bulk_recipient_postal_code_is_no_place() =>
        PlaceLookup.Find("10875", "").Should().BeNull();

    /// <summary>
    /// A four-digit code belongs to Austria AND Switzerland — the place name
    /// decides.
    /// </summary>
    /// <remarks>
    /// Without a place name such a code stays unknown. Grabbing one of the two
    /// candidates would be a coin toss between two countries, and the miss would
    /// lie six hundred kilometres off.
    /// </remarks>
    [Fact]
    public void An_ambiguous_code_needs_the_place_name()
    {
        var vienna = PlaceLookup.Find("1010", "Wien");
        var without = PlaceLookup.Find("1010", "");

        vienna.Should().NotBeNull();
        vienna!.Value.Latitude.Should().BeApproximately(48.21, 0.1);
        without.Should().BeNull();
    }

    /// <summary>A postal code in free text is read too.</summary>
    /// <remarks>
    /// Adverts from before the field of its own write "10115 Berlin" into the
    /// location line. Denying them the place because of it would be a gap
    /// without a reason.
    /// </remarks>
    [Fact]
    public void A_postal_code_in_free_text_is_read()
    {
        var point = PlaceLookup.Find(null, "10115 Berlin");

        point.Should().NotBeNull();
        point!.Value.Latitude.Should().BeApproximately(52.53, 0.05);
    }

    /// <summary>The large place wins against its namesakes — the ambiguous one does not.</summary>
    /// <remarks>
    /// There are five places called "Husum" in Germany. The one in North
    /// Frisia has 20841 inhabitants, the next 2336 — whoever writes "Husum"
    /// means the first. "Neustadt", by contrast, is truly ambiguous, and there
    /// "I do not know" is the right answer: a miss would lie hundreds of
    /// kilometres off and would strike nobody.
    /// </remarks>
    [Fact]
    public void The_large_place_wins_the_ambiguous_one_does_not()
    {
        var husum = PlaceLookup.Find("Husum");

        husum.Should().NotBeNull();
        husum!.Value.Latitude.Should().BeApproximately(54.49, 0.1);

        PlaceLookup.Find("Neustadt").Should().BeNull();
    }

    /// <summary>Foreign-language name forms of large cities hit too.</summary>
    /// <remarks>
    /// The interface speaks three languages (ADR-0031), and an English advert
    /// says "Munich".
    /// </remarks>
    [Theory]
    [InlineData("Munich", 48.14, 11.57)]
    [InlineData("Cologne", 50.93, 6.95)]
    [InlineData("Vienna", 48.21, 16.37)]
    [InlineData("Geneva", 46.20, 6.15)]
    public void Foreign_language_names_hit(string name, double latitude, double longitude)
    {
        var point = PlaceLookup.Find(name);

        point.Should().NotBeNull();
        point!.Value.Latitude.Should().BeApproximately(latitude, 0.1);
        point.Value.Longitude.Should().BeApproximately(longitude, 0.1);
    }

    /// <summary>No place is picked out of prose.</summary>
    /// <remarks>
    /// <strong>Measured, not made up.</strong> The place table knows twenty-six
    /// thousand names, and many of them are ordinary words: <em>Hof</em> is a
    /// town in Bavaria with 46000 inhabitants, and so are <em>Essen</em>,
    /// <em>Halle</em>, <em>Lage</em>, <em>Brand</em> and <em>Bad</em>. As long
    /// as every word sequence of a location was looked up, "Auf dem Hof meiner
    /// Oma" lay in Upper Franconia — and nobody would have noticed, because an
    /// advert in a wrong place looks just like one in a right place.
    /// <para>
    /// With two to three words the splitting stays allowed: "Raum Stuttgart" is
    /// a reading, "Auf dem Hof meiner Oma" a guessing game.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Auf dem Hof meiner Oma")]
    [InlineData("Wir sitzen mitten in der Lage")]
    [InlineData("Zum Essen gehen wir raus")]
    public void No_place_is_picked_out_of_prose(string sentence) =>
        PlaceLookup.Find(sentence).Should().BeNull();

    /// <summary>Two to three words are still split.</summary>
    [Theory]
    [InlineData("Raum Stuttgart")]
    [InlineData("Grossraum Muenchen")]
    [InlineData("Standort: Hamburg")]
    [InlineData("Berlin oder remote")]
    public void Short_values_are_still_split(string value) =>
        PlaceLookup.Find(value).Should().NotBeNull();

    /// <summary>What the table does not know is not guessed.</summary>
    /// <remarks>
    /// <c>null</c> means "we know nothing about it" and never "lies at 0,0" — a
    /// zero point in the Gulf of Guinea would make every unknown advert one that
    /// is 5000 kilometres away, instead of one about which nobody said anything.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Atlantis")]
    [InlineData("Elbonien")]
    public void The_unknown_is_not_guessed(string written) =>
        PlaceLookup.Find(written).Should().BeNull();

    /// <summary>The distance is right against known routes.</summary>
    /// <remarks>
    /// Three routes instead of one, and one of them east-west: a formula that
    /// does not shorten the longitude by the cosine of the latitude is right for
    /// north-south routes and 1.6 times too large east-west — with a single
    /// probe that would not show.
    /// </remarks>
    [Theory]
    [InlineData("Berlin", "Hamburg", 255)]
    [InlineData("Berlin", "Potsdam", 27)]
    [InlineData("Köln", "Dresden", 470)]
    public void The_distance_is_right(string from, string to, double expectedKm)
    {
        var a = PlaceLookup.Find(from)!.Value;
        var b = PlaceLookup.Find(to)!.Value;

        // Ten per cent tolerance: the numbers are town centres, and no radius
        // search hangs on a kilometre.
        PlaceLookup.DistanceKm(a, b).Should().BeApproximately(expectedKm, expectedKm * 0.1);
    }

    /// <summary>The same city is zero kilometres away.</summary>
    [Fact]
    public void The_same_city_is_zero_kilometres_away()
    {
        var berlin = PlaceLookup.Find("Berlin")!.Value;

        PlaceLookup.DistanceKm(berlin, berlin).Should().BeApproximately(0, 0.001);
    }

    /// <summary>Something incomplete yields no filter, no half result.</summary>
    [Theory]
    [InlineData(null, 13.4, 25.0)]
    [InlineData(52.5, null, 25.0)]
    [InlineData(52.5, 13.4, null)]
    [InlineData(200.0, 13.4, 25.0)]
    [InlineData(52.5, 400.0, 25.0)]
    public void Something_incomplete_yields_no_radius(
        double? latitude, double? longitude, double? radius) =>
        SearchRadius.From(latitude, longitude, radius).Should().BeNull();

    /// <summary>A nonsensical radius is clamped, not discarded.</summary>
    /// <remarks>
    /// Whoever sends 100000 means "far". Making no filter at all of it would be
    /// the more surprising answer — the list would then silently show
    /// everything.
    /// </remarks>
    [Theory]
    [InlineData(0.5, SearchRadius.MinRadiusKm)]
    [InlineData(100000.0, SearchRadius.MaxRadiusKm)]
    [InlineData(-40.0, SearchRadius.MinRadiusKm)]
    [InlineData(50.0, 50.0)]
    public void A_nonsensical_radius_is_clamped(double wanted, double expected) =>
        SearchRadius.From(52.5, 13.4, wanted)!.RadiusKm.Should().Be(expected);
}

/// <summary>The radius search over <c>GET /jobs</c>.</summary>
/// <remarks>
/// <strong>The point comes from the browser and is imprecise on purpose</strong>
/// — two decimal places, a good kilometre. This suite does not check the
/// rounding (that happens in the browser) but what depends on it: that an
/// advert about whose place NOTHING is known is not treated as "far away" but
/// COUNTED and reported. A filter that silently leaves out delivers a result
/// that looks complete and is not.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RadiusSearchTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "withdrawal-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => new ProbeDrafter()));
                // Publishing demands an `admin` since PBI-2. This suite measures
                // the radius and not the role — so the probe answers like an
                // owner.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    // Berlin, rounded the way the browser sends it.
    private const string Berlin = "lat=52.52&lon=13.41";

    private HttpClient AsCompany()
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Company(Guid.CreateVersion7(), Guid.CreateVersion7()));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Creates a published advert and returns its id.</summary>
    /// <remarks>
    /// The <paramref name="keyword"/> stands in the description and is sent
    /// along as <c>q</c> with every probe. Without this fence a probe would also
    /// see the adverts of the other suites of this collection — they share a
    /// database, and a count over foreign rows is no statement.
    /// </remarks>
    private async Task<Guid> Create(
        string location, string keyword, string remote = "hybrid", string skill = "C#")
    {
        // ONE browser for both steps. Two calls of `AsCompany()` would be two
        // different companies, and the service answers publishing a foreign
        // advert with 403 — the advert would stay a draft, and every probe would
        // count zero hits without anything being broken.
        var browser = AsCompany();

        var created = await browser.PostAsJsonAsync("/jobs", new
        {
            title = $"Position {Guid.NewGuid():N}",
            description = $"We build distributed systems. {keyword}",
            location,
            remote_mode = remote,
            employment_type = "full_time",
            skills = new[] { skill }
        });

        created.EnsureSuccessStatusCode();

        var id = (await Json(created)).GetProperty("id").GetGuid();

        (await browser.PostAsync($"/jobs/{id}/publish", null)).EnsureSuccessStatusCode();

        return id;
    }

    private async Task<JsonElement> Search(string query) =>
        await Json(await _service.CreateClient().GetAsync($"/jobs?{query}"));

    private static IEnumerable<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

    /// <summary>Near is in, far is out — and the radius decides.</summary>
    /// <remarks>
    /// Potsdam lies twenty-seven kilometres from Berlin. It stands here instead
    /// of Hamburg because it lies EXACTLY between the two radii checked: a search
    /// that does not read the radius at all would be the same for Hamburg
    /// (255 km) in both cases and would look green.
    /// </remarks>
    [Fact]
    public async Task The_radius_decides_who_is_in()
    {
        var keyword = $"radius{Guid.NewGuid():N}";
        var near = await Create("Berlin", keyword);
        var middle = await Create("Potsdam", keyword);
        var far = await Create("Hamburg", keyword);

        var narrow = await Search($"q={keyword}&{Berlin}&radius_km=25");
        var wide = await Search($"q={keyword}&{Berlin}&radius_km=50");
        var veryWide = await Search($"q={keyword}&{Berlin}&radius_km=300");

        Ids(narrow).Should().BeEquivalentTo([near]);
        Ids(wide).Should().BeEquivalentTo([near, middle]);
        Ids(veryWide).Should().BeEquivalentTo([near, middle, far]);
    }

    /// <summary>Fully remote is always included.</summary>
    /// <remarks>
    /// From where such a job can be reached is no question of distance. Hiding
    /// it because of a location filter would hit exactly the adverts that are
    /// the most interesting for somebody outside the conurbations.
    /// </remarks>
    [Fact]
    public async Task Fully_remote_is_always_included()
    {
        var keyword = $"remote{Guid.NewGuid():N}";
        var farAwayButRemote = await Create("Wien", keyword, remote: "full");
        await Create("Wien", keyword, remote: "hybrid");

        var narrow = await Search($"q={keyword}&{Berlin}&radius_km=25");

        Ids(narrow).Should().BeEquivalentTo([farAwayButRemote]);
    }

    /// <summary>An unknown place drops out — and is COUNTED.</summary>
    /// <remarks>
    /// The actual reason for this suite. A radius search can say nothing about
    /// an advert whose place the table does not know. Leaving it out silently
    /// would be the lie by omission from ADR-0022 §3: the result would look
    /// complete. The number goes out as <c>omitted</c>, so the interface can
    /// name it.
    /// </remarks>
    [Fact]
    public async Task An_unknown_place_drops_out_and_is_counted()
    {
        var keyword = $"unknown{Guid.NewGuid():N}";
        var known = await Create("Berlin", keyword);
        await Create("Auf dem Hof meiner Oma", keyword);
        await Create("", keyword);

        var page = await Search($"q={keyword}&{Berlin}&radius_km=25");

        Ids(page).Should().BeEquivalentTo([known]);
        page.GetProperty("total_items").GetInt32().Should().Be(1);
        page.GetProperty("omitted").GetInt32().Should().Be(2);
    }

    /// <summary>Without a radius search <c>omitted</c> does not stand in the answer at all.</summary>
    /// <remarks>
    /// A <c>0</c> would claim the question had been asked and answered with
    /// "none". But it was not — there was no location filter, so nothing left
    /// anything out.
    /// </remarks>
    [Fact]
    public async Task Without_a_radius_search_there_is_no_omitted()
    {
        var keyword = $"without{Guid.NewGuid():N}";
        await Create("Auf dem Hof meiner Oma", keyword);

        var page = await Search($"q={keyword}");

        page.GetProperty("total_items").GetInt32().Should().Be(1);
        page.TryGetProperty("omitted", out _).Should().BeFalse();
    }

    /// <summary>An incomplete radius does not filter.</summary>
    /// <remarks>
    /// A radius without a point is no question one can answer. Answering it as
    /// if the point stood at 0,0 would be worse than not asking it — the list
    /// would be empty, and nobody would know why.
    /// </remarks>
    [Theory]
    [InlineData("radius_km=25")]
    [InlineData("lat=52.52&radius_km=25")]
    [InlineData("lat=abc&lon=xyz&radius_km=25")]
    public async Task An_incomplete_radius_does_not_filter(string radius)
    {
        var keyword = $"half{Guid.NewGuid():N}";
        await Create("Hamburg", keyword);

        var page = await Search($"q={keyword}&{radius}");

        page.GetProperty("total_items").GetInt32().Should().Be(1);
        page.TryGetProperty("omitted", out _).Should().BeFalse();
    }

    /// <summary>Radius and skills work together, not against each other.</summary>
    /// <remarks>
    /// Both filters run in memory, and both count the filtered set. If one ran
    /// over the result of the other, a total would stand over a list that does
    /// not exist like that.
    /// </remarks>
    [Fact]
    public async Task Radius_and_skills_work_together()
    {
        var keyword = $"both{Guid.NewGuid():N}";
        var hit = await Create("Berlin", keyword, skill: "Rust");
        await Create("Berlin", keyword, skill: "Cobol");
        await Create("Hamburg", keyword, skill: "Rust");

        var page = await Search($"q={keyword}&skill=Rust&{Berlin}&radius_km=25");

        Ids(page).Should().BeEquivalentTo([hit]);
        page.GetProperty("total_items").GetInt32().Should().Be(1);
        page.GetProperty("total_pages").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// WITHOUT coordinates the location is the centre — and then no text filter
    /// any more.
    /// </summary>
    /// <remarks>
    /// Whoever types "Leipzig" and chooses "200 km" means "around Leipzig" and
    /// needs neither GPS nor the permission for it.
    /// <para>
    /// The second part is the more important one: both at once — radius AND
    /// word filter — would exclude exactly the neighbouring places for whose
    /// sake somebody chooses a radius. An advert in Berlin lies 150 km from
    /// Leipzig and carries the word "Leipzig" nowhere; it MUST be included.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Without_coordinates_the_location_is_the_centre()
    {
        var keyword = $"centre{Guid.NewGuid():N}";
        var leipzig = await Create("Leipzig", keyword);
        var berlin = await Create("Berlin", keyword);
        await Create("Wien", keyword);

        var narrow = await Search($"q={keyword}&location=Leipzig&radius_km=25");
        var wide = await Search($"q={keyword}&location=Leipzig&radius_km=200");

        Ids(narrow).Should().BeEquivalentTo([leipzig]);
        Ids(wide).Should().BeEquivalentTo([leipzig, berlin]);
    }

    /// <summary>Without a radius the location stays a text filter.</summary>
    /// <remarks>
    /// The counter-probe to the previous one: the location changes its meaning
    /// only when a distance stands beside it. Without one it goes on searching
    /// for the word.
    /// </remarks>
    [Fact]
    public async Task Without_a_radius_the_location_stays_a_text_filter()
    {
        var keyword = $"textfilter{Guid.NewGuid():N}";
        var leipzig = await Create("Leipzig", keyword);
        await Create("Berlin", keyword);

        var page = await Search($"q={keyword}&location=Leipzig");

        Ids(page).Should().BeEquivalentTo([leipzig]);
    }

    /// <summary>The reverse direction: which place lies at this point?</summary>
    /// <remarks>
    /// So that the searching person SEES from where the measuring is done — a
    /// radius around an invisible point is an imposition.
    /// <para>
    /// The last case is the one that was once wrong: under bulk-recipient postal
    /// codes the raw data lists companies as "place names", and this answer read
    /// "Adam Opel GmbH" for Berlin and "Amtsgericht München" for Munich. They
    /// are sorted out when the table is generated.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("lat=52.52&lon=13.39", "Berlin")]
    [InlineData("lat=53.55&lon=9.99", "Hamburg")]
    [InlineData("lat=48.14&lon=11.57", "München")]
    [InlineData("lat=47.37&lon=8.54", "Zürich")]
    public async Task The_reverse_direction_names_the_place(string point, string expected)
    {
        var response = await Json(await _service.CreateClient().GetAsync($"/jobs/place?{point}"));

        response.GetProperty("location").GetString().Should().Be(expected);
    }

    /// <summary>Outside, it stays silent instead of inventing a place.</summary>
    /// <remarks>
    /// Whoever does not stand in DE, AT or CH gets <c>null</c>. Returning the
    /// nearest German place would be a claim about the whereabouts nobody made —
    /// and the location field would then carry a place the person never was.
    /// </remarks>
    [Theory]
    [InlineData("lat=10.0&lon=10.0")]
    [InlineData("lat=abc&lon=xyz")]
    [InlineData("")]
    public async Task Outside_the_reverse_direction_names_nothing(string point)
    {
        var response = await Json(await _service.CreateClient().GetAsync($"/jobs/place?{point}"));

        response.GetProperty("location").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>The radius search pages like any other.</summary>
    [Fact]
    public async Task The_radius_search_pages()
    {
        var keyword = $"paging{Guid.NewGuid():N}";

        for (var i = 0; i < 5; i++)
        {
            await Create("Berlin", keyword);
        }

        var first = await Search($"q={keyword}&{Berlin}&radius_km=25&page_size=2");
        var second = await Search($"q={keyword}&{Berlin}&radius_km=25&page_size=2&page=2");

        first.GetProperty("total_items").GetInt32().Should().Be(5);
        first.GetProperty("total_pages").GetInt32().Should().Be(3);
        first.GetProperty("items").GetArrayLength().Should().Be(2);
        Ids(first).Should().NotIntersectWith(Ids(second));
    }
}
