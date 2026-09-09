# ADR-0038: Zivilidentität und Bewerbungsanschrift

**Status:** angenommen (07.09.2026)
**Betrifft:** identity-service, applications-service, `web/`
**Verwandt:** ADR-0017 (Person hat keinen Mandanten), ADR-0018 (Token ohne Rollen/Namen), ADR-0020 (Sichtbarkeit nur im Ledger), ADR-0024 (KI-Naht speichert nichts), ADR-0027 (Löschung), ADR-0034 (Anschreiben), ADR-0035 (Bewerbungsmappe)

## Kontext

Ein Bewerbungsbrief braucht einen bürgerlichen Namen und eine Anschrift im
Briefkopf. Die Plattform hatte nur `display_name` (einen gewählten Anzeigenamen)
und einen Matching-Ort auf dem Profil. Beides in denselben Topf zu werfen
hiesse: Nickname in Verträge, Klarname in die Suche.

Die KI-Naht (ADR-0024, ADR-0034) darf den Brief *inhaltlich* stützen. Straße,
Telefon und E-Mail identifizieren den Wohnort und gehören nicht ins Modell.

## Entscheidung

1. **Bürgerlicher Vor- und Nachname leben auf `users`** (`given_name`,
   `family_name`, nullable). `display_name` bleibt der öffentliche Anzeigename
   (Team, Kopf, Session). Die Profil**seite** editiert identity; profile-service
   bekommt keine Klarnamen-Spalte — sonst sähe jedes suchende Unternehmen die
   Zivilidentität, sobald `profile.visibility` gilt (ADR-0020).

2. **Postanschrift plus optionales Telefon** leben als *Vorlage* auf identity
   (`addresses`, `Personenzeile`) und als **Snapshot** auf der gesendeten
   Bewerbung. Nie auf `profiles`. Nie im JWT. Nie in einem `IEntwerfer`- oder
   `Anschreibenkontext`. Beim Senden kopiert applications-service, was dann
   gilt, damit ein späterer Umzug die Firmenmappe nicht umschreibt.

3. **`Anschreibenkontext` darf den Klarnamen für die Signatur nutzen** und
   **darf keine** Anschrift-/Mail-/Telefonfelder bekommen. Das Wort „Kontakt“ in
   ADR-0034 meint den Briefkopf in der Oberfläche, nicht den Prompt.

4. **Weder Name noch Anschrift im Token.** JS liest das Access-Cookie nie.
   `Tokenform.NiemalsImToken` nennt `given_name`, `family_name`, `address`,
   `phone`.

## Rechtsgrundlage (kein zweites Ledger)

Art. 6 Abs. 1 lit. b DSGVO: vorvertragliche Schritte auf Anfrage der Person —
das Senden. Art. 5 Abs. 1 lit. b+c (Zweckbindung, Datenminimierung), Art. 13
(Transparenz im Katalog), Art. 17 / ADR-0027 (fällt mit Konto und Bewerbung),
Art. 25 (Felder optional, Vorgabe leer). Keine Einwilligung Art. 6 Abs. 1 lit. a:
die wäre kündbar und würde Senden blockieren. Keine neue Ledger-Capability:
Senden *ist* die Freigabe (ADR-0035).

## Folgen

- Registrierung: Anzeigename Pflicht, Klarname optional.
- `GET /auth/session` trägt `given_name` / `family_name`, **keine** Anschrift —
  sonst zöge `HttpBewerberauskunft` sie in den Prompt.
- `GET/PUT /account/address` nur die Person selbst.
- Qualifizierte Signatur / DocuSign: eigenes ADR, nicht dieses.

## Nicht entschieden

Telefon auf der Anschrift ist optional und folgt demselben Zweck. Geburtsdatum,
Geschlecht, Staatsangehörigkeit bleiben draussen.
