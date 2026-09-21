using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>Subject and body of one mail, in one language.</summary>
/// <param name="Subject">The subject line.</param>
/// <param name="Text">The body. Plain text; the link is written out.</param>
public sealed record MailText(string Subject, string Text);

/// <summary>The five mails this service sends, in the three languages.</summary>
/// <remarks>
/// <strong>Here and not in the application layer</strong>, for the same reason
/// the wording was here before: a handler names an intent, and the words are
/// infrastructure. What changed is that each intent now has three wordings
/// instead of one.
/// <para>
/// The German is the source and the other two are translations of it —
/// <c>MailtextTests</c> pins that all three exist for every intent and that
/// none is a copy of the German, the same guard the frontend catalogues carry.
/// </para>
/// <para>
/// <strong>The news mail says the same nothing in every language.</strong> That
/// it carries no kind, no company and no count is not a wording decision that
/// translation could soften: a mail can land in a mailbox that is not the
/// person's alone, and the sentence that would make it more useful is exactly
/// the one that costs somebody their job.
/// </para>
/// </remarks>
public static class MailTexts
{
    /// <summary>"Confirm your address", with the link.</summary>
    /// <param name="language">The recipient's language.</param>
    /// <param name="link">The full confirmation address.</param>
    /// <returns>Subject and body.</returns>
    public static MailText Confirmation(AccountLanguage language, string link) => language switch
    {
        AccountLanguage.En => new MailText(
            "Please confirm your email address",
            "Welcome to WorkerTransfer! Please confirm your email address "
            + $"using this link:\n\n{link}\n"),
        AccountLanguage.Fr => new MailText(
            "Merci de confirmer votre adresse e-mail",
            "Bienvenue sur WorkerTransfer ! Merci de confirmer votre adresse "
            + $"e-mail via ce lien :\n\n{link}\n"),
        _ => new MailText(
            "Bitte bestätige deine E-Mail-Adresse",
            "Willkommen bei WorkerTransfer! Bitte bestätige deine E-Mail-Adresse "
            + $"über folgenden Link:\n\n{link}\n")
    };

    /// <summary>"Somebody tried to register with your address."</summary>
    /// <param name="language">The recipient's language.</param>
    /// <returns>Subject and body.</returns>
    public static MailText DuplicateRegistration(AccountLanguage language) => language switch
    {
        AccountLanguage.En => new MailText(
            "Registration attempt with your email address",
            "Somebody tried to create a new account at WorkerTransfer with your "
            + "email address. You already have an account — if that was you, "
            + "simply sign in. If it was not, you can ignore this message.\n"),
        AccountLanguage.Fr => new MailText(
            "Tentative d’inscription avec votre adresse e-mail",
            "Quelqu’un a tenté de créer un nouveau compte sur WorkerTransfer "
            + "avec votre adresse e-mail. Vous avez déjà un compte — si c’était "
            + "vous, connectez-vous simplement. Sinon, vous pouvez ignorer ce "
            + "message.\n"),
        _ => new MailText(
            "Registrierungsversuch mit deiner E-Mail-Adresse",
            "Jemand hat versucht, mit deiner E-Mail-Adresse ein neues Konto bei "
            + "WorkerTransfer anzulegen. Du hast bereits ein Konto — falls du das "
            + "warst, melde dich einfach an. War es nicht du, kannst du diese "
            + "Nachricht ignorieren.\n")
    };

