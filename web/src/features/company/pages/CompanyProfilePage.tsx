import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { ADMIN_CASES, type ServerCase, failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import { getOwnCompanyProfile, saveCompanyProfile } from "../api/companies";

interface ProfileDraft {
  display_name: string;
  about: string;
  website: string;
  locations: string;
  benefits: string;
  line1: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
}

const EMPTY: ProfileDraft = {
  display_name: "",
  about: "",
  website: "",
  locations: "",
  benefits: "",
  line1: "",
  postal_code: "",
  city: "",
  country: "DE",
  phone: "",
};

/** Comma-separated, empties drop out — "nothing stated" is no empty line. */
const list = (raw: string): string[] =>
  raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");


/**
 * Saving the shop window demands an `admin` (PBI-2), and the profile names
 * what was wrong with it. A catalogue text, never companies-service's sentence
 * — each pattern matches `EmployerProfile.cs`, which points back here.
 */
const SAVE_CASES: readonly ServerCase[] = [
  ...ADMIN_CASES,
  { status: 422, pattern: /display name must not be empty/i, key: "companyProfile.displayNameRequired" },
  { status: 422, pattern: /link|http and https/i, key: "companyProfile.websiteInvalid" },
  { status: 422, pattern: /at most \d+ (locations|benefits)/i, key: "companyProfile.tooManyEntries" },
  { status: 422, pattern: /exceeds \d+ characters/i, key: "companyProfile.fieldTooLong" },
];

/**
 * <c>/company/profile</c> — how a company presents itself.
 *
 * <strong>As long as nothing stands here, an advert stays anonymous</strong> —
 * title and description, nothing else. That is no shortcoming but the default:
 * a company decides itself whether it shows itself.
 *
 * <strong>The stored state fills the form exactly once.</strong> If that ran on
 * every answer, a reload would overwrite the text just typed.
 */
export function CompanyProfilePage() {
  const { t } = useTranslation();
  const { forCompany, tenantId } = useActor();

  const [draft, setDraft] = useState<ProfileDraft>(EMPTY);
  const [adopted, setAdopted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [running, setRunning] = useState(false);

  const profile = useAsync(
    (signal) => getOwnCompanyProfile(signal),
    [tenantId],
    forCompany,
  );

  useEffect(() => {
    if (adopted || !profile.data?.ok) return;
    const current = profile.data.profile;
    if (current !== null) {
      setDraft({
        display_name: current.display_name,
        about: current.about,
        website: current.website ?? "",
        locations: current.locations.join(", "),
        benefits: current.benefits.join(", "),
        line1: current.line1 ?? "",
        postal_code: current.postal_code ?? "",
        city: current.city ?? "",
        country: current.country || "DE",
        phone: current.phone ?? "",
      });
    }
    setAdopted(true);
  }, [adopted, profile.data]);

  if (!forCompany) {
    return (
      <PageShell title={t("companyProfile.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("companyProfile.companyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function save() {
    setRunning(true);
    const result = await saveCompanyProfile({
      display_name: draft.display_name,
      about: draft.about,
      website: draft.website.trim() === "" ? null : draft.website.trim(),
      locations: list(draft.locations),
      benefits: list(draft.benefits),
      line1: draft.line1,
      postal_code: draft.postal_code,
      city: draft.city,
      country: draft.country,
      phone: draft.phone,
    });
    setRunning(false);

    if (result.ok) {
      setError(null);
      setSaved(true);
    } else {
      setError(t(failureKey(result.error, "errors.profileNotSaved", SAVE_CASES)));
      setSaved(false);
    }
  }

  return (
    <PageShell
      title={t("companyProfile.title")}
      narrow
      lead={t("companyProfile.lead")}
    >
      <Card>
        <CardContent>
          {profile.pending ? (
            <LoadingBlock label={t("companyProfile.loading")} />
          ) : null}

          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          {saved ? (
            <Alert severity="success" role="status" sx={{ mb: 2 }}>
              {t("companyProfile.saved")}
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
              label={t("companyProfile.displayName")}
              helperText={t("companyProfile.displayNameHint")}
              value={draft.display_name}
              onChange={(e) => {
                setDraft({ ...draft, display_name: e.target.value });
                setSaved(false);
              }}
              required
            />
            <TextField
              label={t("companyProfile.aboutUs")}
              helperText={t("companyProfile.aboutUsHint")}
              value={draft.about}
              onChange={(e) => {
                setDraft({ ...draft, about: e.target.value });
                setSaved(false);
              }}
              multiline
              minRows={4}
            />
            <TextField
              label={t("companyProfile.website")}
              helperText={t("companyProfile.websiteHint")}
              value={draft.website}
              onChange={(e) => {
                setDraft({ ...draft, website: e.target.value });
                setSaved(false);
              }}
            />
            <TextField
              label={t("companyProfile.locations")}
              helperText={t("companyProfile.locationsHint")}
              value={draft.locations}
              onChange={(e) => {
                setDraft({ ...draft, locations: e.target.value });
                setSaved(false);
              }}
            />
            <TextField
              label={t("companyProfile.street")}
              helperText={t("companyProfile.streetHint")}
              value={draft.line1}
              onChange={(e) => {
                setDraft({ ...draft, line1: e.target.value });
                setSaved(false);
              }}
            />
            <Box sx={{ display: "grid", gap: 2, gridTemplateColumns: { sm: "1fr 2fr 1fr" } }}>
              <TextField
                label={t("companyProfile.postalCode")}
                value={draft.postal_code}
                onChange={(e) => {
                  setDraft({ ...draft, postal_code: e.target.value });
                  setSaved(false);
                }}
              />
              <TextField
                label={t("companyProfile.city")}
                value={draft.city}
                onChange={(e) => {
                  setDraft({ ...draft, city: e.target.value });
                  setSaved(false);
                }}
              />
              <TextField
                label={t("companyProfile.country")}
                value={draft.country}
                onChange={(e) => {
                  setDraft({ ...draft, country: e.target.value });
                  setSaved(false);
                }}
                slotProps={{ htmlInput: { maxLength: 2 } }}
              />
            </Box>
            <TextField
              label={t("companyProfile.phone")}
              value={draft.phone}
              onChange={(e) => {
                setDraft({ ...draft, phone: e.target.value });
                setSaved(false);
              }}
            />
            <TextField
              label={t("companyProfile.benefits")}
              helperText={t("companyProfile.benefitsHint")}
              value={draft.benefits}
              onChange={(e) => {
                setDraft({ ...draft, benefits: e.target.value });
                setSaved(false);
              }}
            />

            <Box>
              <Button type="submit" variant="contained" disabled={running}>
                {running
                  ? t("common.saving")
                  : t("common.save")}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}
