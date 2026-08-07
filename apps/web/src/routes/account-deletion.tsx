import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { requestErasure } from "../account/client";
import { useCompanies } from "../auth/companies";
import type { MeResponse } from "../auth/client";
import { SESSION_QUERY_KEY } from "../auth/session";

export interface AccountDeletionRouteProps {
  principal?: MeResponse | null;
}

/**
 * Konto löschen — die Seite, an der die Zusage aus ADR-0027 eingelöst wird.
 *
 * **Der Text steht VOR dem Knopf, nicht in einer Datenschutzerklärung.**
 * Dieselbe Regel wie beim Entwurfsdienst (ADR-0024): wer hier drückt, soll an
 * dieser Stelle wissen, was passiert — nicht drei Klicks entfernt.
 *
 * **Er verspricht keine Ausnahme.** In der Voreinstellung bleibt nichts stehen,
 * auch nicht die Bewerbung, über die jemand eingestellt wurde (§3). Ein Satz
 * wie „manches müssen wir aufbewahren" wäre eine Zusage über einen Schalter,
 * der auf AUS steht — und niemand würde je prüfen, ob sie noch stimmt.
 *
 * **Bewusst, aber ohne Hürdenlauf.** Zwei Schritte mit anders formuliertem
 * zweitem Knopf, dazwischen die Folge noch einmal. Kein abzutippendes Wort,
 * keine Bedenkzeit, kein erneutes Passwort: wer löschen will, darf das.
 *
 * **Und danach kein Fortschrittsbalken.** Wer sich noch anmelden könnte, um
 * zuzusehen, hätte ein Konto, das noch funktioniert — und genau das soll nicht
 * mehr stimmen (§6). Die Auskunft kommt in EINER Nachricht am Ende.
 */
