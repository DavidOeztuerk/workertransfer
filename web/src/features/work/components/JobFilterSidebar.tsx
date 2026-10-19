import ClearIcon from "@mui/icons-material/ClearOutlined";
import MyLocationIcon from "@mui/icons-material/MyLocation";
import TuneIcon from "@mui/icons-material/TuneOutlined";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Divider from "@mui/material/Divider";
import FormHelperText from "@mui/material/FormHelperText";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";

import type { GeolocationState } from "../../../shared/hooks/useGeolocation";
import { radius } from "../../../styles/tokens/spacing";

import {
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  employmentLabel,
  remoteLabel,
} from "../api/jobs";

/**
 * The selectable distances, in kilometres.
 *
 * None below ten: the adverts' coordinates are city centres, and a radius below
 * the extent of a city would claim a precision the data does not have. The same
 * number stands as the smallest radius in the service and is enforced there —
 * here it is the choice, there the rule.
 */
export const DISTANCES_KM = [10, 25, 50, 100, 200] as const;

/** What can be filtered. Empty means "any" everywhere. */
export interface JobFilter {
  q: string;
  location: string;
  remote: RemoteMode | "";
  employment: EmploymentType | "";
  skills: string;
  /**
   * The radius in kilometres, as a string like every other select field.
   *
   * The location deliberately does NOT stand here: it is no input into this
   * form but a permission the browser obtains — and it is stored nowhere (see
   * `useGeolocation`).
   */
  radius: string;
}

export const EMPTY_FILTER: JobFilter = {
  q: "",
  location: "",
  remote: "",
  employment: "",
  skills: "",
  radius: "",
};

/** How many filters are doing something right now. For the display at the top. */
export function countActiveFilters(filter: JobFilter): number {
  return Object.values(filter).filter((value) => value.trim() !== "").length;
}

/**
 * The filters as a sidebar.
 *
 * <strong>One below the other and not in a row</strong>, and that is the actual
 * gain: the filters used to stand as five fields side by side above the list,
 * wrapped on every narrow screen and pushed the results below the fold. A
 * column has room for label AND hint, and the list starts at the top.
 *
 * <strong>Filtering happens only on submit.</strong> No filtering while typing:
 * every key would be a request, the list would jump under one's hand, and
 * whoever types "Kubernetes" would have seen eleven result lists on the way
 * that do not interest them.
 */
