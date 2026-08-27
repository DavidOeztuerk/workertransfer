namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Makes and recognises single-use tokens.</summary>
/// <remarks>
/// The plaintext goes into the mail, the hash into the database. Nothing here
/// can turn a stored hash back into a link, which is the whole point.
/// </remarks>
public interface IEinmaltoken
{
    /// <summary>A fresh token and its hash.</summary>
    (string Klartext, string Hash) Erzeuge();

    /// <summary>The hash of a token somebody presented.</summary>
    string Hashe(string klartext);
}
