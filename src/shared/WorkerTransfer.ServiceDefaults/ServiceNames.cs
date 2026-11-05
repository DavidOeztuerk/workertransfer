namespace WorkerTransfer.ServiceDefaults;

/// <summary>Who may ask identity-service for a service token (ADR-0057).</summary>
/// <remarks>
/// Constants and not strings at each door: a misspelt caller on a list compiles,
/// and that caller then gets a 404 which several services read as "nothing
/// there".
/// </remarks>
public static class ServiceClients
{
    /// <summary>advisor-service.</summary>
    public const string Advisor = "advisor";

    /// <summary>applications-service.</summary>
    public const string Applications = "applications";

    /// <summary>assessment-service.</summary>
    public const string Assessment = "assessment";

    /// <summary>companies-service.</summary>
    public const string Companies = "companies";

    /// <summary>jobs-service.</summary>
    public const string Jobs = "jobs";

    /// <summary>messages-service.</summary>
    public const string Messages = "messages";

    /// <summary>notification-service.</summary>
    public const string Notification = "notification";

    /// <summary>profile-service.</summary>
    public const string Profile = "profile";

    /// <summary>resume-service.</summary>
    public const string Resume = "resume";

    /// <summary>scout-service.</summary>
    public const string Scout = "scout";

    /// <summary>transfer-service.</summary>
    public const string Transfer = "transfer";

    /// <summary>Every client, as the issuer registers them.</summary>
    public static readonly string[] All =
    [
        Advisor, Applications, Assessment, Companies, Jobs, Messages,
        Notification, Profile, Resume, Scout, Transfer
    ];
}

/// <summary>What a token may be issued for: the services that have doors.</summary>
/// <remarks>
/// None may equal <c>JwtSettings__Audience</c>, the audience of people's tokens:
/// Noelia then refuses to start.
/// </remarks>
public static class ServiceAudiences
{
    /// <summary>identity-service.</summary>
    public const string Identity = "identity";

    /// <summary>notification-service.</summary>
    public const string Notification = "notification";

    /// <summary>profile-service.</summary>
    public const string Profile = "profile";

    /// <summary>Every audience, as the issuer registers them.</summary>
    public static readonly string[] All = [Identity, Notification, Profile];
}
