using System.Text.RegularExpressions;

namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>A capability token that does not read as one.</summary>
public sealed class InvalidCapabilityException(string value)
    : Exception($"'{value}' is not a capability token.")
{
    /// <summary>What was offered.</summary>
    public string Value { get; } = value;
}

/// <summary>
/// A namespaced permission, e.g. <c>profile.visibility:public</c>.
/// </summary>
/// <remarks>
/// Every capability in this system is a <em>visibility</em>: who may see
/// something, never what may be done to it. That is why "delete
/// profile.visibility:public" could never mean "delete the profile" — and why
/// there is no <c>/consent/delete</c> (ADR-0027 §1).
/// <para>
/// The shape is checked, the vocabulary is not. A list of permitted
/// capabilities would be a claim about which permissions can exist, and the
/// ledger has to be able to answer about anything a consumer asks — absence is
/// a state, not an error.
/// </para>
/// </remarks>
public sealed partial record Capability
{
    /// <summary>The longest token the boundary contract carries.</summary>
    public const int HoechsteLaenge = 255;

    private Capability(string value) => Value = value;

    /// <summary>The token itself.</summary>
    public string Value { get; }

    /// <summary>Reads a token, or says it cannot.</summary>
    public static bool TryParse(string? value, out Capability capability)
    {
        capability = null!;

        if (string.IsNullOrEmpty(value) || value.Length > HoechsteLaenge || !Muster().IsMatch(value))
        {
            return false;
        }

        capability = new Capability(value);
        return true;
    }

    /// <summary>Reads a token, or refuses.</summary>
    /// <exception cref="InvalidCapabilityException">It is not a token.</exception>
    public static Capability Parse(string value) =>
        TryParse(value, out var capability) ? capability : throw new InvalidCapabilityException(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    // The pattern the Python ledger used, verbatim: a lowercase namespace, an
    // optional word, and an optional third part that may carry a UUID — that is
    // what makes `resume.visibility:tenant:<uuid>` one capability rather than a
    // family of them.
    [GeneratedRegex(@"^[a-z][a-z_.]+(:\w+)?(:[{]?[\w-]+[}]?)?$")]
    private static partial Regex Muster();
}
