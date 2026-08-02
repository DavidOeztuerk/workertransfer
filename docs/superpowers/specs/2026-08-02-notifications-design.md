# Querschnitt — Benachrichtigungen: eine Mail, die nichts verrät

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0017 (Tenant = Unternehmen), [Marktzugang (5.3)](2026-08-02-market-access-and-ui-design.md), [Resume-Design](2026-08-02-resume-service-design.md)

## Die Lücke, die in jedem Schnitt seit 3.3 steht

> „Eine Anfrage erreicht nur, wer sich anmeldet."

Das steht wortgleich unter Sub-step 3.3, 4.2, 5.2 und 5.3. Inzwischen hängen
daran vier Vorgänge — Lebenslauf-Anfrage, Marktstatus-Anfrage, Bewerbung,
Transfer —, und alle vier warten auf einen Menschen, der zufällig vorbeischaut.
Das ist keine Unbequemlichkeit mehr, sondern ein Konstruktionsfehler: ein System,
das „die Person entscheidet" verspricht, muss die Person erreichen.

## Der Fund, der den Entwurf bestimmt

Die naheliegende Mail wäre:

> *„Acme GmbH möchte deinen Marktstatus sehen."*

**Das ist die gefährlichste Zeile, die dieses System schreiben könnte.**

Eine Mail landet in einem Postfach, und dieses Postfach kann das Postfach beim
aktuellen Arbeitgeber sein — auf dessen Servern, in dessen Backups, im Blick
seiner Administratoren. Eine Zeile, die „Marktstatus" und einen Firmennamen
enthält, ist genau die Auskunft, gegen die die gesamte Plattform gebaut ist,
freiwillig verschickt und in Klartext.