export function JobFilterSidebar({
  filter,
  onChange,
  onSubmit,
  onReset,
  geolocation,
}: {
  filter: JobFilter;
  onChange: (filter: JobFilter) => void;
  onSubmit: () => void;
  onReset: () => void;
  geolocation: GeolocationState;
}) {
  const { t } = useTranslation();
  const activeCount = countActiveFilters(filter);
  const hasPosition = geolocation.position !== null;

  /*
   * ONE CENTRE IS ENOUGH, and the typed place is one too.
   *
   * The distance used to hang on the location button alone. Whoever types
   * "Leipzig" and wants "50 km" means "around Leipzig", though — and needs
   * neither GPS nor the permission for it. The service resolves the place name
   * with the same table it uses to place the adverts (ADR-0032).
   */
  const hasCentre = hasPosition || filter.location.trim() !== "";

  /*
    WITH "FULLY REMOTE" THERE IS NO DISTANCE.

    Not taste, but demonstrably without effect: the service lets fully remote
    adverts through regardless of the radius (ADR-0032). The combination "remote
    only" + "25 km" therefore delivers the same as "remote only" alone — a
    control that stands there visibly and provably does nothing is worse than
    none. The same rule as in SkillSwap, there
    `filters.locationType !== 'remote'`.
  */
  const remoteOnly = filter.remote === "full";

  // A failure, otherwise the confirmation, otherwise — if the browser cannot
  // locate at all — the reason. Nothing at rest.
  //
  // Only when there is something to say: what the button does and that the
  // position is rounded lives in its tooltip, on demand instead of permanently.
  const hint =
    geolocation.error ??
    (hasPosition
      ? t("jobs.locationRounded")
      : geolocation.supported
        ? null
        : t("geolocation.unsupported"));

  return (
    <Paper
      component="form"
      variant="outlined"
      onSubmit={(event: React.FormEvent) => {
        event.preventDefault();
        onSubmit();
      }}
      sx={{
        p: 2.5,
        // THE SAME radius as a job card. `3` used to stand here, which gave
        // thirty pixels via `theme.shape` — the cards beside it have fourteen,
        // and two roundings side by side look like an accident. The value comes
        // from the same token as the card.
        borderRadius: `${radius.lg}px`,
        // Sticks while scrolling — the filters stay reachable without anybody
        // having to scroll back up.
        position: { md: "sticky" },
        top: { md: 24 },
      }}
    >
      <Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 2 }}>
        <TuneIcon fontSize="small" color="action" />
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {t("jobs.filter")}
        </Typography>
        {activeCount > 0 ? (
          <Button
            size="small"
            variant="text"
            startIcon={<ClearIcon fontSize="small" />}
            onClick={onReset}
          >
            {t("jobs.filterReset")}
          </Button>
        ) : null}
      </Box>

      {activeCount > 0 ? (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mb: 2 }}
        >
          {t("jobs.activeFilters", { count: activeCount })}
        </Typography>
      ) : null}

      <Divider sx={{ mb: 2.5 }} />

      <Box sx={{ display: "grid", gap: 2.5 }}>
        <TextField
          label={t("jobs.searchTerm")}
          placeholder={t("jobs.searchTermExample")}
          value={filter.q}
          onChange={(event) => onChange({ ...filter, q: event.target.value })}
        />

        <TextField
          label={t("jobs.skills")}
          // No helper text below the field: the placeholder says the same thing
          // shorter, and three lines under one of six fields turn a filter bar
          // into a manual.
          placeholder={t("jobs.skillsExample")}
          value={filter.skills}
          onChange={(event) => onChange({ ...filter, skills: event.target.value })}
        />

        <TextField
          select
          label={t("jobs.workMode")}
          value={filter.remote}
          onChange={(event) => {
            const chosen = event.target.value as RemoteMode | "";

            // The radius goes WITH it when "fully remote" is chosen. If it
            // stayed, it would count below as an active filter and be none —
            // and it would come back unnoticed when switching back.
            onChange({
              ...filter,
              remote: chosen,
              radius: chosen === "full" ? "" : filter.radius,
            });
          }}
        >
          <MenuItem value="">{t("jobs.any")}</MenuItem>
          {REMOTE_MODES.map((value) => (
            <MenuItem key={value} value={value}>
              {remoteLabel(value)}
            </MenuItem>
          ))}
        </TextField>

        <TextField
          select
          label={t("jobs.employmentType")}
          value={filter.employment}
          onChange={(event) =>
            onChange({
              ...filter,
              employment: event.target.value as EmploymentType | "",
            })
          }
        >
          <MenuItem value="">{t("jobs.any")}</MenuItem>
          {EMPLOYMENT_TYPES.map((value) => (
            <MenuItem key={value} value={value}>
              {employmentLabel(value)}
            </MenuItem>
          ))}
        </TextField>

        <Box
          // PLACE AND DISTANCE ARE ONE GROUP, and it stands last: both answer
          // the same question ("where?"), as a name and as a radius. The tighter
          // gap inside the group shows that without a heading having to say it.
          sx={{ display: "grid", gap: 1.25 }}
        >
          <TextField
            label={t("jobs.location")}
            value={filter.location}
            onChange={(event) => onChange({ ...filter, location: event.target.value })}
          />

          {remoteOnly ? null : (
            <Box>
              <Box sx={{ display: "flex", alignItems: "flex-end", gap: 1 }}>
                <TextField
                  select
                  sx={{ flexGrow: 1 }}
                  label={t("jobs.distance")}
                  value={filter.radius}
                  // WITHOUT a centre this field is dead, and that is more honest
                  // than a choice one can make and that then does nothing:
                  // "25 km" without a point is no question anybody could answer.
                  disabled={!hasCentre}
                  onChange={(event) => onChange({ ...filter, radius: event.target.value })}
                >
                  <MenuItem value="">{t("jobs.any")}</MenuItem>
                  {DISTANCES_KM.map((km) => (
                    <MenuItem key={km} value={String(km)}>
                      {t("jobs.kilometers", { km })}
                    </MenuItem>
                  ))}
                </TextField>

                <Tooltip
                  title={hasPosition ? t("jobs.forgetLocation") : t("jobs.useLocationHint")}
                >
                  <Box
                    // The wrapper is needed: a disabled button sends no pointer
                    // events, so the tooltip would never appear — exactly when it
                    // explains the most.
                    component="span"
                  >
                    <IconButton
                      aria-label={
                        hasPosition ? t("jobs.forgetLocation") : t("jobs.useLocation")
                      }
                      color={hasPosition ? "primary" : "default"}
                      disabled={!geolocation.supported || geolocation.loading}
                      onClick={() => {
                        if (!hasPosition) {
                          geolocation.request();
                          return;
                        }

                        // The location goes, and the distance goes with it. A
                        // "25 km" left standing without a point would look like
                        // an active filter and be none.
                        geolocation.forget();
                        onChange({ ...filter, radius: "" });
                      }}
                      sx={{ mb: 0.25 }}
                    >
                      {geolocation.loading ? <CircularProgress size={20} /> : <MyLocationIcon />}
                    </IconButton>
                  </Box>
                </Tooltip>
              </Box>

              {hint === null ? null : (
                <FormHelperText error={geolocation.error !== null} sx={{ mx: 0 }}>
                  {hint}
                </FormHelperText>
              )}
            </Box>
          )}
        </Box>

        <Button type="submit" variant="contained" fullWidth>
          {t("jobs.search")}
        </Button>
      </Box>
    </Paper>
  );
}