    /// <summary>"You have been invited into a company", with the link.</summary>
    /// <param name="language">The recipient's language.</param>
    /// <param name="company">The company's name, already filled in.</param>
    /// <param name="link">The full invitation address.</param>
    /// <returns>Subject and body.</returns>
    public static MailText InvitationMail(AccountLanguage language, string company, string link) => language switch
    {
        AccountLanguage.En => new MailText(
            "You have been invited to a company",
            $"You have been invited to act for {company} at WorkerTransfer. "
            + "Use this link to accept the invitation:\n\n"
            + $"{link}\n\n"
            + "If this means nothing to you, ignore this message.\n"),
        AccountLanguage.Fr => new MailText(
            "Vous avez été invité dans une entreprise",
            $"Vous avez été invité à agir pour {company} sur WorkerTransfer. "
            + "Utilisez ce lien pour accepter l’invitation :\n\n"
            + $"{link}\n\n"
            + "Si cela ne vous dit rien, ignorez ce message.\n"),
        _ => new MailText(
            "Du wurdest zu einem Unternehmen eingeladen",
            $"Du wurdest eingeladen, für {company} bei WorkerTransfer zu handeln. "
            + "Über folgenden Link nimmst du die Einladung an:\n\n"
            + $"{link}\n\n"
            + "Wenn du damit nichts anfangen kannst, ignoriere diese Nachricht.\n")
    };

    /// <summary>The company name to use when none was given.</summary>
    /// <param name="language">The recipient's language.</param>
    /// <returns>A neutral placeholder.</returns>
    public static string ACompany(AccountLanguage language) => language switch
    {
        AccountLanguage.En => "a company",
        AccountLanguage.Fr => "une entreprise",
        _ => "ein Unternehmen"
    };

    /// <summary>The one mail that says the deletion is done.</summary>
    /// <param name="language">The recipient's language.</param>
    /// <returns>Subject and body.</returns>
    public static MailText ErasureConfirmation(AccountLanguage language) => language switch
    {
        AccountLanguage.En => new MailText(
            "Your WorkerTransfer account has been deleted",
            "Your account and the data other services held about you have been "
            + "deleted. There is nothing left here that points to you.\n\n"
            + "This message is the last one you will get from us.\n"),
        AccountLanguage.Fr => new MailText(
            "Votre compte WorkerTransfer a été supprimé",
            "Votre compte et les données que d’autres services détenaient à "
            + "votre sujet ont été supprimés. Plus rien ici ne renvoie à vous.\n\n"
            + "Ce message est le dernier que vous recevrez de notre part.\n"),
        _ => new MailText(
            "Dein Konto bei WorkerTransfer ist gelöscht",
            "Dein Konto und die Daten, die andere Dienste über dich hielten, sind "
            + "gelöscht. Es gibt nichts mehr, das dich hier zuordnet.\n\n"
            + "Diese Nachricht ist die letzte, die du von uns bekommst.\n")
    };

    /// <summary>"There is something new" — and deliberately nothing more.</summary>
    /// <param name="language">The recipient's language.</param>
    /// <param name="baseUrl">The site's address.</param>
    /// <returns>Subject and body.</returns>
    public static MailText News(AccountLanguage language, string baseUrl) => language switch
    {
        AccountLanguage.En => new MailText(
            // The same subject for every kind — the kind is precisely the secret.
            "News on WorkerTransfer",
            "There is something new for you.\n\n"
            + $"Sign in to take a look: {baseUrl}\n\n"
            + "What it is deliberately does not appear in this mail — it could "
            + "sit in a mailbox that is not yours alone.\n"),
        AccountLanguage.Fr => new MailText(
            "Du nouveau sur WorkerTransfer",
            "Il y a du nouveau pour vous.\n\n"
            + $"Connectez-vous pour y jeter un œil : {baseUrl}\n\n"
            + "Ce dont il s’agit ne figure volontairement pas dans cet e-mail — "
            + "il pourrait arriver dans une boîte qui n’est pas seulement la "
            + "vôtre.\n"),
        _ => new MailText(
            "Neuigkeiten auf WorkerTransfer",
            "Es gibt etwas Neues für dich.\n\n"
            + $"Melde dich an, um nachzusehen: {baseUrl}\n\n"
            + "Was es ist, steht bewusst nicht in dieser Mail — sie könnte in "
            + "einem Postfach liegen, das nicht nur dir gehört.\n")
    };
}