Und es hilft nicht, das der Person zu überlassen („nimm halt deine private
Adresse"): die Registrierung ist offen für jede Adresse, viele werden die
dienstliche nehmen, und die Folgen trägt nicht, wer die Wahl getroffen hat,
sondern wer sie nicht überblickt hat.

**Entscheidung: eine Benachrichtigung sagt nicht, worum es geht.**

```
Betreff: Neuigkeiten auf WorkerTransfer
Text:    Es gibt etwas Neues für dich. Melde dich an: <link>
```

Kein Firmenname. Kein Vorgangstyp. Keine Anzahl. Der Inhalt lebt hinter der
Anmeldung — dort, wo er hingehört, weil dort geprüft wird, wer liest.

Das macht die Mail schwächer, und zwar spürbar: sie sagt nicht, ob sich das
Anmelden lohnt. Das ist der Preis, und er ist richtig herum bezahlt.

### Die Nebenwirkung, die man leicht übersieht

Auch **ohne** Inhalt verrät eine Mail etwas: ihren Zeitpunkt. Wer beobachtet,
wann WorkerTransfer schreibt, sieht, wann etwas passiert. Drei Mails an einem
Tag heißt: da läuft etwas.

Deshalb: **höchstens eine Benachrichtigung je Person und Stunde**, unabhängig
davon, wie viele Ereignisse in dieses Fenster fallen. Der Drosselung liegt nicht
Höflichkeit zugrunde, sondern dieselbe Überlegung wie dem Inhalt — sie nimmt der
Frequenz die Aussagekraft. Nebenbei macht sie den Endpunkt als Spam-Werkzeug
wertlos.

## Wo das lebt: identity-service, nicht ein eigener Dienst

Ein `notifications-service` wäre der erste Reflex und wäre falsch, aus einem
Grund: **er bräuchte die E-Mail-Adresse.** Die liegt im identity-service, und
sie dorthin zu kopieren oder über einen Lookup `subject_id → E-Mail`
herauszureichen, hieße, das empfindlichste Datum des Systems zu vervielfachen —
für eine Textmail.

identity-service besitzt die Adresse ohnehin, verschickt bereits Mails
(Bestätigung, Einladung) und hat den SMTP-Adapter. Ihm „sag dieser Person, dass
es etwas Neues gibt" hinzuzufügen, ist keine Ausweitung seiner Rolle, sondern
ihre Fortsetzung: **er ist der Dienst, der die Beziehung zur Person hält.** Die
Adresse verlässt ihn nie.

Auch die Einstellungen liegen dort — sie sind eine Eigenschaft des Kontos, wie
die Adresse.

## Wer darf jemanden benachrichtigen

Nicht jeder Angemeldete. Sonst wäre der Endpunkt ein Weg, beliebigen Menschen
Mails zu schicken.

**Ein gemeinsames Geheimnis zwischen den Diensten** (`WORKER_NOTIFY_SECRET`,
Header `X-Notify-Secret`). Die Dienste haben es aus der Umgebung, ein Browser
nicht. Das ist bewusst die einfachste Sache, die funktioniert, und ausdrücklich
eine Zwischenlösung: echte Dienstidentitäten gehören in die Härtung (Phase 10).
Bis dahin ist die Eigenschaft, auf die es ankommt, erfüllt — der Endpunkt ist
vom Netz her nicht der Öffentlichkeit ausgesetzt und trägt keine Nutzdaten, die
ein Missbrauch verwerten könnte.

**Ohne Geheimnis: `404`, nicht `401`.** Ein `401` bestätigt, dass es den
Endpunkt gibt.

## Arten und Einstellungen

```
NotificationKind = resume_request | market_request | application_update
                 | transfer_update
```

Vier Arten, je eine Einstellung, alle **standardmäßig an**. Das ist die einzige
Voreinstellung in diesem System, die nicht zurückhaltend ist, und sie hat einen
Grund: eine Benachrichtigung über den eigenen Vorgang ist keine Werbung, sondern
die Bedingung dafür, dass „die Person entscheidet" überhaupt eintreten kann.
Wer nicht erfährt, dass gefragt wurde, hat keine Wahl, sondern nur den Anschein.

Werbung gibt es nicht, deshalb gibt es auch keine Einstellung dafür.

**Kein Ereignis, keine Mail an Unternehmen.** Ein Unternehmen erfährt vom
Ausgang, indem es nachsieht. Es ist die Seite, die etwas will; ihr das Nachsehen
abzunehmen, ist keine Aufgabe dieses Systems, und eine Mail an einen
Firmenverteiler mit dem Namen einer Person wäre wieder genau der Leck-Kanal von
oben, nur andersherum.

## Domäne

```
NotificationPreference
  user_id   UUID (PK)
  resume_request       bool = true
  market_request       bool = true
  application_update   bool = true
  transfer_update      bool = true
  last_sent_at         datetime | None   ← die Drossel
```

`last_sent_at` steht **hier** und nicht in einem eigenen Tisch: es gibt genau
eine Drossel je Person, sie ist kein eigener Gegenstand, und ein zweiter Tisch
wäre ein Join für einen Zeitstempel.

## Endpunkte

| Methode | Pfad | Wer |
|---|---|---|
| `POST` | `/notifications` | ein Dienst (Geheimnis) |
| `GET` | `/me/notification-preferences` | die Person |
| `PUT` | `/me/notification-preferences` | dieselbe |

`POST /notifications` antwortet **immer `202`**, auch wenn nichts verschickt
wurde — abbestellt, gedrosselt, Adresse unbestätigt, Person unbekannt. Der
Aufrufer erfährt nichts darüber, ob und warum: er soll nicht aus einer Antwort
ableiten können, ob es diese Person gibt oder ob sie Mails will.

## Wie die Dienste rufen

**Feuern und vergessen.** Ein Fehlschlag beim Benachrichtigen darf **niemals**
den Vorgang scheitern lassen, der ihn ausgelöst hat.

Das ist die genaue Umkehrung der Consent-Regel (dort: im Zweifel schließen), und
sie stimmt aus demselben Grund, aus dem jene stimmt. Beim Ledger geht es um
Erlaubnis — im Zweifel nein. Hier geht es um Höflichkeit — und einen Widerruf
zurückzurollen, weil eine Mail nicht rausging, wäre grotesk.

Konkret: der Aufruf läuft **nach dem Commit**, in einer eigenen Aufgabe, und
jede Ausnahme wird geloggt und geschluckt. Vor dem Commit zu senden hieße, über
etwas zu benachrichtigen, das gleich zurückgerollt wird.

Der HTTP-Adapter wird je Dienst kopiert, wie der Consent-Adapter auch. Die
Dienste haben getrennte Datenbanken und getrennte Abhängigkeiten (ADR-0004); ein
gemeinsames Paket für vierzig Zeilen HTTP wäre ein Kopplungspunkt, dessen Preis
höher ist als die Kopie.

## Umsetzung in zwei Scheiben

**Scheibe A** — identity-service: Modell, Migration, drei Endpunkte, Drossel,
inhaltsfreie Mail, Integrationstests.

**Scheibe B** — die Rufe aus `resume-service`, `transfer-service` und
`applications-service`; Seite `/einstellungen` in `apps/web`; eine
Playwright-Erweiterung, die belegt, dass die Mail ankommt **und nichts verrät**.

## Selbstprüfung

*Ist eine Mail ohne Inhalt nicht nutzlos?* Sie ist weniger nützlich, ja. Aber
die nützlichere Variante kostet im schlimmsten Fall den Arbeitsplatz, und dieser
Fall ist nicht selten, sondern der Normalfall auf einem Transfermarkt: die
meisten Nutzer sind beschäftigt. Zwischen „weniger nützlich" und „gefährlich"
gibt es keine Abwägung.

*Warum nicht die Person wählen lassen, ob die Mail Details enthält?* Weil sie
die Folgen nicht überblicken kann und die Voreinstellung dann trotzdem
entscheidet — und weil eine solche Einstellung im Nachhinein wie eine
Schuldzuweisung aussieht. Wer Details will, meldet sich an; das ist ein Klick.

*Warum sitzt die Drossel bei einer Stunde?* Weil ein Fenster, das kürzer ist als
die Zeit zwischen zwei Sitzungen, nichts drosselt, und ein längeres eine echte
Nachricht verschluckt. Eine Stunde ist eine Wahl, keine Ableitung — sie steht
als Konstante an einer Stelle.

*Warum kein Ereignisbus?* Weil es noch keinen gibt (Phase 9) und ein direkter,
fehlertoleranter Aufruf dieselbe Zusage erfüllt: die Benachrichtigung darf
verloren gehen, ohne dass jemand Schaden nimmt. Genau deshalb ist sie der
richtige erste Konsument eines Busses, wenn er kommt — und nicht sein Grund.
