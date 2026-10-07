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
import type { Evidence, Hit, Tick } from "../api/scout";
import { requestResume } from "../api/resumeRequests";
import {
  type MarketRequest,
  getMarketStatus,
  requestMarketStatus,
} from "../../work/api/market";
import { COMPANY_TRANSFER_CASES, expressInterest } from "../../work/api/transfers";
import { failureKey, RECRUITER_CASES } from "../../../shared/api/errors";
import { openConversation } from "../../work/api/advisor";

/** As catalogue keys — the wording lies in the catalogues. */
const APPROACHABILITY: Record<string, string> = {
  open: "scout.approachableOpen",
  listening: "scout.approachableListening",
  unavailable: "scout.approachableUnavailable",
};

/**
 * A hit as a company sees it.
 *
 * <strong>No number stands on this card, and none may.</strong> No score, no
 * fit in percent, no ranking — and explicitly no "2 of 3" either. The tick list
 * names <em>which</em> word is missing; a sum over it would hide exactly that
 * (ADR-0022, ADR-0036 condition 2).
 *
 * <strong>Ticks and evidence come from the server</strong>, from `checks` and
 * `evidence` of the contract. That is decision 3 of the ADR: if the list lay
 * only here, the browser would compute a number from the raw data.
 *
 * <strong>Three separate doors, and none opens the other.</strong> The CV is
 * requested, the market status is requested, the interest is recorded. The
 * person answers each request separately.
 */
export function HitCard({
  hit,
  marketRequest,
  onChanged,
}: {
  hit: Hit;
  marketRequest: MarketRequest | undefined;
  onChanged: () => void;
}) {
  const { t } = useTranslation();

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h3" sx={{ mb: 0.5 }}>
          {hit.headline}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {hit.location !== ""
            ? hit.location
            : t("scout.locationMissing")}
          {hit.remote_ok ? t("scout.remotePossible") : null}
        </Typography>

        {hit.bio !== "" ? (
          <Typography sx={{ mb: 1.5 }}>{hit.bio}</Typography>
        ) : null}

        <TickList ticks={hit.checks} />
        <AvailabilityTick hit={hit} />

        {hit.named.length > 0 ? (
          <Box sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", mb: 2 }}>
            {hit.named.map((skill) => (
              <Chip key={skill} label={skill} size="small" />
            ))}
          </Box>
        ) : null}

        <ConversationOpener subjectId={hit.subject_id} />
        <ResumeRequestButton subjectId={hit.subject_id} />
        <MarketAccess
          subjectId={hit.subject_id}
          requestRecord={marketRequest}
          onChanged={onChanged}
        />
        <EvidenceList evidence={hit.evidence} state={hit.evidence_state} />
      </CardContent>
    </Card>
  );
}

/**
 * The tick list: a yes or no per searched word.
 *
 * <strong>And no sum beside it.</strong> Whoever searched nothing sees nothing
 * here — and explicitly no "0 of 0": nothing searched is no statement about the
 * person on this card.
 *
 * The order is that of the search, not "met first". That would be sorting by
 * fit in the small — and the beginning of the same in the large.
 */
function TickList({ ticks }: { ticks: Tick[] }) {
  const { t } = useTranslation();

  if (ticks.length === 0) return null;

  return (
    <Box
      component="ul"
      // "Abgleich mit der Suche" and NOT "Gesuchte Faehigkeiten": the word
      // "Faehigkeiten" already stands at this page's search field, and a second
      // element with the same name part makes `getByLabel` ambiguous.
      // Measured: six E2E journeys fell at exactly this line.
      aria-label={t("scout.ticks")}
      sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", listStyle: "none", p: 0, mb: 1.5 }}
    >
      {ticks.map((tick) => (
        <Chip
          component="li"
          key={tick.word}
          size="small"
          variant={tick.named ? "filled" : "outlined"}
          color={tick.named ? "success" : "default"}
          // The mark stands BESIDE the word, not instead of it: whoever does not
          // see the colour still reads what was searched and what is missing.
          label={`${tick.word} ${tick.named ? "✓" : "✗"}`}
          aria-label={
            tick.named
              ? t("scout.tickNamed", { wort: tick.word })
              : t("scout.tickMissing", { wort: tick.word })
          }
        />
      ))}
    </Box>
  );
}

