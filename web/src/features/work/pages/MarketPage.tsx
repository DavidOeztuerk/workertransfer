import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Link from "@mui/material/Link";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Checkbox from "@mui/material/Checkbox";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { type ServerCase, failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { NoticeCard } from "../components/NoticeCard";
import { EmployerRouteCard } from "../components/EmployerRoute";
import { useAsync } from "../lib/useAsync";
import {
  type Availability,
  type MarketRequest,
  answerMarketRequest,
  getMyMarketStatus,
  listMyMarketRequests,
  revokeMarketAccess,
  saveMyMarketStatus,
} from "../api/market";

/** Only the values — label and hint stand in the catalogues. */
const CHOICES: { value: Availability; label: string; hint: string }[] = [
  { value: "open", label: "market.choiceOpen", hint: "market.choiceOpenHint" },
  { value: "listening", label: "market.choiceListening", hint: "market.choiceListeningHint" },
  { value: "unavailable", label: "market.choiceUnavailable", hint: "market.choiceUnavailableHint" },
];

/**
 * <c>/market</c> — being approachable, and for whom.
 *
 * <strong>There is deliberately no "for everybody" here.</strong> That somebody
 * wants to switch is the most delicate statement on this platform: it can cost
 * the job the person still has. That is why a company asks on its own, and the
 * release holds for that one.
 *
 * <strong>The default is "not right now".</strong> Whoever opens the page and
 * saves without choosing anything must not become approachable by accident.
 */

/**
 * What a failed step on this page reads as — a catalogue text, never the
 * server's sentence (transfer-service answers in English).
 *
 * A note that is too long is the one 422 the form can produce and the person
 * can fix; its sentence is written in `MarketStatus.cs` (`RemarkTooLong`).
 */
const SAVE_CASES: readonly ServerCase[] = [
  { status: 422, pattern: /note exceeds/i, key: "market.remarkTooLong" },
  { status: 422, pattern: /window ends before/i, key: "market.windowUpsideDown" },
];

/** A 409 on a request means it was answered meanwhile — elsewhere, or in another tab. */
const REQUEST_CASES: readonly ServerCase[] = [
  { status: 409, key: "market.requestAlreadyAnswered" },
];

export function MarketPage() {
  const { t } = useTranslation();
  const { signedIn, subjectId } = useActor();

  const current = useAsync(
    (signal) => getMyMarketStatus(signal),
    [subjectId],
    signedIn,
  );
  const requests = useAsync(
    (signal) => listMyMarketRequests(signal),
    [subjectId],
    signedIn,
  );

  const [availability, setAvailability] = useState<Availability>("unavailable");
  const [employed, setEmployed] = useState(false);
  // What the SERVER holds, not the box: the notice period's dates count only
  // while the saved status says employed, so the cards follow the save.
  const [savedEmployed, setSavedEmployed] = useState(false);
  const [note, setNote] = useState("");
  const [openFrom, setOpenFrom] = useState("");
  const [openUntil, setOpenUntil] = useState("");
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [adopted, setAdopted] = useState(false);

  // The loaded state fills the form EXACTLY ONCE. If that ran on every answer, a
  // reload would overwrite the note just typed — and a save would write it back
  // without anybody noticing.
  useEffect(() => {
    if (adopted || !current.data?.ok) return;
    setAvailability(current.data.status.availability);
    setEmployed(current.data.status.employed);
    setSavedEmployed(current.data.status.employed);
    setNote(current.data.status.note);
    setOpenFrom(current.data.status.open_from ?? "");
    setOpenUntil(current.data.status.open_until ?? "");
    setAdopted(true);
  }, [adopted, current.data]);

  if (!signedIn) {
    return (
      <PageShell title={t("market.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="market.signInRequired"
                components={{
                  1: <Link component={RouterLink} to="/login" />,
                }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function save() {
    setRunning(true);
    const result = await saveMyMarketStatus({ availability, employed, note, openFrom, openUntil });
    setRunning(false);

    if (result.ok) {
      setError(null);
      setSaved(true);
      setSavedEmployed(employed);
      current.reload();
    } else {
      setError(t(failureKey(result.error, "errors.marketStatusNotSaved", SAVE_CASES)));
      setSaved(false);
    }
  }

  async function answer(id: string, grant: boolean) {
    setRunning(true);
    const result = await answerMarketRequest(id, grant);
    setRunning(false);
    if (!result.ok) setError(t(failureKey(result.error, "errors.requestNotAnswered", REQUEST_CASES)));
    requests.reload();
  }

  async function withdraw(id: string) {
    setRunning(true);
    const result = await revokeMarketAccess(id);
    setRunning(false);
    if (!result.ok) setError(t(failureKey(result.error, "errors.consentNotChanged", REQUEST_CASES)));
    requests.reload();
  }

  const list = requests.data?.ok ? requests.data.requests : [];

  return (
    <PageShell
      title={t("market.title")}
      narrow
      lead={t("market.lead")}
    >
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("market.requests")}
          </Typography>

          {requests.pending ? (
            <LoadingBlock label={t("market.requestsLoading")} />
          ) : null}

          {requests.data !== null && !requests.data.ok ? (
            <Alert severity="error">
              {t(failureKey(requests.data.error, "errors.requestsUnavailable"))}
            </Alert>
          ) : null}

          {requests.data?.ok && list.length === 0 ? (
            <EmptyBlock title={t("market.requestsEmpty")} />
          ) : null}

          {list.length > 0 ? (
            <Box
              component="ul"
              sx={{
                display: "flex",
                flexDirection: "column",
                gap: 1.5,
                listStyle: "none",
                p: 0,
                m: 0,
              }}
            >
              {list.map((request: MarketRequest) => (
                <RequestRow
                  key={request.id}
                  request={request}
                  locked={running}
                  onAnswer={(grant) => void answer(request.id, grant)}
                  onWithdraw={() => void withdraw(request.id)}
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("market.approachable")}
          </Typography>

          {!adopted && current.pending ? (
            <LoadingBlock label={t("market.statusLoading")} />
          ) : null}

          {/* The form shows the STORED status or nothing. Drawn with its
              defaults while the answer is out, a choice made in that gap was
              overwritten when the answer came, and "Speichern" wrote "not right
              now" for a person who had clicked "Ich höre zu". And with no
              answer at all, saving would write the defaults over whatever is
              stored. */}
          {!adopted && current.data !== null && !current.data.ok ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {t(failureKey(current.data.error, "errors.marketStatusUnavailable"))}
            </Alert>
          ) : null}

          {saved ? (
            <Alert severity="success" sx={{ mb: 2 }} role="status">
              {t("market.saved")}
            </Alert>
          ) : null}

          {adopted ? (
            <Box
              component="form"
              onSubmit={(event) => {
                event.preventDefault();
                void save();
              }}
            >
              <FormControl sx={{ mb: 2 }}>
                <FormLabel id="market-status">{t("market.status")}</FormLabel>
                <RadioGroup
                  aria-labelledby="market-status"
                  // A shared `name` is what makes the arrow keys move the focus
                  // inside the group.
                  name="availability"
                  value={availability}
                  onChange={(event) => {
                    setSaved(false);
                    setAvailability(event.target.value as Availability);
                  }}
                >
                  {CHOICES.map((choice) => (
                    <Box key={choice.value} sx={{ mb: 0.5 }}>
                      <FormControlLabel
                        value={choice.value}
                        control={<Radio />}
                        label={t(choice.label)}
                      />
                      <Typography
                        variant="body2"
                        color="text.secondary"
                        sx={{ ml: 4 }}
                      >
                        {t(choice.hint)}
                      </Typography>
                    </Box>
                  ))}
                </RadioGroup>
              </FormControl>

              <FormControlLabel
                control={
                  <Checkbox
                    // A box and NOT a switch — the consent-switch rule read
                    // backwards. A switch says: it holds at once. This field sits
                    // in a form with "Save", so flipping it and leaving the page
                    // changes nothing; a switch would promise an immediacy that
                    // does not exist, at the platform's most delicate field.
                    checked={employed}
                    onChange={(event) => {
                      setSaved(false);
                      setEmployed(event.target.checked);
                    }}
                  />
                }
                label={t("market.employed")}
                sx={{ mb: 2, display: "block" }}
              />

              {/* A window to be approachable in means nothing to someone who
                  is not approachable; the dates stay stored and come back
                  with the choice. */}
              {availability === "unavailable" ? null : (
              <>
              <Typography variant="h4" sx={{ mb: 0.5 }}>
                {t("market.window")}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
                {t("market.windowHint")}
              </Typography>
              <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, mb: 2 }}>
                <TextField
                  type="date"
                  label={t("market.openFrom")}
                  value={openFrom}
                  onChange={(event) => {
                    setSaved(false);
                    setOpenFrom(event.target.value);
                  }}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                <TextField
                  type="date"
                  label={t("market.openUntil")}
                  value={openUntil}
                  onChange={(event) => {
                    setSaved(false);
                    setOpenUntil(event.target.value);
                  }}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
              </Box>
              </>
              )}

              <TextField
                label={t("market.note")}
                value={note}
                onChange={(event) => {
                  setSaved(false);
                  setNote(event.target.value);
                }}
                helperText={t("market.noteHint")}
                multiline
                minRows={3}
                sx={{ mb: 2 }}
              />

              <Button type="submit" variant="contained" disabled={running}>
                {running ? t("market.saving") : t("market.save")}
              </Button>
            </Box>
          ) : null}
        </CardContent>
      </Card>
      {/* A notice period and a way through one's employer exist only with an
          employer. An open route still shows, so it can be closed. */}
      {savedEmployed ? <NoticeCard /> : null}
      <EmployerRouteCard employed={savedEmployed} />
    </PageShell>
  );
}

/**
 * A request with its state.
 *
 * As with the CV: "released" and "holds access right now" are two things. Only
 * whoever holds gets "Zurückziehen" offered.
 */
function RequestRow({
  request,
  locked,
  onAnswer,
  onWithdraw,
}: {
  request: MarketRequest;
  locked: boolean;
  onAnswer: (grant: boolean) => void;
  onWithdraw: () => void;
}) {
  const { t } = useTranslation();
  const open = request.status === "PENDING";
  const holdsAccess = request.status === "GRANTED" && request.active === true;

  const current = open
    ? t("market.statusOpen")
    : request.status === "DECLINED"
      ? t("market.statusDeclined")
      : t(holdsAccess ? "market.statusReleased" : "market.statusWithdrawn");

  return (
    <Card component="li" variant="outlined">
      <CardContent
        sx={{
          display: "flex",
          flexDirection: { xs: "column", sm: "row" },
          justifyContent: "space-between",
          alignItems: { xs: "flex-start", sm: "center" },
          gap: 2,
        }}
      >
        <Box>
          <Typography variant="h4">
            {t("market.requestTitle")}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {current}
          </Typography>
        </Box>

        <Box sx={{ display: "flex", gap: 1, flexShrink: 0 }}>
          {open ? (
            <>
              <Button
                variant="contained"
                size="small"
                onClick={() => onAnswer(true)}
                disabled={locked}
              >
                {t("common.release")}
              </Button>
              <Button
                variant="text"
                size="small"
                onClick={() => onAnswer(false)}
                disabled={locked}
              >
                {t("common.decline")}
              </Button>
            </>
          ) : null}
          {holdsAccess ? (
            <Button
              variant="text"
              size="small"
              onClick={onWithdraw}
              disabled={locked}
            >
              {t("common.withdraw")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
