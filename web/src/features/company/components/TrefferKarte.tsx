import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";

import { useAsync } from "../lib/useAsync";
import type { Beleg, Haken, Treffer } from "../api/scout";
import { requestResume } from "../api/resumeRequests";
import {
  type MarketRequest,
  getMarketStatus,
  requestMarketStatus,
} from "../../work/api/market";
import { expressInterest } from "../../work/api/transfers";
import { eroeffne } from "../../work/api/advisor";

/** Als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const ANSPRECHBARKEIT: Record<string, string> = {
  open: "kandidaten.ansprechbarOpen",
  listening: "kandidaten.ansprechbarListening",
  unavailable: "kandidaten.ansprechbarUnavailable",
};

/**
 * Ein Treffer, wie ein Unternehmen ihn sieht.
 *
 * <strong>Es steht keine Zahl auf dieser Karte, und es darf keine geben.</strong>
 * Kein Punktwert, keine Passung in Prozent, keine Rangfolge — und ausdrücklich
 * auch kein „2 von 3". Die Häkchenliste nennt, <em>welches</em> Wort fehlt; eine
 * Summe darüber verbärge genau das (ADR-0022, ADR-0036 Auflage 2).
 *
 * <strong>Häkchen und Belege kommen vom Server</strong>, aus `checks` und
 * `evidence` des Vertrags. Das ist Entscheidung 3 des ADR: läge die Liste nur
 * hier, rechnete der Browser sich aus den Rohdaten eine Zahl aus.
 *
 * <strong>Drei getrennte Türen, und keine öffnet die andere.</strong> Der
 * Lebenslauf wird angefragt, der Marktstatus wird angefragt, das Interesse wird
 * hinterlegt. Jede Anfrage beantwortet die Person einzeln.
 */
export function TrefferKarte({
  treffer,
  marketRequest,
  onGeaendert,
}: {
  treffer: Treffer;
  marketRequest: MarketRequest | undefined;
  onGeaendert: () => void;
}) {
  const { t } = useTranslation();

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h3" sx={{ mb: 0.5 }}>
          {treffer.headline}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {treffer.location !== ""
            ? treffer.location
            : t("kandidaten.ortFehlt")}
          {treffer.remote_ok ? t("kandidaten.remoteMoeglich") : null}
        </Typography>

        {treffer.bio !== "" ? (
          <Typography sx={{ mb: 1.5 }}>{treffer.bio}</Typography>
        ) : null}

        <Haekchenliste haken={treffer.checks} />
        <Erreichbarkeitshaken treffer={treffer} />

        {treffer.named.length > 0 ? (
          <Box sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", mb: 2 }}>
            {treffer.named.map((faehigkeit) => (
              <Chip key={faehigkeit} label={faehigkeit} size="small" />
            ))}
          </Box>
        ) : null}

        <Gespraechseroeffnung subjectId={treffer.subject_id} />
        <Lebenslaufanfrage subjectId={treffer.subject_id} />
        <Marktzugang
          subjectId={treffer.subject_id}
          anfrage={marketRequest}
          onGeaendert={onGeaendert}
        />
        <Belegliste belege={treffer.evidence} stand={treffer.evidence_state} />
      </CardContent>
    </Card>
  );
}

/**
 * Die Häkchenliste: ein Ja oder Nein je gesuchtem Wort.
 *
 * <strong>Und daneben keine Summe.</strong> Wer nichts gesucht hat, sieht hier
 * nichts — und ausdrücklich kein „0 von 0": nichts gesucht ist keine Aussage
 * über den Menschen auf dieser Karte.
 *
 * Die Reihenfolge ist die der Suche, nicht „erfüllt zuerst". Das wäre eine
 * Sortierung nach Passung im Kleinen — und der Anfang derselben im Grossen.
 */
