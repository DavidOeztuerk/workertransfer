# ADR-0048 — Der Recruiter macht den ersten Schritt

**Datum:** 07.10.2026 · **Status:** angenommen
**Ersetzt:** nichts · **Berührt:** ADR-0018 (Mitgliedschaft als Beziehung), PBI-2
(`admin`/`member`), `docs/VISION-FRAGEN.md` B2, B7

## Zusammenhang

Eine Firma kennt zwei Rollen: `admin` bindet die Firma (einladen, entfernen,
veröffentlichen, anbieten, abschließen), `member` macht die Tagesarbeit. Der
Kommentar an `CompanyRole` sagt, jede weitere Rolle sei „eine Entscheidung,
wer wen aussperren kann, und die gehört in ein ADR“.

Der Katalog hat sie getroffen (B7): Es kommt eine Rolle **„Recruiter“** dazu,
„der Mensch, der Personen anschreibt“. Und B2: Den ersten Schritt macht die
Firma — durch einen Recruiter oder den KI-Recruiter, dessen Entwürfe ein Mensch
der Firma freigibt.

## Entscheidung

**Drei Rollen, geordnet: `member` < `recruiter` < `admin`.** Jede höhere darf,
was die niedrigere darf. Ein `admin` darf also auch ansprechen — in einer
kleinen Firma ist die Gründerin oft die einzige, die es tut, und ein Admin,
der für den ersten Schritt einen zweiten Account bräuchte, wäre derselbe
Zuschauerplatz, vor dem PBI-2 warnt.

**Der Recruiter darf genau eines zusätzlich: den ersten Kontakt zu einem
Menschen, der sich nicht selbst gemeldet hat.** Die Linie, einmal
aufgeschrieben, damit die nächste Route sie nicht neu erfindet:

- **Erster Kontakt** — eine Firma tritt an eine Person heran, die sich nicht
  beworben hat: Lebenslauf anfragen, Marktstatus anfragen, ein Gespräch beim
  Berater eröffnen, eine Ansprache (auch als Entwurf des KI-Recruiters)
  freigeben. → `recruiter`.
- **Antwort im laufenden Vorgang** — auf eine Bewerbung schreiben, eine
  Arbeitsprobe stellen und bewerten, Bewerbungen bearbeiten, suchen, Entwürfe
  anfordern. → `member`, wie bisher.
- **Binden** — wie bisher. → `admin`.

**Suchen bleibt `member`.** Wer sucht, spricht noch niemanden an; der Scout
zeigt Häkchen, keine Kontaktdaten. Erst der Schritt auf den Menschen zu ist
der, den eine Firma einer bestimmten Rolle vorbehalten will.

**Woher die Rolle kommt, ändert sich nicht:** aus der Mitgliedschaftstabelle,
je Anfrage, ohne Zwischenspeicher, über `GET /internal/companies/{id}/members/{sub}/role`.
Ein stummer Lookup ist 503, nie 403. Die Rechteprüfung in `ServiceDefaults`
wird von „nur admin“ zu „mindestens Rolle X“ verallgemeinert; jeder Dienst
nennt seine Rechte weiterhin selbst.

**Vergeben wird die Rolle wie `member`: per Einladung**, durch einen `admin`.
Eine Rollenänderung für bestehende Mitglieder gibt es heute für keine Rolle;
sie kommt, wenn sie gebraucht wird, für alle drei auf einmal.

## Was es kostet

- Ein `member` verliert vier Handlungen, die er bis heute hatte. Das ist der
  Zweck, und die Routenkarte zeigt es in der Spalte `mitglied`.
- resume-service braucht den Rollen-Lookup neu (`Identity__BaseUrl`,
  `Identity__Secret`) — eine Abhängigkeit mehr, und ohne sie antworten seine
  Anfragerouten 503.

## Verworfen

- **Recruiter als Recht ohne Rolle** (ein Häkchen an `member`). Dann gäbe es
  Mitgliedschaften mit und ohne Häkchen, und „was darf ein member?“ hätte zwei
  Antworten.
- **Recruiter neben statt unter `admin`.** Dann bräuchte jede Admin-Route und
  jede Recruiter-Route eine Liste erlaubter Rollen statt einer Mindestrolle —
  die Liste, die beim nächsten Mal unvollständig ist.
