import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useState } from "react";
import { Alert, Button, Card, Field, Loading, Page, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Portfolio,
  type PortfolioItem,
  attachmentUrl,
  getMyPortfolio,
  saveMyPortfolio,
  uploadAttachment,
} from "../portfolio/client";

export interface PortfolioItemRouteProps {
  /**
   * Welche Arbeit — der Index im Feld, oder `"new"`.
   *
   * Der Index ist die einzige mögliche Adresse: `PortfolioItem` hat **keine ID**,
   * und gespeichert wird das ganze Feld (PUT). Der Preis steht im Befund: zwei
   * Tabs, die gleichzeitig etwas ändern, adressieren dann verschiedene Arbeiten.
   * Sichtbar bleibt es, weil der Titel der Arbeit in der Überschrift steht.
   */
  index?: string;
  principal?: MeResponse | null;
}

/** Was im Formular steht — alles als Text, damit „leer" nicht zu 0 wird. */
interface Draft {
  title: string;
  summary: string;
  url: string;
  role: string;
  year: string;
  /** Name der hochgeladenen Datei, vom Server vergeben. */
  attachment: string | null;
}

const EMPTY: Draft = { title: "", summary: "", url: "", role: "", year: "", attachment: null };

function toDraft(item: PortfolioItem): Draft {
  return {
    title: item.title,
    summary: item.summary,
    url: item.url ?? "",
    role: item.role,
    year: item.year === null ? "" : String(item.year),
    attachment: item.attachment,
  };
}

/**
 * Leere Felder werden `null`, nicht `""` oder `0`.
 *
 * Ein leerer String im URL-Feld würde später als Link gerendert und ins Nichts
 * führen; eine 0 im Jahr wäre eine Jahresangabe, die niemand gemeint hat.
 */
function toItem(draft: Draft): PortfolioItem {
  const year = draft.year.trim();
  return {
    title: draft.title,
    summary: draft.summary,
    url: draft.url.trim() === "" ? null : draft.url.trim(),
    role: draft.role,
    year: year === "" ? null : Number(year),
    attachment: draft.attachment,
  };
}

function indexFromPath(): string {
  // /portfolio/new oder /portfolio/<nummer>
  const parts = window.location.pathname.split("/").filter(Boolean);
  return parts[1] ?? "new";
}

/**
 * Eine Arbeit — anlegen oder ändern, auf einer eigenen Adresse.
 *
 * Vorher standen alle Arbeiten gleichzeitig als aufgeklappte Formulare auf einer
 * Seite. Arbeiten sind aber voneinander unabhängig: man schreibt eine, nicht
 * drei im Vergleich. (Beim Lebenslauf ist es umgekehrt, und deshalb bleiben die
 * Stationen ein Formular — dort ist die Reihenfolge die Aussage und die Lücke
 * zwischen zwei Stationen das, was man sehen muss.)
 *
 * Gespeichert wird weiterhin das ganze Feld: der Vertrag kennt nur PUT. Diese
 * Seite lädt es, tauscht einen Eintrag und schickt es zurück.
 */