function Haekchenliste({ haken }: { haken: Haken[] }) {
  const { t } = useTranslation();

  if (haken.length === 0) return null;

  return (
    <Box
      component="ul"
      // „Abgleich mit der Suche" und NICHT „Gesuchte Faehigkeiten": das Wort
      // „Faehigkeiten" steht schon am Suchfeld dieser Seite, und ein zweites
      // Element mit demselben Namensbestandteil macht `getByLabel` mehrdeutig.
      // Gemessen: sechs E2E-Reisen fielen an genau dieser Zeile.
      aria-label={t("kandidaten.haken")}
      sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", listStyle: "none", p: 0, mb: 1.5 }}
    >
      {haken.map((eintrag) => (
        <Chip
          component="li"
          key={eintrag.word}
          size="small"
          variant={eintrag.named ? "filled" : "outlined"}
          color={eintrag.named ? "success" : "default"}
          // Das Zeichen steht NEBEN dem Wort, nicht statt seiner: wer die Farbe
          // nicht sieht, liest trotzdem, was gesucht war und was fehlt.
          label={`${eintrag.word} ${eintrag.named ? "✓" : "✗"}`}
          aria-label={
            eintrag.named
              ? t("kandidaten.hakenGenannt", { wort: eintrag.word })
              : t("kandidaten.hakenFehlt", { wort: eintrag.word })
          }
        />
      ))}
    </Box>
  );
}

/**
 * Das Häkchen zur Entfernung — drei Zustände, keine Zahl (ADR-0041).
 *
 * <strong>Der Strich ist kein Nein.</strong> Er steht, wenn die Person nichts
 * gesagt hat, wenn keine Stelle gewählt wurde oder wenn ein Ort unbekannt ist —
 * und er sagt das auch, statt so auszusehen wie ein Kreuz. „Nichts gesagt" ist
 * nicht „passt nicht" (ADR-0022 §3).
 *
 * <strong>Daneben stehen die beiden Aussagen</strong>, nicht die Entfernung:
 * „bis 50 km" hat die Person getroffen, „hybrid" das Unternehmen. Beiden kann
 * man widersprechen — einer Kilometerzahl nicht.
 */
function Erreichbarkeitshaken({ treffer }: { treffer: Treffer }) {
  const { t } = useTranslation();

  // Ohne Stelle gäbe es nichts zu sagen — und ein Strich ohne Anlass wäre nur
  // eine leere Zeile auf jeder Karte.
  if (treffer.reach === "unsaid" && treffer.reach_attendance === null) return null;

  const zeichen = { reachable: "✓", further: "✗", unsaid: "—" }[treffer.reach];

  const gesagt = [
    treffer.reach_commute === null
      ? null
      : t("kandidaten.reachSagt", { stufe: t(`profil.pendeln_${treffer.reach_commute}`) }),
    treffer.reach_attendance === null
      ? null
      : t("kandidaten.reachStelle", {
          anwesenheit: t(`kandidaten.anwesenheit_${treffer.reach_attendance}`),
        }),
  ].filter((teil) => teil !== null);

  return (
    <Box sx={{ mb: 1.5 }}>
      <Chip
        size="small"
        variant={treffer.reach === "reachable" ? "filled" : "outlined"}
        color={treffer.reach === "reachable" ? "success" : "default"}
        label={`${t("kandidaten.reach")} ${zeichen}`}
        aria-label={t(`kandidaten.reach_${treffer.reach}`)}
      />
      {gesagt.length > 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
          {gesagt.join(" · ")}
        </Typography>
      ) : null}
    </Box>
  );
}

/**
 * Belege mit Herkunft und Link — und ein Satz, wo keine sind.
 *
 * <strong>Wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders</strong>
 * (ADR-0022 §3). Deshalb steht hier in jedem Fall ein Satz und nie eine leere
 * Box oder eine ausgegraute Karte. „Nichts freigegeben" und „gerade nicht
 * erreichbar" sind dabei zwei verschiedene Sätze: der zweite ist überhaupt
 * keine Aussage über die Person, sondern über uns.
 */
