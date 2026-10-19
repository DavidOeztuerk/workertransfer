import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  type Notice,
  type NoticeEnd,
  type NoticeUnit,
  getMyNotice,
  saveMyNotice,
  withdrawMyNotice,
} from "../api/market";

/** The rule as a catalogue key — written out, never built from the value. */
const END_KEY = {
  fifteenth_or_month_end: "market.noticeEnd_fifteenth_or_month_end",
  month_end: "market.noticeEnd_month_end",
  quarter_end: "market.noticeEnd_quarter_end",
  any_day: "market.noticeEnd_any_day",
} as const satisfies Record<NoticeEnd, string>;

const ENDS = Object.keys(END_KEY) as NoticeEnd[];

/**
 * The notice period (VISION-FRAGEN H2): stated by the person, seen by her only.
 *
 * <strong>An orientation, not a legal statement.</strong> Probation,
 * collective agreements and the day the notice is received shift the real
 * date; the card says so under the result (§ 2 RDG).
 */
export function NoticeCard() {
  const { t, i18n } = useTranslation();
  const [notice, setNotice] = useState<Notice | null>(null);
  const [length, setLength] = useState("");
  const [unit, setUnit] = useState<NoticeUnit>("month");
  const [end, setEnd] = useState<NoticeEnd>("month_end");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void getMyNotice(controller.signal).then((result) => {
      if (controller.signal.aborted || !result.ok) return;
      setNotice(result.notice);
      if (result.notice.length !== null) setLength(String(result.notice.length));
      if (result.notice.unit !== null) setUnit(result.notice.unit);
      if (result.notice.end !== null) setEnd(result.notice.end);
    });
    return () => controller.abort();
  }, []);

  const day = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString(i18n.language);

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("market.notice")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t("market.noticeLead")}
        </Typography>

        <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr 2fr" }, mb: 2 }}>
          <TextField
            type="number"
            label={t("market.noticeLength")}
            value={length}
            onChange={(event) => setLength(event.target.value)}
            slotProps={{ htmlInput: { min: 1, step: 1 } }}
          />
          <TextField
            select
            label={t("market.noticeUnit")}
            value={unit}
            onChange={(event) => setUnit(event.target.value as NoticeUnit)}
          >
            <MenuItem value="week">{t("market.noticeWeeks")}</MenuItem>
            <MenuItem value="month">{t("market.noticeMonths")}</MenuItem>
          </TextField>
          <TextField
            select
            label={t("market.noticeEnd")}
            value={end}
            onChange={(event) => setEnd(event.target.value as NoticeEnd)}
          >
            {ENDS.map((value) => (
              <MenuItem key={value} value={value}>
                {t(END_KEY[value])}
              </MenuItem>
            ))}
          </TextField>
        </Box>

        {error !== null ? (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, mb: 2 }}>
          <Button
            variant="contained"
            disabled={busy || length.trim() === ""}
            onClick={() => {
              setBusy(true);
              void saveMyNotice(Number(length), unit, end).then((result) => {
                setBusy(false);
                if (result.ok) {
                  setNotice(result.notice);
                  setError(null);
                } else {
                  setError(t(result.reason === "invalid" ? "market.noticeInvalid" : "errors.requestFailed"));
                }
              });
            }}
          >
            {t("market.noticeSave")}
          </Button>
          {notice?.length != null ? (
            <Button
              variant="text"
              disabled={busy}
              onClick={() => {
                setBusy(true);
                void withdrawMyNotice().then((ok) => {
                  setBusy(false);
                  if (ok) {
                    setNotice(null);
                    setLength("");
                  }
                });
              }}
            >
              {t("market.noticeWithdraw")}
            </Button>
          ) : null}
        </Box>

        {notice?.earliest_start != null && notice.last_day != null ? (
          <Alert severity="info">
            {t("market.noticeResult", { last: day(notice.last_day), earliest: day(notice.earliest_start) })}
            <Typography variant="body2" sx={{ mt: 1 }}>
              {t("market.noticeOrientation")}{" "}
              <Link component={RouterLink} to="/checklists">
                {t("footer.checklists")}
              </Link>
            </Typography>
          </Alert>
        ) : null}
      </CardContent>
    </Card>
  );
}
