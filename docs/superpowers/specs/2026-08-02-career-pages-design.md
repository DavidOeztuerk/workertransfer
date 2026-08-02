# Sub-step 4.4 — Karriere-Seiten: eine Adresse, die man teilen kann

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: [Companies-Design](2026-08-02-companies-service-design.md), [Jobs-Design](2026-08-02-jobs-service-design.md), ADR-0004 (kein Scraping, eigene Datenbank je Dienst), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 4

## Die offene Frage: Subdomain oder Pfad?

Der ULTRAPLAN nennt `karriere.firma.de/<bewerber>` — also Wildcard-DNS, ein
Reverse-Proxy und TLS je Kundendomain.

**Entscheidung: Pfad zuerst, Subdomain später als reine Routing-Frage.**

Der Wert einer Karriere-Seite liegt im Inhalt und in der Adresse, die man
weitergeben kann — nicht im DNS-Label. Eine Subdomain ist eine
**Betriebsentscheidung**: Wildcard-Zertifikat, Proxy-Regel, ein Weg, wie ein
Kunde seinen DNS-Eintrag setzt. Nichts davon steht im Anwendungscode, und
nichts davon lässt sich lokal ehrlich prüfen.

Wird die Anwendung pfadbasiert gebaut (`/karriere/<kürzel>`), läuft
derselbe Code später hinter `karriere.firma.de` — der Proxy schreibt den Host
auf den Pfad um. Andersherum wäre der Anwendungscode an eine Betriebsform
gebunden, die es noch nicht gibt.

Das ist eine bewusste Abweichung vom ULTRAPLAN, und sie verkleinert den Umfang
nicht: die Seite existiert, sie ist erreichbar, sie ist teilbar.

## Was auf der Seite steht

Das Unternehmensprofil (4.3) und **seine offenen Stellen**. Mehr nicht, in
diesem Schnitt.

**Ausdrücklich keine personalisierte Ansprache.** Der ULTRAPLAN sieht
`karriere.firma.de/<bewerber>` vor, also eine Seite für einen bestimmten
Menschen. Eine solche Seite, die öffentlich erreichbar ist, verrät jedem mit
dem Link zwei Dinge: dass diese Person auf der Plattform ist, und dass dieses
Unternehmen um sie wirbt. Beides kann jemanden den Arbeitsplatz kosten.

Wer sie will, bekommt sie später mit derselben Regel wie bei der Einladung ins
Unternehmen (Scheibe C): **Token UND passende angemeldete Person.** Der
öffentliche Teil bleibt dann öffentlich, der persönliche erscheint nur der
angesprochenen Person. Das ist ein eigener Schnitt mit eigener Begründung.

## Das Kürzel

Jedes Unternehmensprofil bekommt eins, **abgeleitet aus dem Anzeigenamen** und
danach **unveränderlich**.

- Abgeleitet, nicht eingegeben: ein freies Feld lädt zum Besetzen fremder
  Namen ein, und es wäre eine Entscheidung mehr in einem Formular, das schon
  fünf hat.
- Unveränderlich, obwohl der Anzeigename sich ändern darf: **die Adresse ist ein
  Versprechen.** Ein Kürzel, das dem Namen folgt, bricht jeden geteilten Link,
  sobald jemand aus „Muster" die „Muster AG" macht.
- Bei Kollision ein Zähler (`muster`, `muster-2`). Nicht die Tenant-UUID
  anhängen: die stünde dann in einer URL, die weitergegeben wird.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `GET` | `/companies/by-slug/{slug}` | jeder | das Profil, sonst `404` |
| `GET` | `/jobs?company={tenant_id}` | jeder | veröffentlichte Stellen dieses Unternehmens |

Der Filter in der Stellensuche ist eine Ergänzung, kein neuer Endpunkt: es ist
dieselbe Menge mit einer Bedingung mehr, und ein zweiter Weg an dieselben Daten
hätte einen zweiten Filter, der irgendwann abweicht.

Die Seite selbst holt beides: erst das Profil über das Kürzel, dann die Stellen
über die `tenant_id` daraus. Zwei Aufrufe statt eines zusammengesetzten
Endpunkts — die Dienste haben getrennte Datenbanken (ADR-0004), und ein
Dienst, der für den anderen antwortet, verwischt genau die Grenze.

## Abgrenzung

**Keine Videos, keine Bilder** — die Ablage kann Dateien (ADR-0021), aber ein
Medienbereich ist eine Darstellungsentscheidung mit eigenem Umfang.
**Keine Direktbewerbung außerhalb des bestehenden Wegs**: die Stellen auf der
Seite führen zu `/jobs`, wo bewerben bereits funktioniert und der
Consent-Ledger greift. Ein zweiter Bewerbungsweg wäre ein zweiter Ort, an dem
die Freigabe entsteht.
**Kein DNS, kein Proxy** — siehe oben.

## Umsetzung

**Eine Scheibe.** Kürzel in `companies-service` (Migration, Ableitung,
Kollisionszähler, Endpunkt), `company`-Filter in `jobs-service`, die Seite
`/karriere/<kürzel>` in `apps/web`, Integrationstests und eine Erweiterung der
bestehenden Jobs-Reise.

## Selbstprüfung

*Warum kein editierbares Kürzel?* Weil es zwei Probleme schafft: das Besetzen
fremder Namen und geteilte Links, die brechen. Wer wirklich ein anderes will,
kann es später bekommen — dann mit einer Weiterleitung vom alten, und das ist
eine Funktion mit eigenem Preis.

*Ist `/karriere/<kürzel>` ohne Subdomain nicht enttäuschend?* Für den Kunden
vielleicht. Aber eine Seite, die es gibt, ist mehr wert als eine Subdomain, die
konfiguriert werden müsste, bevor irgendetwas zu sehen ist. Der Weg dorthin
bleibt offen und kostet keinen Anwendungscode.