function Belegliste({ belege, stand }: { belege: Beleg[]; stand: Treffer["evidence_state"] }) {
  const { t } = useTranslation();

  const hinweis: Record<Treffer["evidence_state"], string | null> = {
    complete: null,
    partial: t("kandidaten.belegeUnvollstaendig"),
    none_released: t("kandidaten.belegeKeine"),
    unavailable: t("kandidaten.belegeUnerreichbar"),
  };

  const satz = hinweis[stand];

  return (
    <Box sx={{ mt: 2, pt: 2, borderTop: 1, borderColor: "divider" }}>
      <Typography variant="body2" sx={{ fontWeight: 600, mb: 0.5 }}>
        {t("kandidaten.belege")}
      </Typography>

      {belege.length > 0 ? (
        <Box sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", mb: satz === null ? 0 : 1 }}>
          {belege.map((beleg) => (
            <Chip
              key={`${beleg.origin}:${beleg.project}:${beleg.word}`}
              size="small"
              variant="outlined"
              clickable
              component="a"
              href={beleg.url}
              target="_blank"
              rel="noreferrer noopener"
              label={beleg.word}
              // Die Herkunft steht dabei: ein Topic hat ein Mensch geschrieben,
              // einen Sprachnamen hat GitHub erkannt. Ohne sie laese sich
              // beides wie eine Aussage ueber die Person.
              title={t(
                beleg.origin === "topic"
                  ? "kandidaten.belegTopic"
                  : "kandidaten.belegSprache",
                { projekt: beleg.project },
              )}
            />
          ))}
        </Box>
      ) : null}

      {satz !== null ? (
        <Typography variant="body2" color="text.secondary">
          {satz}
        </Typography>
      ) : null}
    </Box>
  );
}

/**
 * Ein Gespräch eröffnen (ADR-0037).
 *
 * <strong>Die sanftere der beiden Türen, und der Unterschied ist der Punkt.</strong>
 * „Interesse zeigen" legt sofort einen Transfer-Vorgang an und verlangt dafür,
 * dass die Person ihren Marktstatus freigegeben hat <em>und</em> gerade
 * ansprechbar ist. Ein Gespräch verlangt nur, was diese Karte ohnehin beweist:
 * eine Profil-Freigabe. Erst darin gibt die Person Stufe für Stufe mehr frei —
 * und die erste Stufe schliesst den Marktstatus ein. Der Weg führt also zur
 * anderen Tür, statt an ihr vorbei.
 *
 * <strong>Ein `404` sagt hier nichts über die Person.</strong> Sie hat nichts
 * freigegeben, sie hat dieses Unternehmen ausgeschlossen, oder es gibt sie
 * nicht — die drei sind ununterscheidbar, und diese Karte bastelt daraus keine
 * Auskunft, die der Server gerade verweigert hat.
 */
function Gespraechseroeffnung({ subjectId }: { subjectId: string }) {
  const { t } = useTranslation();
  const [meldung, setMeldung] = useState<string | null>(null);
  const [fertig, setFertig] = useState(false);
  const [running, setLaeuft] = useState(false);

  if (fertig) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {t("berater.eroeffnet")}
      </Typography>
    );
  }

  return (
    <Box sx={{ mb: 1 }}>
      {meldung !== null ? (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {meldung}
        </Alert>
      ) : null}
      <Button
        variant="text"
        size="small"
        disabled={running}
        onClick={() => {
          setLaeuft(true);
          void eroeffne(subjectId, t("kandidaten.interesseText")).then((result) => {
            setLaeuft(false);
            if (result.ok) {
              setMeldung(null);
              setFertig(true);
            } else {
              setMeldung(result.error.detail ?? result.error.title);
            }
          });
        }}
      >
        {running ? t("kandidaten.wirdGefragt") : t("berater.eroeffnen")}
      </Button>
    </Box>
  );
}

/**
 * Den Lebenslauf anfragen.
 *
 * <strong>Fragen darf nur, wer die Profil-Freigabe hat</strong> — und ob
 * überhaupt ein Lebenslauf existiert, verrät diese Tür nicht. „Hat schon einen
 * geschrieben" wäre eine Tatsache über die Person, nach der niemand tasten
 * können soll.
 */