/**
 * The tick for the distance — three states, no number (ADR-0041).
 *
 * <strong>The dash is not a no.</strong> It stands when the person said
 * nothing, when no job was chosen or when a place is unknown — and it says so,
 * instead of looking like a cross. "Said nothing" is not "does not fit"
 * (ADR-0022 §3).
 *
 * <strong>Beside it stand the two statements</strong>, not the distance: "bis
 * 50 km" was made by the person, "hybrid" by the company. Both can be
 * contradicted — a kilometre figure cannot.
 */
function AvailabilityTick({ hit }: { hit: Hit }) {
  const { t } = useTranslation();

  // Without a job there would be nothing to say — and a dash without a reason
  // would only be an empty line on every card.
  if (hit.reach === "unsaid" && hit.reach_attendance === null) return null;

  const mark = { reachable: "✓", further: "✗", unsaid: "—" }[hit.reach];

  const stated = [
    hit.reach_commute === null
      ? null
      : t("scout.reachStated", { stufe: t(`profile.commute_${hit.reach_commute}`) }),
    hit.reach_attendance === null
      ? null
      : t("scout.reachJob", {
          anwesenheit: t(`scout.attendance_${hit.reach_attendance}`),
        }),
  ].filter((part) => part !== null);

  return (
    <Box sx={{ mb: 1.5 }}>
      <Chip
        size="small"
        variant={hit.reach === "reachable" ? "filled" : "outlined"}
        color={hit.reach === "reachable" ? "success" : "default"}
        label={`${t("scout.reach")} ${mark}`}
        aria-label={t(`scout.reach_${hit.reach}`)}
      />
      {stated.length > 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
          {stated.join(" · ")}
        </Typography>
      ) : null}
    </Box>
  );
}

/**
 * Evidence with origin and link — and a sentence where there is none.
 *
 * <strong>Whoever has nothing on GitHub is not worse but elsewhere</strong>
 * (ADR-0022 §3). That is why a sentence stands here in every case and never an
 * empty box or a greyed-out card. "Nothing released" and "not reachable right
 * now" are two different sentences: the second is no statement about the
 * person at all, but about us.
 */
function EvidenceList({ evidence, state }: { evidence: Evidence[]; state: Hit["evidence_state"] }) {
  const { t } = useTranslation();

  const hints: Record<Hit["evidence_state"], string | null> = {
    complete: null,
    partial: t("scout.evidenceIncomplete"),
    none_released: t("scout.evidenceNone"),
    unavailable: t("scout.evidenceUnavailable"),
  };

  const sentence = hints[state];

  return (
    <Box sx={{ mt: 2, pt: 2, borderTop: 1, borderColor: "divider" }}>
      <Typography variant="body2" sx={{ fontWeight: 600, mb: 0.5 }}>
        {t("scout.evidence")}
      </Typography>

      {evidence.length > 0 ? (
        <Box sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", mb: sentence === null ? 0 : 1 }}>
          {evidence.map((item) => (
            <Chip
              key={`${item.origin}:${item.project}:${item.word}`}
              size="small"
              variant="outlined"
              clickable
              component="a"
              href={item.url}
              target="_blank"
              rel="noreferrer noopener"
              label={item.word}
              // The origin stands with it: a topic was written by a human, a
              // language name was recognised by GitHub. Without it both would
              // read like a statement about the person.
              title={t(
                item.origin === "topic"
                  ? "scout.evidenceTopic"
                  : "scout.evidenceLanguage",
                { projekt: item.project },
              )}
            />
          ))}
        </Box>
      ) : null}

      {sentence !== null ? (
        <Typography variant="body2" color="text.secondary">
          {sentence}
        </Typography>
      ) : null}
    </Box>
  );
}

/**
 * Open a conversation (ADR-0037).
 *
 * <strong>The gentler of the two doors, and the difference is the point.</strong>
 * "Show interest" creates a transfer at once and requires that the person
 * released their market status <em>and</em> is approachable right now. A
 * conversation requires only what this card proves anyway: a profile release.
 * Only within it does the person release more, stage by stage — and the first
 * stage includes the market status. So the path leads to the other door
 * instead of past it.
 *
 * <strong>A `404` says nothing about the person here.</strong> They released
 * nothing, they excluded this company, or they do not exist — the three are
 * indistinguishable, and this card makes no statement out of it that the server
 * just refused.
 */