export function AccountDeletionRoute({ principal = null }: AccountDeletionRouteProps) {
  const queryClient = useQueryClient();
  // Nur für die Auskunft aus §7 — wer kein Unternehmen hat, bekommt dazu auch
  // keinen Absatz zu lesen.
  const { companies } = useCompanies();
  const [asking, setAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);

  const erase = useMutation({
    mutationFn: requestErasure,
    onSuccess: (result) => {
      if (!result.ok) {
        // Der Weg bleibt offen: ein Netzfehler ist kein Grund, jemanden mit
        // einem halb gedrückten Knopf stehen zu lassen.
        setError(result.message);
        return;
      }
      setError(null);
      setAccepted(true);
      // Das Konto ist ab jetzt gesperrt und jede Sitzung widerrufen. Die
      // Oberfläche zieht nach, statt so zu tun, als wäre noch jemand
      // angemeldet.
      queryClient.setQueryData(SESSION_QUERY_KEY, null);
    },
  });

  // ZUERST der Erfolg, DANN die Anmeldeaufforderung — die Reihenfolge ist
  // tragend, nicht Geschmack.
  //
  // Bei Erfolg räumt diese Seite die Sitzung weg (das Konto IST gesperrt), und
  // die Hülle rendert sie sofort mit `principal = null` neu. Stünde die
  // Anmeldeaufforderung zuerst, sähe die Person nach dem Löschen „Bitte
  // anmelden, um dein Konto zu löschen" — als wäre nichts geschehen, beim
  // unwiderruflichsten Vorgang des Systems. Genau so ist es in der E2E-Reise
  // aufgefallen, während die Komponententests grün waren: die übergeben
  // `principal` als feste Eigenschaft, die sich nie ändert.
  if (accepted) {
    return (
      <main className="page page--narrow">
        <header className="page__header">
          <h1>Konto löschen</h1>
        </header>
        <Card>
          <p role="status">
            <strong>Deine Löschung ist angenommen und läuft.</strong> Du bist abgemeldet, und
            unter deinem Namen passiert ab jetzt nichts mehr. Wir schicken dir{" "}
            <strong>eine einzige E-Mail</strong>, sobald alles gelöscht ist — bis dahin gibt es
            hier nichts mehr zu sehen.
          </p>
          <p className="page__note">
            Dass es etwas dauert, hat einen einfachen Grund: deine Daten liegen bei mehreren
            Diensten, und jeder muss den Auftrag bestätigen. Erreicht er einen davon nicht, gilt
            die Löschung nicht als fertig — und du bekommst keine Nachricht, die nicht stimmt.
          </p>
        </Card>
      </main>
    );
  }

  if (principal === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Konto löschen</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein Konto zu löschen.
          </p>
        </Card>
      </main>
    );
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Konto löschen</h1>
        <p className="page__lead">
          Hier wird dein Konto gelöscht — <strong>unwiderruflich</strong>. Es gibt keinen
          Papierkorb und keine Frist, in der du es dir noch anders überlegen kannst. Was hier
          steht, gilt; deshalb steht es hier und nicht im Kleingedruckten.
        </p>
      </header>

      <Card>
        <h2>Was gelöscht wird</h2>
        <ul className="overview">
          <li>Dein Konto: E-Mail-Adresse, Passwort, Anzeigename.</li>
          <li>Dein Profil mit Überschrift, Text, Ort und Fähigkeiten.</li>
          <li>Dein Lebenslauf mit allen Stationen und Ausbildungen.</li>
          <li>Deine Arbeiten samt hochgeladener Dateien.</li>
          <li>Dein Marktstatus und alle Gespräche über einen Wechsel.</li>
          <li>Deine Bewerbungen samt der Anschreiben, die du geschrieben hast.</li>
          <li>Deine GitHub-Verbindung.</li>
          <li>Deine Benachrichtigungs-Einstellungen.</li>
        </ul>
        <p className="requests__meta">
          <strong>Es bleibt nichts davon stehen.</strong> Auch nicht die Bewerbung, über die du{" "}
          <strong>eingestellt</strong> wurdest — auch die verschwindet aus der Liste des
          Unternehmens. Das ist so gewollt: die Unterlage über ein Arbeitsverhältnis ist dein
          Vertrag beim Arbeitgeber, nicht eine Zeile bei einer Vermittlungsplattform.
        </p>
      </Card>

      <Card>
        <h2>Was bleibt — und warum</h2>
        <p className="requests__meta">
          Ein Nachweis darüber, <em>dass</em> gelöscht wurde: welche Freigaben unter deiner
          Kennung einmal erteilt und wann sie zurückgenommen wurden. Ohne ihn ließe sich nicht
          mehr belegen, dass wir deiner Löschung nachgekommen sind. Was du selbst
          hineingeschrieben hast — etwa der Grund für eine zurückgenommene Freigabe — wird dabei
          entfernt. Übrig bleiben Kennungen und Zeitpunkte, die auf keinen Menschen mehr zeigen.
        </p>
      </Card>

      {companies.length > 0 ? (
        <Card>
          <h2>Und deine Unternehmen</h2>
          <ul className="overview">
            {companies.map((company) => (
              <li key={company.id}>
                <strong>{company.name}</strong>{" "}
                {company.role === "admin" ? (
                  <>
                    — bist du die <em>letzte</em> Person mit Verwaltungsrechten, wird das
                    Unternehmen <strong>stillgelegt</strong> und seine Stellenanzeigen werden
                    zurückgezogen. Eine Anzeige, hinter der niemand mehr steht, ist schlechter
                    als keine: Bewerbungen liefen an niemanden.
                  </>
                ) : (
                  <>
                    — deine Mitgliedschaft endet. Das Unternehmen selbst bleibt, wie es ist.
                  </>
                )}
              </li>
            ))}
          </ul>
          <p className="page__note">
            Das hält deine Löschung <strong>nicht auf</strong>. Du musst niemandem vorher etwas
            übergeben: dein Recht auf Löschung hängt nicht daran, ob sich jemand anderes um ein
            Unternehmen kümmert.
          </p>
        </Card>
      ) : null}

      <Card>
        <h2>Wie es abläuft</h2>
        <p className="requests__meta">
          Mit dem Klick bist du <strong>sofort abgemeldet</strong> und deine Sitzungen sind
          widerrufen. Die Löschung selbst läuft danach weiter — sie geht{" "}
          <strong>nicht sofort</strong> durch, weil deine Daten bei mehreren Diensten liegen und
          jeder einzeln bestätigen muss. Du bekommst genau eine E-Mail, wenn alles erledigt ist.
        </p>
        <p className="page__note">
          Du kannst deine Daten vorher <a href="/meine-daten">unter „Meine Daten"</a>{" "}
          herunterladen. Musst du aber nicht — wer löschen will, darf das ohne Umweg.
        </p>
      </Card>

      <Card>
        {error !== null ? (
          <p className="auth__alert" role="alert">
            {error}
          </p>
        ) : null}

        {asking ? (
          <>
            <p className="auth__alert" role="alert">
              Letzte Frage: dein Konto und alles oben Genannte werden gelöscht. Das lässt sich
              nicht rückgängig machen.
            </p>
            <div className="form__actions">
              <Button onClick={() => erase.mutate()} disabled={erase.isPending}>
                {erase.isPending ? "Wird angenommen…" : "Ja, endgültig löschen"}
              </Button>
              <Button
                variant="quiet"
                onClick={() => {
                  setAsking(false);
                  setError(null);
                }}
                disabled={erase.isPending}
              >
                Abbrechen
              </Button>
            </div>
          </>
        ) : (
          <Button variant="secondary" onClick={() => setAsking(true)}>
            Konto löschen
          </Button>
        )}
      </Card>
    </main>
  );
}
