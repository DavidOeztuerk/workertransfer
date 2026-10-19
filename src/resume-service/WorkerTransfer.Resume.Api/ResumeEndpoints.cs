using WorkerTransfer.Resume.Api.Authorization;
using WorkerTransfer.ServiceDefaults.Roles;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.DocumentStore;
using WorkerTransfer.Resume.Application.Documents;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Application.Requests;
using WorkerTransfer.Resume.Application.Resumes;
using WorkerTransfer.Resume.Contracts;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Resume.Domain.Requests;
using WorkerTransfer.Resume.Domain.Resumes;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Api;

/// <summary><c>/resumes/*</c> — writing one, asking for one, answering.</summary>
/// <remarks>
/// There is no public switch anywhere here, and none may be added. A profile is
/// a notice board; a résumé names real employers with dates — exactly what a
/// current employer must not see. A company asks, the person answers, and the
/// release covers that one company.
/// </remarks>
public static class ResumeEndpoints
{
    /// <summary>Maps the endpoints of the résumé and its documents.</summary>
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var resumes = app.MapGroup("/resumes");

        // A silent ledger is neither a yes nor a no. Answering 404 would say
        // the person is not there; answering with the résumé would hand out
        // what nobody confirmed may be handed out. 503 says the one true
        // thing: this service cannot answer right now.
        //
        // Here as one filter rather than a catch in each route, because the
        // route that forgets it is the one that answers wrongly — and it looks
        // like a working route until the ledger is down.
        resumes.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (ConsentGateSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    invocation.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Written;
            }
        });

        resumes.MapPut("/me", async (
            StoreResumeV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            // A missing field is NOT the same as an empty list.
            //
            // `{}` used to run `null.Select(...)` and came back as 500 (D2).
            // Simply reading it as empty would be worse: then a request in which
            // somebody forgot a field would mean "delete my whole résumé".
            // Whoever wants to empty it sends `[]` explicitly.
            if (body.Positions is null || body.Education is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "positions and education are required");
                return;
            }

            try
            {
                // Whose résumé this is comes from the verified token, never
                // from the body: a subject id on the wire would be a way to
                // write into somebody else's.
                var resume = await mediator.Send(
                    new SaveResumeCommand(
                        caller.Subject, ToPositions(body), ToEducation(body)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(ToContract(resume), cancellationToken);
            }
            catch (ResumeRule rule)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", rule.Message);
            }
        });

        resumes.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var resume = await mediator.Send(
                new MyResumeQuery(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                resume is null ? null : ToContract(resume), cancellationToken);
        });

        // The person's own list, and the one place where "was granted" and
        // "holds now" visibly come apart: a withdrawn release still shows
        // GRANTED, with `active: false` beside it.
        resumes.MapGet("/me/requests", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var mine = await mediator.Send(
                new MyRequestsQuery(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(view => ToContract(view.Request, view.Active)),
                cancellationToken);
        });

        resumes.MapPost("/requests/{id:guid}/grant", (
            Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
            AnswerAsync(id, grant: true, mediator, principal, context, cancellationToken));

        resumes.MapPost("/requests/{id:guid}/decline", (
            Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
            AnswerAsync(id, grant: false, mediator, principal, context, cancellationToken));

        // Taking a release back. No notification goes out for this: pushing it
        // at the company would turn withdrawing into a confrontation, and the
        // whole point of reading the ledger fresh is that it costs the person
        // nothing. The company notices the same way it would anyway — the next
        // read comes up empty.
        resumes.MapPost("/requests/{id:guid}/revoke", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var result = await mediator.Send(
                new RevokeAccessCommand(id, caller.Subject), cancellationToken);

            await WriteResultAsync(context, result, cancellationToken);
        });

        // A company asks. It needs the PROFILE release to do so — never the
        // existence of a résumé: "has already written one" is a fact about the
        // person that nobody should be able to probe for.
        resumes.MapPost("/{subjectId:guid}/requests", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                // A statement about the caller, which leaks nothing: only a
                // company can ask for a résumé, and a private person knows they
                // are not one.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "only a company may ask for a resume");
                return;
            }

            var result = await mediator.Send(
                new RequestResumeCommand(
                    new SubjectId(subjectId), company.Tenant, caller.Subject),
                cancellationToken);

            await WriteResultAsync(context, result, cancellationToken, StatusCodes.Status201Created);
        })
        // A first contact to a person who did not come forward: at least a
        // `recruiter` (ADR-0048), resolved from identity-service's membership
        // table per request. The check stands BEFORE the body and the person
        // are looked at.
        .RequireAuthorization(AdminPermissionExtensions.Policy(ResumePermissions.Request));

        // The company's own list. `active` is null here: the company already has
        // the answer in the form of the data it does or does not get, and a
        // field here could be polled without ever reading a résumé.
        resumes.MapGet("/requests", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var requests = await mediator.Send(
                new CompanyRequestsQuery(company.Tenant), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                requests.Select(requestRecord => ToContract(requestRecord, active: null)),
                cancellationToken);
        });

        resumes.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var result = await mediator.Send(
                new VisibleResumeQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            switch (result)
            {
                case ResumeResult.Found found:
                    await context.Response.WriteAsJsonAsync(
                        ToContract(found.Resume), cancellationToken);
                    return;

                default:
                    // Withheld and non-existent answer alike, and they must stay
                    // alike: a different answer would say whether this person
                    // has written a résumé, which is a fact about them.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status404NotFound,
                        "Request failed", "no resume");
                    return;
            }
        });

        // ---------------------------------------------------------------
        // THE DOCUMENTS (ADR-0035)
        //
        // They hang on the résumé and not on the application: a person uploads
        // their reference once and then encloses it with twenty applications.
        // Storing it per application would mean holding the same file twenty
        // times — and missing nineteen of them on deletion.
        // ---------------------------------------------------------------

        // THE COMPANY'S VIEW of the folder. It hangs on the ledger and on
        // nothing else — no application table is asked (ADR-0020).
        resumes.MapGet("/{subjectId:guid}/documents", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var all = await mediator.Send(
                new VisibleDocumentsQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            // An empty list when nothing is released — and the same empty list
            // when there is nothing. A difference would betray that this person
            // HAS documents, and that is a fact about them.
            await context.Response.WriteAsJsonAsync(
                all.Select(ToContract).ToArray(), cancellationToken);
        });

        resumes.MapGet("/{subjectId:guid}/documents/{id:guid}/content", async (
            Guid subjectId,
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var found = await mediator.Send(
                new VisibleDocumentContentQuery(
                    new SubjectId(subjectId), company.Tenant, id),
                cancellationToken);

            if (found is not { } hit)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            context.Response.ContentType = hit.Document.ContentType;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + hit.Document.Id.ToString("N")
                + ContentTypeDetection.Extension(hit.Document.ContentType) + "\"";

            await context.Response.Body.WriteAsync(hit.Content, cancellationToken);
        });

        var documents = app.MapGroup("/resumes/me/documents");

        documents.MapPost("/", async (
            HttpRequest request,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (!request.HasFormContentType)
            {
                // 415 AND NOT 400: the request is well-formed, only its media
                // type is not served here. 400 would mean "I could not read your
                // body" and sends the caller searching for a typo that does not
                // exist.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status415UnsupportedMediaType,
                    "Request failed", "expected multipart/form-data");
                return;
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: file");
                return;
            }

            // Check before reading, not after: pulling a file completely into
            // memory first and refusing it then is exactly the way a caller with
            // an address fills the working memory.
            if (file.Length > DocumentRecord.MaxSizeBytes)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "Request failed",
                    $"document must not exceed {DocumentRecord.MaxSizeBytes} bytes");
                return;
            }

            using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, cancellationToken);

            var result = await mediator.Send(
                new AddDocumentCommand(
                    caller.Subject,
                    form["name"].ToString() is { Length: > 0 } named
                        ? named
                        : file.FileName,
                    KindFromWord(form["kind"].ToString()),
                    stream.ToArray()),
                cancellationToken);

            switch (result)
            {
                case DocumentResult.Done done:
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        ToContract(done.Document), cancellationToken);
                    return;

                case DocumentResult.TooMany:
                    // web/src/features/person/api/resume.ts (UPLOAD_CASES) matches this sentence.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed",
                        $"at most {DocumentRecord.MaxDocuments} documents");
                    return;

                case DocumentResult.Rejected rejected:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", rejected.Reason);
                    return;
            }
        });

        documents.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var all = await mediator.Send(
                new MyDocumentsQuery(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                all.Select(ToContract).ToArray(), cancellationToken);
        });

        /*
         * THE BUTTON — and it is the whole difference to a background run.
         *
         * ADR-0004 forbids scraping; a nightly run over the document store would
         * keep its letter and miss its point — a platform that watches a human
         * being permanently does something other than one that looks once when
         * asked. That is why there is exactly one address here, it is called by
         * a click, and the upload path contains no call to the recogniser.
         * Measured in `RecognitionJourneyTests`, not claimed: after an upload the
         * recogniser's counter stands at zero.
         *
         * POST and not GET, because it WRITES: the terms are stored. A query
         * would run past `TransactionBehavior` and the row would never be
         * committed — the same trap as `POST /me/oauth/start`.
         */
        documents.MapPost("/read", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var state = await mediator.Send(
                new ReadDocumentsCommand(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                state.Select(ToContract).ToArray(), cancellationToken);
        });

        /*
         * What was last read — WITHOUT reading.
         *
         * The profile page asks this on load, and it may, because nothing
         * happens here: no file is opened, no recogniser called, no row
         * written. Whoever lifts this separation and hangs the read on here
         * turns opening a page into exactly the background run the button
         * beside it avoids.
         */
        documents.MapGet("/terms", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var state = await mediator.Send(
                new MyTermsQuery(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                state.Select(ToContract).ToArray(), cancellationToken);
        });

        documents.MapGet("/{id:guid}/content", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var found = await mediator.Send(
                new DocumentContentQuery(caller.Subject, id), cancellationToken);

            if (found is not { } hit)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            // `inline` and not `attachment`: the folder shows the document in the
            // browser instead of downloading it. The file name carries the
            // extension from the RECOGNISED type, not from the uploaded name.
            context.Response.ContentType = hit.Document.ContentType;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + hit.Document.Id.ToString("N")
                + ContentTypeDetection.Extension(hit.Document.ContentType) + "\"";

            await context.Response.Body.WriteAsync(hit.Content, cancellationToken);
        });

        documents.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var deleted = await mediator.Send(
                new DeleteDocumentCommand(caller.Subject, id), cancellationToken);

            context.Response.StatusCode = deleted
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        documents.MapPut("/{id:guid}/as-cv", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var marked = await mediator.Send(
                new MarkDocumentAsResumeCommand(caller.Subject, id), cancellationToken);

            context.Response.StatusCode = marked
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        resumes.MapPut("/me/template", async (
            ChooseTemplateV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            // An unknown template is REFUSED and not quietly turned into the
            // default: otherwise somebody chooses something, gets 200 back and
            // sees something else afterwards.
            if (TemplateFromWord(body?.Template) is not { } template)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: template");
                return;
            }

            var chosen = await mediator.Send(
                new ChooseTemplateCommand(caller.Subject, template), cancellationToken);

            if (!chosen)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no resume");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    /// <summary>
    /// What a filter returns that has written the answer itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> along with headers,
    /// and those have already been sent at that point. For a request without a
    /// body the caller still sees their 503 and only the log carries an
    /// unhandled exception; for one with a body the connection tears, and they
    /// get "Error while copying content to a stream". Measured at
    /// <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Written = Results.Empty;

    private static async Task AnswerAsync(
        Guid id,
        bool grant,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (principal.Current is not { } caller)
        {
            await NotAuthenticated(context);
            return;
        }

        var result = await mediator.Send(
            new AnswerRequestCommand(id, caller.Subject, grant), cancellationToken);

        await WriteResultAsync(context, result, cancellationToken);
    }

    /// <summary>
    /// Turns the domain's answers into status codes — in one place, so eight
    /// routes cannot drift apart.
    /// </summary>
    private static async Task WriteResultAsync(
        HttpContext context,
        ResumeRequestResult result,
        CancellationToken cancellationToken,
        int success = StatusCodes.Status200OK)
    {
        switch (result)
        {
            case ResumeRequestResult.Done done:
                context.Response.StatusCode = success;
                await context.Response.WriteAsJsonAsync(
                    ToContract(done.Request, active: null), cancellationToken);
                return;

            case ResumeRequestResult.NotVisible:
                // The same 404 a non-existent person gets. Asking requires the
                // profile release; without it the company must not be able to
                // tell "hidden" from "not here".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;

            case ResumeRequestResult.AlreadyAsked:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "already asked");
                return;

            case ResumeRequestResult.RuleBroken broken:
                await ProblemDetailsMiddleware.Write(
                    context,
                    broken.Code == ResumeRequestRule.NotTheSubject
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status409Conflict,
                    "Request failed",
                    broken.Explanation);
                return;
        }
    }

    private static ResumeV1 ToContract(CurriculumVitae resume) => new(
        resume.Subject.Value,
        [.. resume.Positions.Select(position => new PositionV1(
            position.Employer, position.Title, position.Start.ToString(),
            position.End?.ToString(), position.Description, position.Technologies))],
        [.. resume.Education.Select(education => new EducationV1(
            education.Institution, education.Qualification, education.Start.ToString(),
            education.End?.ToString(),
            // Wire words, shared with the web app and with the stored rows.
            education.Kind == EducationKind.School ? "schule" : "ausbildung"))],
        resume.UpdatedAt,
        TemplateWord(resume.Template));

    /// <summary>The template name on the wire.</summary>
    /// <remarks>
    /// Lower-case and German — wire words the web app sends and reads back.
    /// They stay byte-identical; a second dictionary would be a second
    /// opportunity to misspell them.
    /// </remarks>
    private static string TemplateWord(ResumeTemplate template) => template switch
    {
        ResumeTemplate.Classic => "klassisch",
        ResumeTemplate.Modern => "modern",
        _ => "schlicht"
    };

    /// <summary>The template for the word, or <c>null</c>.</summary>
    private static ResumeTemplate? TemplateFromWord(string? word) => word switch
    {
        "schlicht" => ResumeTemplate.Plain,
        "klassisch" => ResumeTemplate.Classic,
        "modern" => ResumeTemplate.Modern,
        _ => null
    };

    /// <summary>The wire word for a document's kind.</summary>
    private static string KindWord(DocumentKind kind) => kind switch
    {
        DocumentKind.Reference => "zeugnis",
        DocumentKind.Certificate => "zertifikat",
        DocumentKind.CurriculumVitae => "lebenslauf",
        _ => "sonstiges"
    };

    /// <summary>The kind for the wire word — anything unknown becomes "sonstiges".</summary>
    /// <remarks>
    /// Deliberately NOT refused here: the kind orders things for the human who
    /// reads the folder, and it is evaluated nowhere. Letting an application
    /// fail over a typo in an ordering term would be strictness in the wrong
    /// place.
    /// </remarks>
    private static DocumentKind KindFromWord(string? word) => word switch
    {
        "zeugnis" => DocumentKind.Reference,
        "zertifikat" => DocumentKind.Certificate,
        "lebenslauf" => DocumentKind.CurriculumVitae,
        _ => DocumentKind.Other
    };

    private static DocumentRecordV1 ToContract(DocumentRecord document) => new(
        document.Id,
        document.Name,
        KindWord(document.Kind),
        document.ContentType,
        document.SizeBytes,
        document.UploadedAt);

    /// <summary>The state of a document — read or not, and what stood in it.</summary>
    /// <remarks>
    /// <c>read_at: null</c> means "never read" and is something other than an
    /// empty word list. The name travels along so the interface can say from
    /// WHICH file there was nothing to read — "nothing found" without the file
    /// would again be the silent claim that nothing stands in it (ADR-0022 §3).
    /// </remarks>
    private static DocumentTermsV1 ToContract(DocumentState state) => new(
        state.Document.Id,
        state.Document.Name,
        state.Terms?.ReadAt,
        state.Terms?.TextFound ?? false,
        state.Terms?.Terms ?? []);

    private static RequestV1 ToContract(ResumeRequest requestRecord, bool? active) => new(
        requestRecord.Id,
        requestRecord.Subject.Value,
        requestRecord.Company.Value,
        requestRecord.State.ToString().ToUpperInvariant(),
        requestRecord.AskedAt,
        requestRecord.AnsweredAt,
        active);

    private static IReadOnlyList<Position> ToPositions(StoreResumeV1 body) =>
        [.. body.Positions.Select(position => Position.Create(
            position.Employer, position.Title, Month.Parse(position.StartedOn),
            position.EndedOn is null ? null : Month.Parse(position.EndedOn),
            position.Description, position.Technologies))];

    private static IReadOnlyList<Education> ToEducation(StoreResumeV1 body) =>
        [.. body.Education.Select(education => Education.Create(
            education.Institution, education.Qualification, Month.Parse(education.StartedOn),
            education.EndedOn is null ? null : Month.Parse(education.EndedOn),
            // Wire words: "schule" is school, anything else vocational training.
            education.Kind == "schule" ? EducationKind.School : EducationKind.Vocational))];

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
