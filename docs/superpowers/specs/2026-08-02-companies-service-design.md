# Sub-step 4.3 — Companies-Service: wer sucht

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0017 (Tenant = Unternehmen), ADR-0019 (Domain-Nachweis), ADR-0021 (Ablage), [Jobs-Design](2026-08-02-jobs-service-design.md)

## Die Lücke

Wer heute eine Stellenausschreibung liest, sieht einen Titel, eine
Beschreibung und einen Ort. **Er sieht nicht, wer sucht.** Auf einem
Transfermarkt ist das die halbe Entscheidung: eine Stelle ist nicht dasselbe
bei zwei verschiedenen Arbeitgebern.

## Warum ein eigener Dienst und nicht `identity-service`

`tenants` in `identity-service` hält Name und Domain — die **Identität** eines
Unternehmens, entstanden aus einem Domain-Nachweis (ADR-0019). Ein
Arbeitgeberprofil ist etwas anderes: **Darstellung**. Kultur, Leistungen,
Standorte, ein Text über sich selbst.

Es ist genau dieselbe Trennung wie zwischen `identity-service` und
`profile-service` bei einer Person: die eine Seite weiß, wer jemand ist, die
andere, wie er sich zeigt. Beides in einen Dienst zu legen hieße, den
Anmeldeweg mit Marketing-Inhalten mithaften zu lassen.

## Der Name — zwei, und das ist Absicht

`tenants.name` ist der **Kontoname**, bei der Anlage angegeben. Das
Arbeitgeberprofil trägt einen eigenen **Anzeigenamen**.

Das sieht nach einer zweiten Wahrheit aus und ist keine: sie beschreiben
verschiedene Dinge, die nur ähnlich aussehen. Eine „Muster Holding GmbH & Co.
KG" tritt als „Muster" auf, und keiner der beiden Werte wird aus dem anderen
abgeleitet. Gefährlich wäre eine Kopie — hier gibt es keine.

**Ohne Profil bleibt eine Stelle anonym.** `GET /companies/{id}/profile`
antwortet dann `404`, und die Oberfläche zeigt schlicht keinen
Unternehmensteil. Ein Profil zu erzwingen, bevor jemand ausschreiben darf, wäre
eine Kopplung zwischen zwei Diensten für eine Regel, die kein Nutzer verlangt
hat; ein automatisch angelegtes Profil mit dem Kontonamen wäre eine Aussage,
die das Unternehmen nicht getroffen hat.

## Domäne

```
CompanyProfile
  tenant_id     UUID (PK)
  display_name  ≤ 160, Pflicht
  about         ≤ 8000        wer wir sind
  website       ≤ 2000, optional, nur http/https
  locations     [≤ 120] ≤ 20  wo wir sitzen
  benefits      [≤ 120] ≤ 20  was wir bieten
  updated_at
```

**Nur `http` und `https` in der Website** — dieselbe Regel wie bei
Portfolio-Links (ADR-0021 §Inhalte): ein Link wird von fremden Menschen
angeklickt, und `javascript:` in einem Feld, das im Browser landet, ist kein
Randfall.

**Listen statt Freitext für Standorte und Leistungen.** Sie werden gefiltert
und verglichen; ein Absatz „wir bieten unter anderem …" lässt sich weder
darstellen noch durchsuchen. Entdoppelt und getrimmt wie die Fähigkeiten im
Profil.

**Kein Logo in diesem Schnitt.** Die Ablage kann es (ADR-0021), aber ein Bild
gehört zu einer Darstellungsentscheidung, die zusammen mit der Oberfläche
getroffen wird — und `worker-storage` hat mit dem Portfolio bereits einen
Konsumenten.

## Wer darf was

| Handlung | Wer |
|---|---|
| Profil anlegen und ändern | jedes **Mitglied** |
| eigenes Profil lesen | dasselbe |
| ein Profil lesen | **jeder**, auch ohne Anmeldung |

**Jedes Mitglied, wie bei den Stellen.** Das Rollensystem kennt heute zwei
Rollen, und `admin` heißt „verwaltet die Mannschaft". Inhalte sind die Arbeit
der Mitglieder. Wer feinere Rechte braucht, braucht ein Rollensystem — keine
Sonderregel an dieser Stelle.

**Öffentlich lesbar.** Es ist die Selbstdarstellung eines Unternehmens; sie
hinter eine Anmeldung zu legen widerspricht ihrem Zweck, genau wie bei den
Stellen. Der Consent-Ledger kommt nicht vor: hier ist niemand betroffen, der
einwilligen könnte.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `PUT` | `/companies/me/profile` | Mitglied | das gespeicherte Profil |
| `GET` | `/companies/me/profile` | dasselbe | Profil oder `null` |
| `GET` | `/companies/{tenant_id}/profile` | jeder | das Profil, sonst `404` |

`null` für „noch keins" beim eigenen (ein Zustand, den die Oberfläche als
leeres Formular zeigt), `404` beim fremden (für die Öffentlichkeit gibt es
nichts, solange nichts angelegt wurde).

## Abgrenzung

**Kein Team, keine Mitgliederliste** — die gibt es in `identity-service`
(`/companies/{id}/members`) und sie ist bewusst **nicht** öffentlich.
**Keine Karriere-Seiten** — das ist 4.4, mit Subdomain und DNS.
**Keine Bewertungen.** Sie wären Aussagen von Personen über ein Unternehmen und
damit ein eigener Schnitt mit eigener Abwägung.

## Umsetzung in zwei Scheiben

**Scheibe A** — Dienst über `worker new-service`, Domäne, Migration,
Repository, drei Endpunkte, Integrationstests.

**Scheibe B** — Oberfläche: `/company/profile` zum Pflegen, und auf `/jobs`
zeigt jede Stelle das Unternehmen, sobald es eines hat.

## Selbstprüfung

*Warum kein `size` (Mitarbeiterzahl)?* Weil sie veraltet, sobald sie
eingetragen ist, und niemand sie pflegt. Wer sie nennen will, schreibt sie in
den Text — dort ist sichtbar, wann sie geschrieben wurde.

*Ist „jedes Mitglied darf ändern" nicht riskant?* Es ist dieselbe Abwägung wie
bei den Stellen, und dort ist sie größer: eine Ausschreibung geht nach draußen
und kostet Geld. Wer dem einen traut, traut auch dem anderen.
