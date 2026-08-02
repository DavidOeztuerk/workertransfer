# ADR-0020: Der Consent-Ledger als Enabler — wie profile-service ihn benutzt

Date: 2026-08-02
Status: Accepted
Related: ADR-0013 (Consent-Ledger eigenständig, synchron gelesen, ohne Cache), ADR-0004 (eigene Datenbank je Service), ADR-0017 (Tenant ist ein Unternehmensbegriff), [product-scope.md](../product-scope.md)

## Kontext

ADR-0013 hat den Consent-Ledger als eigenständigen Dienst festgelegt, der
synchron gelesen und nicht zwischengespeichert wird. Was dort offenblieb: wie
ein konsumierender Service ihn tatsächlich benutzt — und was passiert, wenn die
Antwort ausbleibt.

`profile-service` ist der erste Konsument und damit der Ort, an dem sich
entscheidet, ob „Consent als Enabler" ein Satz aus einem Dokument bleibt oder
eine Eigenschaft des Systems wird. Die Fragen, die dabei beantwortet werden
mussten, sind alle von der Form „was sagen wir, wenn wir es nicht wissen" — und
jede falsche Antwort darauf ist ein Leck.

## Entscheidung

**1. Verborgen und nicht vorhanden sind ununterscheidbar.**

`GET /profiles/{subject_id}` antwortet `404`, wenn kein Profil existiert, wenn
eines existiert aber nicht freigegeben ist, und wenn die UUID unlesbar ist. Der
Antwortkörper ist in allen drei Fällen bis auf die Korrelations-ID identisch.
Ein eigener Code oder eine eigene Meldung für „existiert, zeigt sich aber
nicht" wäre ein Orakel: wer eine Liste von UUIDs durchprobiert, erführe, wer
Mitglied ist, ohne je ein Profil zu sehen.

**2. Aussagen über den Aufrufer dürfen unterscheidbar sein.**

Ohne aktives Unternehmen antwortet derselbe Endpunkt `403`. Das ist kein
Widerspruch zu (1): der Code sagt etwas über den Fragenden, nicht über das
Ziel, und ist damit keine Information über eine dritte Person. Er ist außerdem
behebbar — die Oberfläche kann zum Unternehmenswechsel führen, statt eine
Sackgasse zu zeigen.

**3. Schweigt der Ledger, antwortet niemand für ihn.**

Fällt die Abfrage aus — Transportfehler, ein anderer Status als 200, ein
unlesbarer Körper — wirft der Adapter `ConsentUnavailable`, und der Router macht
daraus `503`. Nicht `404`, denn das wäre die Behauptung, die Person habe nicht
eingewilligt; nicht „anzeigen", denn das wäre die umgekehrte Behauptung. Beides
wüsste in dem Moment niemand.

**4. Kein Nachladen, bis die Seite voll ist.**

`GET /profiles` liefert eine Seite, filtert sie auf die freigegebenen Einträge
und gibt zurück, was übrig bleibt — auch wenn das weniger ist als angefragt.
Nachzuladen, bis die Seite voll ist, würde über die Anzahl der Runden verraten,
wie viele Profile gerade **nicht** freigegeben sind. Aus demselben Grund nennt
die Oberfläche keine Gesamtzahl.

**5. Das eigene Profil fragt den Ledger nicht.**

`GET /profiles/me` liest ohne Consent-Abfrage. Die eigene Einwilligung zu
prüfen, um sich selbst zu sehen, wäre nicht nur ein überflüssiger Round-Trip:
wer nichts freigegeben hat, könnte sein Profil sonst nicht mehr bearbeiten.

**6. Das Profil kennt keine Sichtbarkeit.**

Weder das Aggregat noch `SaveProfileV1` hat ein Sichtbarkeitsfeld. Die
Freigabe steht ausschließlich im Ledger. Ein Flag am Profil wäre eine zweite
Wahrheit — und eine, die der Client mitschicken könnte.

**7. Der Service fragt im Auftrag des Aufrufers.**

Der Adapter reicht das Bearer-Token des Aufrufers an den Ledger weiter, statt
sich mit einem Dienstkonto zu authentisieren. Damit steht im Audit-Log des
Ledgers, wer wirklich gefragt hat.

## Konsequenzen

Jeder Abruf eines fremden Profils kostet einen HTTP-Round-Trip zum Ledger; eine
Seite mit 20 Einträgen kostet 20 davon (parallel abgesetzt). Das ist der Preis
für die Sofortwirkung aus ADR-0013 und wird bewusst bezahlt. Die Seitengröße ist
auf 50 gedeckelt, damit eine einzelne Anfrage den Ledger nicht flutet.

Fällt der Ledger aus, ist die Kandidatensuche nicht benutzbar. Das ist die
gewollte Kopplungsrichtung: lieber nichts zeigen als das Falsche.

Belegt ist das Ganze an der Stelle, an der es zählt —
`apps/profile-service/tests/integration/test_consent_gated_reads.py` fährt beide
Dienste mit je eigener Datenbank hoch und prüft: anlegen → freigeben → `200` →
widerrufen → `404`, ohne Wartezeit dazwischen. Die Browser-Reise in
`apps/web/e2e/consent-journey.spec.ts` prüft dasselbe noch einmal durch die
Oberfläche, weil ein Schalter, der nicht schaltet, auf beiden Ebenen darunter
unsichtbar wäre.

## Verworfene Alternativen

**Ein Cache mit kurzer TTL.** Von ADR-0013 bereits verworfen und hier erneut
bestätigt: „sofort" mit einer Ausnahme ist nicht sofort. Ein Widerruf, der erst
nach dreißig Sekunden wirkt, ist ein Widerruf, den man dreißig Sekunden lang
nicht hat.

**Ein Sichtbarkeits-Flag am Profil, mit dem Ledger als Zweitprüfung.** Hätte den
Listen-Endpunkt billiger gemacht, weil sich schon in SQL filtern ließe. Zwei
Wahrheiten über dieselbe Frage laufen aber auseinander, und die Stelle, an der
sie das tun, ist genau die, an der jemand sichtbar wird, der es nicht sein
wollte.

**`403` statt `404` für ein verborgenes Profil.** Ehrlicher gegenüber dem
Aufrufer, aber die Ehrlichkeit geht auf Kosten der Person, um die es geht.
