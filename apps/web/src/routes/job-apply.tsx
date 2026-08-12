import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Checkbox, Loading, Page, TextArea } from "@workertransfer/ui";

import { apply } from "../applications/client";
import type { MeResponse } from "../auth/client";
import { getJob } from "../jobs/client";
import { merkeStelle } from "../jobs/intent";
import { getMyProfile } from "../profile/client";
import { Requirements } from "../jobs/Requirements";

export interface JobApplyRouteProps {
  /** Injizierbar für den Test; sonst aus dem Pfad. */
  jobId?: string;
  principal?: MeResponse | null;
}

/** Geprüft, bevor die ID in eine Anfrage geht — sie kommt aus der Adresszeile. */
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function jobIdFromPath(): string {
  // /jobs/<uuid>/apply
  const parts = window.location.pathname.split("/").filter(Boolean);
  return parts[1] ?? "";
}

/**
 * Bewerben — auf einer eigenen Adresse, nicht in einer aufklappbaren Box.
 *
 * Vorher steckte dieses Formular in der Stellenkarte und klappte auf. Eine eigene
 * Route ist teilbar, überlebt ein Neuladen, und sie ist das Ziel, auf dem jemand
 * nach dem Anmelden landet — vorher landete er auf einer gefilterten Liste, die
 * eine Box aufklappte.
 *
 * Der Rückweg steht ausdrücklich da (`Page`-`back`): ein `<main>` ohne ihn ist auf
 * einem kalten Deep-Link eine Sackgasse.
 */
export function JobApplyRoute({ jobId, principal = null }: JobApplyRouteProps) {
  const wanted = jobId ?? jobIdFromPath();
  const gueltig = UUID.test(wanted);

  const [message, setMessage] = useState("");
  const [resume, setResume] = useState(true);
  const [portfolio, setPortfolio] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const job = useQuery({
    queryKey: ["jobs", "einzeln", wanted],
    queryFn: () => getJob(wanted),
    enabled: gueltig,
  });

  // Dieselbe Abfrage und derselbe Schlüssel wie auf der Trefferliste und der
  // Profilseite — die drei teilen sich einen Stand, statt dreimal zu fragen.
  const profileQuery = useQuery({
    queryKey: ["profile", "me"],
    queryFn: getMyProfile,
    enabled: principal !== null,
    staleTime: 5 * 60 * 1000,
  });

  // Drei Zustände, wie auf der Liste: `null` heißt „nichts zu vergleichen", `[]`
  // heißt „angemeldet, aber nichts eingetragen". Der Unterschied trägt — ohne
  // ihn behauptet die Seite eine Lücke, die sie nicht kennt.
  const mySkills: string[] | null =
    principal === null || !profileQuery.isSuccess ? null : (profileQuery.data?.skills ?? []);

  const send = useMutation({
    mutationFn: () =>
      apply({
        job_id: wanted,
        message,
        shares_resume: resume,
        shares_portfolio: portfolio,
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSent(true);
      } else {
        setError(result.message);
      }
    },
  });

  const zurueck = <a href="/jobs">Zurück zu den offenen Stellen</a>;

  if (!gueltig) {
    return (
      <Page title="Diese Stelle gibt es nicht" narrow back={zurueck}>
        <Card>
          <p>Die Adresse nennt keine gültige Stelle.</p>
        </Card>
      </Page>
    );
  }

  if (job.isPending) {
    return (
      <Page title="Bewerben" narrow back={zurueck}>
        <Card>
          <Loading label="Stelle wird geladen…" />
        </Card>
      </Page>
    );
  }

  const stelle = job.data;
  if (stelle === null || stelle === undefined) {
    // Eine zurückgezogene Stelle und eine, die es nie gab, sehen hier gleich
    // aus — und das ist richtig: welche von beiden es war, ist eine Aussage über
    // das Unternehmen, die niemand von uns erwarten kann.
    return (
      <Page title="Diese Stelle gibt es nicht" narrow back={zurueck}>
        <Card>
          <p>Sie wurde zurückgezogen, oder es gab sie nie.</p>
        </Card>
      </Page>
    );
  }

  if (sent) {
    return (
      <Page title="Bewerbung abgeschickt" narrow back={zurueck}>
        <Card>
          {/* Wo man es zurücknimmt, steht dort, wo man es getan hat — nicht in
              einer Hilfe, die man erst suchen muss. */}
          <p>
            Zurückziehen kannst du sie jederzeit unter{" "}
            <a href="/applications">Meine Bewerbungen</a> — dann sieht das Unternehmen deine Daten
            nicht mehr.
          </p>
        </Card>
      </Page>
    );
  }

  if (principal === null) {
    return (
      <Page title={stelle.title} narrow back={zurueck}>
        <Card>
          <p>Zum Bewerben brauchst du ein Konto — danach geht es hierher zurück.</p>
          {/* Erst merken, dann wechseln: derselbe Weg wie über den Knopf in der
              Trefferliste. Wer über die Kopfzeile zur Anmeldung geht, hat keine
              Absicht geäußert und wird auch nicht zurückgeworfen. */}
          <Button
            onClick={() => {
              merkeStelle(stelle.id, stelle.title);
              window.location.href = "/login";
            }}
          >
            Anmelden und bewerben
          </Button>
        </Card>
      </Page>
    );
  }

  return (
    <Page
      title={stelle.title}
      narrow
      back={zurueck}
      lead={stelle.location !== "" ? stelle.location : undefined}
    >
      <Card>
        {/* Die Passung steht auch hier: beim Formulieren hilft es zu sehen,
            welche Fähigkeit fehlt. Weiterhin eine Liste mit Haken, niemals eine
            Zahl (ADR-0022) — und wer nichts eingetragen hat, bekommt kein
            „0 von 3", sondern den Hinweis aufs Profil. */}
        <Requirements skills={stelle.skills} mine={mySkills} />
      </Card>

      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            send.mutate();
          }}
        >
          <TextArea
            label="Anschreiben"
            hint="Optional. Was dich mit dieser Stelle verbindet."
            rows={4}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            maxLength={4000}
          />
          {/* Das Profil steht bewusst NICHT zur Wahl: eine Bewerbung ohne jede
              Angabe zur Person ist keine, und ein Kästchen dafür wäre eine Wahl,
              die niemand ernsthaft trifft. */}
          <p className="wt-field__hint">
            Dein Profil geht immer mit — ohne es wäre es keine Bewerbung. Was du zusätzlich
            freigibst, entscheidest du:
          </p>
          {/* Kästchen und nicht Schalter: hier gilt die Freigabe mit dem
              Absenden, nicht sofort. Bei einer Einwilligung ist dieser
              Unterschied keine Kosmetik. */}
          <Checkbox
            label="Lebenslauf"
            checked={resume}
            onChange={(e) => setResume(e.target.checked)}
          />
          <Checkbox
            label="Meine Arbeiten"
            checked={portfolio}
            onChange={(e) => setPortfolio(e.target.checked)}
          />
          {error !== null ? <Alert>{error}</Alert> : null}
          <Button type="submit" disabled={send.isPending}>
            {send.isPending ? "Wird gesendet…" : "Bewerbung abschicken"}
          </Button>
        </form>
      </Card>
    </Page>
  );
}
