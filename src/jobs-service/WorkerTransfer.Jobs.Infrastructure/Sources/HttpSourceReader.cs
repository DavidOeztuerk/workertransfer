using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;

namespace WorkerTransfer.Jobs.Infrastructure.Sources;

/// <summary>Reads Greenhouse, Lever, Personio and a company's own feed (ADR-0052).</summary>
/// <remarks>
/// <para><strong>Only the official, documented reads</strong> — measured on
/// 17.10.2026 against public boards. Nothing is logged of what a board says;
/// a failure is a short word in the source's outcome.</para>
///
/// <para>What an ATS does not say in a fixed field (skills, salary) stays
/// empty rather than guessed from prose.</para>
/// </remarks>
public sealed class HttpSourceReader(
    IHttpClientFactory factory,
    ICompanySite site,
    IOptions<SourceSettings> settings,
    ILogger<HttpSourceReader> log) : ISourceReader
{
    /// <summary>The client for the ATS hosts.</summary>
    public const string ClientName = "ats";

    private readonly SourceSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<SourceRead> ReadAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            return source.Kind switch
            {
                SourceKind.Greenhouse => await Json(
                    $"{_settings.Greenhouse.TrimEnd('/')}/v1/boards/{source.Key}/jobs?content=true", Greenhouse, cancellationToken),
                SourceKind.Lever => await Json(
                    $"{_settings.Lever.TrimEnd('/')}/v0/postings/{source.Key}?mode=json", Lever, cancellationToken),
                SourceKind.Personio => await PersonioAsync(source.Key, cancellationToken),
                SourceKind.Feed => await FeedAsync(source, cancellationToken),
                _ => new SourceRead.Failed("unknown_kind")
            };
        }
        catch (Exception error) when (error is JsonException or System.Xml.XmlException or FormatException
                                          or InvalidOperationException or KeyNotFoundException)
        {
            // The shape was not the documented one. Its kind, never its content.
            log.LogWarning("A source answered in an unreadable shape ({Kind})", error.GetType().Name);
            return new SourceRead.Failed("unreadable");
        }
    }

    private async Task<SourceRead> Json(
        string address, Func<JsonElement, IReadOnlyList<ExternalAdvert>> read, CancellationToken cancellationToken)
    {
        var (text, failure) = await GetAsync(address, cancellationToken);

        if (text is null)
        {
            return new SourceRead.Failed(failure!);
        }

        using var document = JsonDocument.Parse(text);
        return new SourceRead.Adverts(read(document.RootElement));
    }

    private async Task<SourceRead> PersonioAsync(string key, CancellationToken cancellationToken)
    {
        var host = $"{key}.{_settings.Personio.Trim('.')}";
        var (text, failure) = await GetAsync($"https://{host}/xml?language=de", cancellationToken);

        if (text is null)
        {
            return new SourceRead.Failed(failure!);
        }

        var xml = XDocument.Parse(text);

        return new SourceRead.Adverts([
            .. xml.Descendants("position").Select(position =>
            {
                var id = Value(position, "id");
                var descriptions = position.Element("jobDescriptions")?.Elements("jobDescription")
                    .Select(part => $"{Value(part, "name")}\n{AdvertText.FromHtml(Value(part, "value"))}".Trim())
                    .Where(part => part.Length > 0)
                    .ToList() ?? [];
                var url = $"https://{host}/job/{id}";

                // An empty description is common in Personio's public feed;
                // then the advert says where it is written in full.
                var description = descriptions.Count > 0
                    ? string.Join("\n\n", descriptions)
                    : string.Join("\n", new[] { Value(position, "department"), Value(position, "office"), url }
                        .Where(line => line.Length > 0));

                return new ExternalAdvert(
                    id,
                    Value(position, "name"),
                    description,
                    Value(position, "office"),
                    RemoteMode.None,
                    Value(position, "schedule") == "part-time"
                        ? EmploymentKind.PartTime
                        : Value(position, "employmentType") switch
                        {
                            "intern" or "trainee" or "working_student" => EmploymentKind.Internship,
                            "freelance" => EmploymentKind.Contract,
                            _ => EmploymentKind.FullTime
                        },
                    url);
            })
        ]);
    }

    private async Task<SourceRead> FeedAsync(JobSource source, CancellationToken cancellationToken)
    {
        var text = await site.PageAsync(source.Key, cancellationToken);

        if (text is null)
        {
            return new SourceRead.Failed("unreachable");
        }

        using var document = JsonDocument.Parse(text);
        return new SourceRead.Adverts(Feed(document.RootElement, source.Key));
    }

    private static IReadOnlyList<ExternalAdvert> Greenhouse(JsonElement root) =>
    [
        .. root.GetProperty("jobs").EnumerateArray().Select(job =>
        {
            var workplace = Metadata(job, "workplace");
            var location = job.TryGetProperty("location", out var place) ? String(place, "name") : string.Empty;

            return new ExternalAdvert(
                job.GetProperty("id").ToString(),
                String(job, "title"),
                AdvertText.FromHtml(String(job, "content")),
                location,
                Remote($"{workplace} {location}"),
                Employment(Metadata(job, "employment")),
                String(job, "absolute_url"));
        })
    ];

    private static IReadOnlyList<ExternalAdvert> Lever(JsonElement root) =>
    [
        .. root.EnumerateArray().Select(posting =>
        {
            var categories = posting.TryGetProperty("categories", out var found) ? found : default;
            var parts = new List<string> { String(posting, "descriptionPlain") };

            if (posting.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Array)
            {
                parts.AddRange(lists.EnumerateArray()
                    .Select(list => $"{String(list, "text")}\n{AdvertText.FromHtml(String(list, "content"))}".Trim()));
            }

            parts.Add(String(posting, "additionalPlain"));

            return new ExternalAdvert(
                String(posting, "id"),
                String(posting, "text"),
                string.Join("\n\n", parts.Where(part => part.Length > 0)),
                categories.ValueKind == JsonValueKind.Object ? String(categories, "location") : string.Empty,
                Remote(String(posting, "workplaceType")),
                Employment(categories.ValueKind == JsonValueKind.Object ? String(categories, "commitment") : string.Empty),
                String(posting, "hostedUrl"));
        })
    ];

    /// <summary>A JSON list of schema.org JobPosting — the shape companies keep for Google for Jobs.</summary>
    private static IReadOnlyList<ExternalAdvert> Feed(JsonElement root, string feed)
    {
        IEnumerable<JsonElement> items = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object when root.TryGetProperty("@graph", out var graph) => graph.EnumerateArray(),
            JsonValueKind.Object => [root],
            _ => []
        };

        return
        [
            .. items
                .Where(item => item.ValueKind == JsonValueKind.Object
                               && (!item.TryGetProperty("@type", out var type) || type.ToString().Contains("JobPosting", StringComparison.Ordinal)))
                .Select(item =>
                {
                    var url = String(item, "url");
                    var id = item.TryGetProperty("identifier", out var identifier)
                        ? identifier.ValueKind == JsonValueKind.Object ? String(identifier, "value") : identifier.ToString()
                        : string.Empty;

                    return new ExternalAdvert(
                        id.Length > 0 ? id : url,
                        String(item, "title"),
                        AdvertText.FromHtml(String(item, "description")),
                        FeedLocation(item),
                        String(item, "jobLocationType").Equals("TELECOMMUTE", StringComparison.OrdinalIgnoreCase)
                            ? RemoteMode.Full
                            : RemoteMode.None,
                        Employment(item.TryGetProperty("employmentType", out var kind) ? kind.ToString() : string.Empty),
                        url.Length > 0 ? url : feed);
                })
        ];
    }

    private static string FeedLocation(JsonElement item)
    {
        if (!item.TryGetProperty("jobLocation", out var place))
        {
            return string.Empty;
        }

        var first = place.ValueKind == JsonValueKind.Array && place.GetArrayLength() > 0 ? place[0] : place;

        return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("address", out var address)
                                                       && address.ValueKind == JsonValueKind.Object
            ? String(address, "addressLocality")
            : string.Empty;
    }

    private static RemoteMode Remote(string text) =>
        text.Contains("remote", StringComparison.OrdinalIgnoreCase) ? RemoteMode.Full
        : text.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ? RemoteMode.Hybrid
        : RemoteMode.None;

    private static EmploymentKind Employment(string text) =>
        text.Contains("part", StringComparison.OrdinalIgnoreCase) ? EmploymentKind.PartTime
        : text.Contains("intern", StringComparison.OrdinalIgnoreCase) ? EmploymentKind.Internship
        : text.Contains("contract", StringComparison.OrdinalIgnoreCase)
          || text.Contains("freelance", StringComparison.OrdinalIgnoreCase)
          || text.Contains("temporary", StringComparison.OrdinalIgnoreCase) ? EmploymentKind.Contract
        : EmploymentKind.FullTime;

    private static string Metadata(JsonElement job, string name) =>
        job.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array
            ? string.Join(' ', metadata.EnumerateArray()
                .Where(entry => String(entry, "name").Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.TryGetProperty("value", out var value) ? value.ToString() : string.Empty))
            : string.Empty;

    private static string String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Value(XElement element, string name) => element.Element(name)?.Value.Trim() ?? string.Empty;

    /// <summary>The body, or the word for why there is none.</summary>
    private async Task<(string? Text, string? Failure)> GetAsync(string address, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            log.LogWarning("A source did not answer ({Kind})", error.GetType().Name);
            return (null, "unreachable");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, "not_found");
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("A source answered {Status}", (int)response.StatusCode);
                return (null, "unreachable");
            }

            var text = await Limited.ReadAsync(response, _settings.MaxBytes, cancellationToken);
            return text is null ? (null, "too_large") : (text, null);
        }
    }
}

/// <summary>Reads an answer up to a size, and not a byte further.</summary>
public static class Limited
{
    /// <summary>The body as text, or <c>null</c> when it is larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<string?> ReadAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Content.Headers.ContentLength is { } announced && announced > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = charset is { Length: > 0 }
            ? System.Text.Encoding.GetEncoding(charset.Trim('"'))
            : System.Text.Encoding.UTF8;

        return encoding.GetString(buffer.ToArray());
    }
}