function ConversationOpener({ subjectId }: { subjectId: string }) {
  const { t } = useTranslation();
  const [message, setMessage] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [running, setRunning] = useState(false);

  if (done) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {t("advisor.opened")}
      </Typography>
    );
  }

  return (
    <Box sx={{ mb: 1 }}>
      {message !== null ? (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {message}
        </Alert>
      ) : null}
      <Button
        variant="text"
        size="small"
        disabled={running}
        onClick={() => {
          setRunning(true);
          void openConversation(subjectId, t("scout.interestText")).then((result) => {
            setRunning(false);
            if (result.ok) {
              setMessage(null);
              setDone(true);
            } else {
              setMessage(t(failureKey(result.error, "errors.conversationNotOpened", RECRUITER_CASES)));
            }
          });
        }}
      >
        {running ? t("scout.requesting") : t("advisor.open")}
      </Button>
    </Box>
  );
}

/**
 * Request the CV.
 *
 * <strong>Only whoever holds the profile release may ask</strong> — and whether
 * a CV exists at all, this door does not reveal. "Has already written one"
 * would be a fact about the person nobody should be able to probe for.
 */
function ResumeRequestButton({ subjectId }: { subjectId: string }) {
  const { t } = useTranslation();
  const [message, setMessage] = useState<string | null>(null);
  const [asked, setAsked] = useState(false);
  const [running, setRunning] = useState(false);

  if (asked) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {t("scout.resumeRequested")}
      </Typography>
    );
  }

  return (
    <Box sx={{ mb: 1 }}>
      {message !== null ? (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {message}
        </Alert>
      ) : null}
      <Button
        variant="text"
        size="small"
        disabled={running}
        onClick={() => {
          setRunning(true);
          void requestResume(subjectId).then((result) => {
            setRunning(false);
            if (result.ok) {
              setMessage(null);
              setAsked(true);
            } else {
              setMessage(t(failureKey(result.error, "errors.requestNotSent", RECRUITER_CASES)));
            }
          });
        }}
      >
        {running ? t("scout.requesting") : t("scout.requestResume")}
      </Button>
    </Box>
  );
}

/**
 * Request the market status, and — only after release — record interest.
 *
 * <strong>"Released" does not mean "visible".</strong> The ledger can be silent
 * at the same moment; then a sentence of its own stands here instead of an
 * invented status. And interest can only be shown when the person is
 * <em>approachable</em> right now — the release alone is not enough.
 */
function MarketAccess({
  subjectId,
  requestRecord,
  onChanged,
}: {
  subjectId: string;
  requestRecord: MarketRequest | undefined;
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const [message, setMessage] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const granted = requestRecord?.status === "GRANTED";
  const current = useAsync(
    (signal) => getMarketStatus(subjectId, signal),
    [subjectId],
    granted,
  );

  const status = current.data?.ok === true ? current.data.status : null;

  return (
    <Box sx={{ mb: 1 }}>
      {message !== null ? (
        <Alert severity="info" sx={{ mb: 1 }}>
          {message}
        </Alert>
      ) : null}

      {requestRecord === undefined ? (
        <Button
          variant="text"
          size="small"
          disabled={running}
          onClick={() => {
            setRunning(true);
            void requestMarketStatus(subjectId).then((result) => {
              setRunning(false);
              setMessage(result.ok ? null : t(failureKey(result.error, "errors.requestNotSent", RECRUITER_CASES)));
              onChanged();
            });
          }}
        >
          {running ? t("scout.requesting") : t("scout.requestMarket")}
        </Button>
      ) : null}

      {requestRecord?.status === "PENDING" ? (
        <Typography variant="body2" color="text.secondary">
          {t("scout.marketRequested")}
        </Typography>
      ) : null}

      {requestRecord?.status === "DECLINED" ? (
        <Typography variant="body2" color="text.secondary">
          {t("scout.marketDeclined")}
        </Typography>
      ) : null}

      {granted && status === null && !current.pending ? (
        <Typography variant="body2" color="text.secondary">
          {t("scout.marketUnavailable")}
        </Typography>
      ) : null}

      {status !== null ? (
        <>
          <Typography variant="body2" color="text.secondary">
            {t(APPROACHABILITY[status.availability] ?? status.availability)}
            {status.employed ? t("scout.currentlyEmployed") : null}
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
                setRunning(true);
                void expressInterest(
                  subjectId,
                  t("scout.interestText"),
                ).then(
                  (result: Awaited<ReturnType<typeof expressInterest>>) => {
                    setRunning(false);
                    setMessage(
                      result.ok
                        ? t("scout.interestRecorded")
                        : t(
                            failureKey(
                              result.error,
                              "errors.interestNotRecorded",
                              COMPANY_TRANSFER_CASES,
                            ),
                          ),
                    );
                  },
                );
              }}
            >
              {running
                ? t("scout.interestInProgress")
                : t("scout.showInterest")}
            </Button>
          ) : null}
        </>
      ) : null}
    </Box>
  );
}
