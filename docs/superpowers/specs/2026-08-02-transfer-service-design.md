# Sub-step 5.1 — Marktstatus: „bin ich ansprechbar?"

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler), [Applications-Design](2026-08-02-applications-service-design.md), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 5

## Die Zustandsliste im Plan mischt zwei Dinge

Der ULTRAPLAN nennt eine Zustandsmaschine:

```
Open → Listening → Unavailable → Under Contract → Transfer Listed →
Negotiating → Transferred
```

Beim Hinsehen sind das **drei verschiedene Gegenstände** in einer Reihe:

| Zustand | Wovon ist das eine Eigenschaft? |
|---|---|
| `Open`, `Listening`, `Unavailable` | **die Person** — was sie will |
| `Under Contract` | **ihr Arbeitsverhältnis** — eine Tatsache, keine Absicht |
| `Transfer Listed` | **der aktuelle Arbeitgeber** — was *er* will |
| `Negotiating`, `Transferred` | **ein einzelner Vorgang** zwischen drei Parteien |

Als ein Feld modelliert wäre das falsch, und zwar auf eine Weise, die man
später nicht mehr auseinanderbekommt: Eine Person, die „offen" ist und drei
Anfragen hat, wäre gleichzeitig `Open` und `Negotiating`. Eine, deren Transfer
scheitert, müsste von `Negotiating` „zurück" — wohin? Ein Feld, das drei
Subjekte beschreibt, hat keine sinnvollen Übergänge.

**Entscheidung: aufteilen.**

- **Marktstatus** (dieser Schnitt) — gehört der Person, hat drei Zustände.
- **Transfer** (5.2) — ein eigener Vorgang je Gespräch, mit eigenem Lebenszyklus,
  mehrere gleichzeitig möglich.
- **„Under Contract"** ist kein Zustand, sondern eine Angabe: *arbeite ich
  gerade irgendwo?* Sie steht als eigenes Feld neben dem Status, weil sie
  festlegt, welcher Weg gilt (siehe unten).
- **„Transfer Listed"** ist eine Aussage des Arbeitgebers über einen Menschen.
  Sie gehört nicht in diesen Schnitt und braucht eine eigene Abwägung — ein
  Unternehmen, das jemanden ohne dessen Wissen „auf die Liste setzt", ist genau
  das, wogegen diese Plattform gebaut ist.

## Marktstatus

```
MarketStatus
  subject_id   UUID (PK)
  status       OPEN | LISTENING | UNAVAILABLE
  employed     bool        arbeite ich gerade irgendwo?
  note         ≤ 500       was ich suche, in eigenen Worten
  updated_at
```

| Zustand | Bedeutung |
|---|---|
| `OPEN` | Ich suche aktiv. |
| `LISTENING` | Ich suche nicht, höre aber zu. |
| `UNAVAILABLE` | Gerade nicht. |

**Alle Übergänge sind erlaubt, in jede Richtung.** Es ist eine Aussage über den
eigenen Willen, und der ändert sich ohne Reihenfolge. Eine Zustandsmaschine mit
Verboten wäre hier Bevormundung: niemand muss erst „offen" gewesen sein, um
„zuhörend" zu werden.

**Der Anfangszustand ist `UNAVAILABLE`.** Wer nichts gesagt hat, hat nicht „ich
höre zu" gesagt. Die Voreinstellung darf nie zugunsten des Marktes ausfallen.

## Wer das sehen darf

**Niemand ohne Einwilligung** — und zwar strenger als beim Profil.

„Diese Person hört zu" ist die gefährlichste Angabe im ganzen System: sie kann
jemanden den Arbeitsplatz kosten, und anders als beim Lebenslauf reicht schon
die **Existenz** der Aussage, um Schaden anzurichten. Ein Lebenslauf verrät, wo
jemand war; der Marktstatus verrät, dass er weg will.

Deshalb: **nur empfängerbezogen**, nie öffentlich.

```
market.visibility:tenant:<tenant_id>
```

Es gibt bewusst **kein `market.visibility:public`**. Beim Profil ist „für alle
Unternehmen" eine sinnvolle Wahl; hier wäre sie ein Schalter, dessen Folgen
niemand überblickt — darunter der eigene Arbeitgeber, der auf derselben
Plattform ist.

Wie die Freigabe entsteht, entscheidet 5.2 (ein Transfer-Gespräch). In diesem
Schnitt gibt es sie über denselben Weg wie beim Lebenslauf: das Unternehmen
fragt, die Person antwortet. Der Code dafür existiert bereits als Muster.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `PUT` | `/market/me` | jede angemeldete Person | der gespeicherte Status |
| `GET` | `/market/me` | dieselbe | Status (nie `null` — siehe unten) |
| `GET` | `/market/{subject_id}` | Unternehmen mit Freigabe | der Status, sonst `404` |

**`GET /market/me` antwortet nie `null`.** Anders als beim Profil: dort ist
„noch keins" ein leeres Formular, hier ist „nichts gesagt" ein echter Zustand
mit einer Bedeutung — `UNAVAILABLE`. Ein `null` würde die Oberfläche zwingen,
sich eine Voreinstellung auszudenken, und die Gefahr ist, dass sie sich die
falsche ausdenkt.

Statuscodes wie gehabt: `404` für „gibt es nicht ODER nicht freigegeben"
(ununterscheidbar), `403` ohne aktives Unternehmen, `503` wenn der Ledger
schweigt.

## Abgrenzung

**Kein Transfer-Vorgang** — das ist 5.2, mit Angebot, Gebühr, Startdatum.
**Kein „Transfer Listed"** — siehe oben; ein eigener Schnitt mit eigener
Abwägung, wenn überhaupt.
**Keine KI-Beratung** (`worker-player-advisor`) — sie soll Entwürfe machen und
niemals verhandeln, und das ist eine eigene Entscheidung mit eigener
Einwilligung.
**Keine Verträge, keine Unterschrift.**

## Selbstprüfung

*Ist ein eigener Dienst für drei Zustände nicht zu viel?* Er ist die Wurzel des
Transfer-Vorgangs (5.2), und der ist das Differenzierungsmerkmal der Plattform.
Ihn ins Profil zu legen hieße, die harmloseste Sache (ein Aushang) mit der
gefährlichsten (die Wechselabsicht) in eine Datenbank zu legen — und in dieselbe
Freigabe zu geraten wäre nur eine Nachlässigkeit entfernt.

*Warum `employed` als Feld und nicht als Zustand?* Weil es keine Absicht ist.
Man kann beschäftigt und offen sein — das ist der Normalfall auf einem
Transfermarkt. Als Zustand würde es genau diesen Fall unmöglich machen.

*Warum darf man `UNAVAILABLE` sein und trotzdem eine Freigabe haben?* Weil die
Freigabe der Vergangenheit angehören kann: jemand hat mit einem Unternehmen
gesprochen und will jetzt nicht mehr. Das Unternehmen sieht dann `UNAVAILABLE` —
und das ist die richtige Auskunft, nicht `404`. Wer auch das nicht will,
widerruft die Freigabe; sie wirkt sofort (ADR-0013).