function Lebenslaufanfrage({ subjectId }: { subjectId: string }) {
  const { t } = useTranslation();
  const [meldung, setMeldung] = useState<string | null>(null);
  const [asked, setGefragt] = useState(false);
  const [running, setLaeuft] = useState(false);

  if (asked) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {t("kandidaten.lebenslaufGefragt")}
      </Typography>
    );
  }

  return (
    <Box sx={{ mb: 1 }}>
      {meldung !== null ? (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {meldung}
        </Alert>
      ) : null}
      <Button
        variant="text"
        size="small"
        disabled={running}
        onClick={() => {
          setLaeuft(true);
          void requestResume(subjectId).then((result) => {
            setLaeuft(false);
            if (result.ok) {
              setMeldung(null);
              setGefragt(true);
            } else {
              setMeldung(result.error.detail);
            }
          });
        }}
      >
        {running ? t("kandidaten.wirdGefragt") : t("kandidaten.lebenslaufAnfragen")}
      </Button>
    </Box>
  );
}

/**
 * Marktstatus anfragen, und — erst nach Freigabe — Interesse hinterlegen.
 *
 * <strong>„Freigegeben" heisst nicht „einsehbar".</strong> Der Ledger kann im
 * selben Augenblick schweigen; dann steht hier ein eigener Satz statt eines
 * erfundenen Status. Und Interesse lässt sich nur zeigen, wenn die Person
 * gerade <em>ansprechbar</em> ist — die Freigabe allein reicht nicht.
 */
function Marktzugang({
  subjectId,
  anfrage,
  onGeaendert,
}: {
  subjectId: string;
  anfrage: MarketRequest | undefined;
  onGeaendert: () => void;
}) {
  const { t } = useTranslation();
  const [meldung, setMeldung] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const erteilt = anfrage?.status === "GRANTED";
  const current = useAsync(
    (signal) => getMarketStatus(subjectId, signal),
    [subjectId],
    erteilt,
  );

  const status = current.data?.ok === true ? current.data.status : null;

  return (
    <Box sx={{ mb: 1 }}>
      {meldung !== null ? (
        <Alert severity="info" sx={{ mb: 1 }}>
          {meldung}
        </Alert>
      ) : null}

      {anfrage === undefined ? (
        <Button
          variant="text"
          size="small"
          disabled={running}
          onClick={() => {
            setLaeuft(true);
            void requestMarketStatus(subjectId).then((result) => {
              setLaeuft(false);
              setMeldung(result.ok ? null : result.error.detail);
              onGeaendert();
            });
          }}
        >
          {running ? t("kandidaten.wirdGefragt") : t("kandidaten.marktAnfragen")}
        </Button>
      ) : null}

      {anfrage?.status === "PENDING" ? (
        <Typography variant="body2" color="text.secondary">
          {t("kandidaten.marktAngefragt")}
        </Typography>
      ) : null}

      {anfrage?.status === "DECLINED" ? (
        <Typography variant="body2" color="text.secondary">
          {t("kandidaten.marktAbgelehnt")}
        </Typography>
      ) : null}

      {erteilt && status === null && !current.pending ? (
        <Typography variant="body2" color="text.secondary">
          {t("kandidaten.marktNichtEinsehbar")}
        </Typography>
      ) : null}

      {status !== null ? (
        <>
          <Typography variant="body2" color="text.secondary">
            {t(ANSPRECHBARKEIT[status.availability] ?? status.availability)}
            {status.employed ? t("kandidaten.arbeitetGerade") : null}
          </Typography>
          {status.note !== "" ? (
            <Typography sx={{ mt: 0.5 }}>{status.note}</Typography>
          ) : null}
          {status.is_approachable ? (
            <Button
              variant="contained"
              size="small"
              sx={{ mt: 1 }}
              disabled={running}
              onClick={() => {
                setLaeuft(true);
                void expressInterest(
                  subjectId,
                  t("kandidaten.interesseText"),
                ).then(
                  (result: Awaited<ReturnType<typeof expressInterest>>) => {
                    setLaeuft(false);
                    setMeldung(
                      result.ok
                        ? t("kandidaten.interesseHinterlegt")
                        : result.error.detail,
                    );
                  },
                );
              }}
            >
              {running
                ? t("kandidaten.interesseLaeuft")
                : t("kandidaten.interesseZeigen")}
            </Button>
          ) : null}
        </>
      ) : null}
    </Box>
  );
}
