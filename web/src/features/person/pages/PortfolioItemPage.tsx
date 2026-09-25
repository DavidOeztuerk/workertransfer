import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate, useParams } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { SignInRequired } from "../components/SignInRequired";
import { useAsync } from "../lib/useAsync";
import {
  type PortfolioItem,
  getMyPortfolio,
  saveMyPortfolio,
  uploadAttachment,
} from "../api/portfolio";

/** The form's state: every field as typed, the year still a string. */
interface ItemDraft {
  title: string;
  summary: string;
  url: string;
  role: string;
  year: string;
  attachment: string | null;
  technologies: string[];
}

const EMPTY: ItemDraft = {
  title: "",
  summary: "",
  url: "",
  role: "",
  year: "",
  attachment: null,
  technologies: [],
};

const toDraft = (item: PortfolioItem): ItemDraft => ({
  title: item.title,
  summary: item.summary,
  url: item.url ?? "",
  role: item.role,
  year: item.year === null ? "" : String(item.year),
  attachment: item.attachment,
  technologies: item.technologies,
});

/** Empty fields become `null`, not `""` — "not stated" is no empty value. */
function toItem(draft: ItemDraft): PortfolioItem {
  const year = draft.year.trim();

  return {
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    url: draft.url.trim() === "" ? null : draft.url.trim(),
    role: draft.role.trim(),
    year: year === "" ? null : Number(year),
    attachment: draft.attachment,
    technologies: draft.technologies,
  };
}

/**
 * <c>/portfolio/new</c> and <c>/portfolio/:index</c> — create or change an
 * item.
 *
 * <strong>An item has no id of its own.</strong> Its address is its position
 * in the stored list, and saving therefore means: send the whole field, with
 * exactly one changed entry. Whoever overlooks that deletes the others on save.
 *
 * <strong>Removing stands HERE and not in the list.</strong> Here the person
 * has the item in front of them that it is about — in a list the click would
 * hit one row among many.
 *
 * <strong>The file field states its rules up front</strong> — which formats and
 * how large. A field whose rules you learn by failing is a riddle, not a field.
 */
export function PortfolioItemPage() {
  const { t } = useTranslation();
  const { index } = useParams();
  const navigate = useNavigate();
  const status = useAppSelector((state) => state.auth.status);
  const session = useAppSelector((state) => state.auth.session);

  const isNew = index === undefined;
  const showcase = useAsync(
    (signal) => getMyPortfolio(signal),
    [session?.userId],
    session !== null,
  );

  const [draft, setDraft] = useState<ItemDraft>(EMPTY);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const items = showcase.value?.ok
    ? (showcase.value.value?.items ?? [])
    : [];
  const position = isNew ? items.length : Number(index);
  const exists =
    !isNew &&
    Number.isInteger(position) &&
    position >= 0 &&
    position < items.length;

  useEffect(() => {
    if (loaded || !showcase.value?.ok) return;
    if (exists) setDraft(toDraft(items[position] as PortfolioItem));
    setLoaded(true);
  }, [loaded, showcase.value, exists, items, position]);

  if (status === "anonymous") {
    return (
      <SignInRequired
        title={t("portfolio.itemTitle")}
        sentenceKey="portfolio.signInRequired"
      />
    );
  }

  if (showcase.loading) {
    return (
      <PageShell title={t("portfolio.itemTitle")} narrow>
        <LoadingBlock label={t("portfolio.itemLoading")} />
      </PageShell>
    );
  }

  // An address with nothing behind it — say after removing in a second tab,
  // or when somebody changes the number by hand.
  if (!isNew && !exists) {
    return (
      <PageShell title={t("portfolio.itemMissingTitle")} narrow>
        <EmptyBlock
          title={t("portfolio.itemMissingTitle")}
          hint={t("portfolio.itemMissingHint")}
          action={
            <Button component={RouterLink} to="/portfolio" variant="contained">
              {t("portfolio.back")}
            </Button>
          }
        />
      </PageShell>
    );
  }

  function change(part: Partial<ItemDraft>) {
    setDraft((before) => ({ ...before, ...part }));
    setError(null);
  }

  async function save() {
    const nextValue = [...items];
    if (isNew) nextValue.push(toItem(draft));
    else nextValue[position] = toItem(draft);

    setRunning(true);
    const result = await saveMyPortfolio(nextValue);
    setRunning(false);

    if (result.ok) void navigate("/portfolio");
    else setError(result.error.detail);
  }

  async function remove() {
    setRunning(true);
    const result = await saveMyPortfolio(
      items.filter((_, i) => i !== position),
    );
    setRunning(false);

    if (result.ok) void navigate("/portfolio");
    else setError(result.error.detail);
  }

  async function chooseFile(file: File) {
    setRunning(true);
    const result = await uploadAttachment(file);
    setRunning(false);

    if (result.ok) change({ attachment: result.value.name });
    else setError(result.error.detail);
  }

  // The local file name is NOT shown: it never travels to the server, and the
  // UI may only say what is true — that a file is attached.
  const fileSentence =
    draft.attachment !== null ? "portfolio.fileAttached" : "portfolio.noFile";

  return (
    <PageShell
      title={
        isNew
          ? t("portfolio.itemNew")
          : draft.title !== ""
            ? draft.title
            : t("portfolio.itemTitle")
      }
      narrow
    >
      <Box sx={{ mb: 2 }}>
        <Link component={RouterLink} to="/portfolio" variant="body2">
          {t("portfolio.back")}
        </Link>
      </Box>

      <Card>
        <CardContent>
          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label={t("portfolio.fieldTitle")}
              value={draft.title}
              onChange={(e) => change({ title: e.target.value })}
              required
            />
            <TextField
              label={t("portfolio.fieldAbout")}
              value={draft.summary}
              onChange={(e) => change({ summary: e.target.value })}
              helperText={t("portfolio.fieldAboutHint")}
              multiline
              minRows={3}
            />
            <TextField
              label={t("portfolio.fieldLink")}
              value={draft.url}
              onChange={(e) => change({ url: e.target.value })}
              helperText={t("portfolio.fieldLinkHint")}
            />
            <TextField
              label={t("portfolio.fieldRole")}
              value={draft.role}
              onChange={(e) => change({ role: e.target.value })}
            />
            <TextField
              label={t("portfolio.fieldYear")}
              value={draft.year}
              onChange={(e) => change({ year: e.target.value })}
            />

            <Box>
              <Typography variant="body2" sx={{ mb: 1 }}>
                {t("portfolio.file")}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                {t(fileSentence)}
              </Typography>
              <Button
                component="label"
                variant="outlined"
                size="small"
                disabled={running}
              >
                {t("portfolio.chooseFile")}
                <input
                  type="file"
                  hidden
                  accept="image/png,image/jpeg,application/pdf"
                  onChange={(event) => {
                    const file = event.target.files?.[0];
                    // Reset the field so that the same file can be chosen
                    // again.
                    event.target.value = "";
                    if (file !== undefined) void chooseFile(file);
                  }}
                />
              </Button>

              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ display: "block", mt: 1 }}
              >
                {t("portfolio.fileHint")}
              </Typography>
            </Box>

            <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
              <Button type="submit" variant="contained" disabled={running}>
                {running
                  ? t("common.saving")
                  : t("common.save")}
              </Button>
              {!isNew ? (
                <Button
                  variant="text"
                  color="error"
                  onClick={() => void remove()}
                  disabled={running}
                >
                  {running
                    ? t("portfolio.removing")
                    : t("portfolio.remove")}
                </Button>
              ) : null}
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