export function PortfolioItemRoute({ index, principal = null }: PortfolioItemRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const wanted = index ?? indexFromPath();
  const isNew = wanted === "new";

  const portfolioQuery = useQuery({
    queryKey: ["portfolio", "me"],
    queryFn: getMyPortfolio,
    enabled: subjectId !== null,
  });

  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);

  const portfolio: Portfolio | null | undefined = portfolioQuery.data;
  const items = portfolio?.items ?? [];
  const position = isNew ? items.length : Number(wanted);
  const existing = isNew ? undefined : items[position];

  // Das Formular folgt dem Server, solange niemand getippt hat. Danach gehört
  // der Zustand der Person — sonst verliert sie ihre Eingabe, sobald eine
  // Abfrage neu auflöst.
  useEffect(() => {
    if (!touched && existing !== undefined) setDraft(toDraft(existing));
  }, [existing, touched]);

  function update(patch: Partial<Draft>) {
    setTouched(true);
    setDraft((current) => ({ ...current, ...patch }));
  }

  /** Speichern heißt: das ganze Feld mit genau einem geänderten Eintrag. */
  const save = useMutation({
    mutationFn: () => {
      const next = [...items];
      if (isNew) next.push(toItem(draft));
      else next[position] = toItem(draft);
      return saveMyPortfolio(next);
    },
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        queryClient.setQueryData(["portfolio", "me"], result.portfolio);
        window.location.href = "/portfolio";
      } else {
        setError(result.message);
      }
    },
  });

  const remove = useMutation({
    mutationFn: () => saveMyPortfolio(items.filter((_, i) => i !== position)),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        queryClient.setQueryData(["portfolio", "me"], result.portfolio);
        window.location.href = "/portfolio";
      } else {
        setError(result.message);
      }
    },
  });

  const zurueck = <a href="/portfolio">Zurück zu meinen Arbeiten</a>;

  if (subjectId === null) {
    return (
      <Page title="Arbeit bearbeiten" narrow back={zurueck}>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Arbeiten zu bearbeiten.
          </p>
        </Card>
      </Page>
    );
  }

  if (portfolioQuery.isPending) {
    return (
      <Page title={isNew ? "Neue Arbeit" : "Arbeit bearbeiten"} narrow back={zurueck}>
        <Card>
          <Loading label="Arbeiten werden geladen…" />
        </Card>
      </Page>
    );
  }

  if (!isNew && existing === undefined) {
    // Eine Adresse, hinter der nichts steht — etwa nach dem Entfernen in einem
    // anderen Tab. Ein leeres Formular anzubieten würde daraus stillschweigend
    // eine NEUE Arbeit machen, und die Person hätte eine angelegt, ohne es zu
    // wollen.
    return (
      <Page title="Diese Arbeit gibt es nicht" narrow back={zurueck}>
        <Card>
          <p>Sie wurde entfernt, oder die Adresse zeigt auf eine Stelle in der Liste, die es nicht gibt.</p>
        </Card>
      </Page>
    );
  }

  return (
    <Page
      // Der Titel der Arbeit steht in der Überschrift: wer `/portfolio/2`
      // öffnet und dort etwas anderes liest als erwartet, sieht es sofort. Das
      // ist die Gegenmaßnahme zur fehlenden ID.
      title={isNew ? "Neue Arbeit" : draft.title !== "" ? draft.title : "Arbeit bearbeiten"}
      narrow
      back={zurueck}
    >
      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <Field
            label="Titel"
            value={draft.title}
            onChange={(e) => update({ title: e.target.value })}
            maxLength={160}
            required
          />
          <TextArea
            label="Worum es geht"
            hint="Was es ist und was du daran gemacht hast."
            rows={4}
            value={draft.summary}
            onChange={(e) => update({ summary: e.target.value })}
            maxLength={1000}
          />
          <Field
            label="Link"
            type="url"
            hint="Optional, und nur http oder https."
            placeholder="https://…"
            value={draft.url}
            onChange={(e) => update({ url: e.target.value })}
          />
          <Field
            label="Deine Rolle"
            value={draft.role}
            onChange={(e) => update({ role: e.target.value })}
            maxLength={160}
          />
          <Field
            label="Jahr"
            inputMode="numeric"
            placeholder="2024"
            value={draft.year}
            onChange={(e) => update({ year: e.target.value })}
          />
          <AttachmentField
            subjectId={subjectId}
            name={draft.attachment}
            onUploaded={(name) => update({ attachment: name })}
            onError={setError}
          />

          {error !== null ? <Alert>{error}</Alert> : null}

          <Button type="submit" disabled={save.isPending || remove.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
          {/* Das Entfernen steht HIER und nicht in der Liste: hier hat die
              Person die Arbeit vor sich und sieht, was verschwindet. In der
              Liste wäre es ein Knopf neben einer Zeile, und Zeilen verwechselt
              man. */}
          {!isNew ? (
            <Button
              type="button"
              variant="quiet"
              onClick={() => remove.mutate()}
              disabled={save.isPending || remove.isPending}
            >
              {remove.isPending ? "Wird entfernt…" : "Diese Arbeit entfernen"}
            </Button>
          ) : null}
        </form>
      </Card>
    </Page>
  );
}

/**
 * Eine Datei anhängen.
 *
 * Der lokale Dateiname wird nicht angezeigt: er wandert nicht zum Server, und
 * ihn hier zu zeigen würde suggerieren, dass er das täte. Sichtbar ist, was
 * wahr ist — dass eine Datei hängt, und wohin sie zeigt.
 *
 * Hochgeladen wird sofort, gespeichert erst mit dem Formular. Ein Anhang, den
 * niemand mehr referenziert, wird beim nächsten Speichern aufgeräumt — deshalb
 * kostet ein Wechsel nichts.
 *
 * Bleibt handgebaut, obwohl das Designsystem sonst überall greift: es hat kein
 * Dateifeld, und es gibt genau EINEN Aufrufer. Ein Primitiv für einen Aufrufer
 * wäre eine Vermutung darüber, wie der zweite aussieht.
 */
function AttachmentField({
  subjectId,
  name,
  onUploaded,
  onError,
}: {
  subjectId: string;
  name: string | null;
  onUploaded: (name: string) => void;
  onError: (message: string) => void;
}) {
  const [busy, setBusy] = useState(false);
  // htmlFor statt eines umschließenden <label>: die Hinweise darunter würden
  // sonst Teil des zugänglichen Namens, und der lautete dann „Datei Optional.
  // PNG, JPEG oder PDF, höchstens 5 MB."
  const id = useId();

  async function onPick(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Auch bei einem abgebrochenen Dialog feuert change — dann gibt es keine
    // Datei, und es ist nichts passiert.
    if (file === undefined) return;
    setBusy(true);
    const result = await uploadAttachment(file);
    setBusy(false);
    // Das Feld zurücksetzen, damit dieselbe Datei erneut gewählt werden kann:
    // ohne das feuert change beim zweiten Mal nicht.
    event.target.value = "";
    if (result.ok) onUploaded(result.name);
    else onError(result.message);
  }

  return (
    <div className="wt-field">
      <label className="wt-field__label" htmlFor={id}>
        Datei
      </label>
      <input id={id} type="file" accept="image/png,image/jpeg,application/pdf" onChange={onPick} />
      <p className="wt-field__hint">
        {busy
          ? "Wird hochgeladen…"
          : name === null
            ? "Optional. PNG, JPEG oder PDF, höchstens 5 MB."
            : null}
      </p>
      {name !== null && !busy ? (
        <p className="wt-field__hint">
          Datei angehängt —{" "}
          <a href={attachmentUrl(subjectId, name)} target="_blank" rel="noreferrer noopener">
            ansehen
          </a>
        </p>
      ) : null}
    </div>
  );
}
