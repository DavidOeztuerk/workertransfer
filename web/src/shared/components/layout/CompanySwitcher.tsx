import { useEffect } from "react";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { useAppDispatch, useAppSelector } from "../../../core/store/hooks";
import { actForCompany, loadMemberships } from "../../../features/auth/store/authThunks";

/**
 * "Acting as" — the person or one of the companies.
 *
 * <strong>The client NAMES the company, the server DECIDES.</strong> The choice
 * sends `POST /auth/company/{id}`; only there is the membership checked and the
 * tenant written into the token (ADR-0018). What stands here is therefore never
 * a claim about rights, but a request.
 *
 * Back to "myself" goes only through signing out, and that is honest: a person
 * token cannot be computed out of a company token, it has to be issued anew.
 */
export function CompanySwitcher() {
  const { t } = useTranslation();
  const dispatch = useAppDispatch();
  const memberships = useAppSelector((state) => state.auth.memberships);
  const tenantId = useAppSelector((state) => state.auth.session?.tenantId ?? null);

  useEffect(() => {
    void dispatch(loadMemberships());
  }, [dispatch]);

  if (memberships.length === 0) return null;

  return (
    <TextField
      select
      // MUI's select, not a native one — why stands at `MuiSelect` in
      // `styles/theme.ts`: it draws `role="combobox"`, which screen readers
      // announce as a select. Playwright's `selectOption` does not reach it;
      // the E2E journeys use the helper in `e2e/stack.ts` instead.
      size="small"
      label={t("header.actingAs")}
      value={tenantId ?? ""}
      onChange={(event) => {
        const chosen = event.target.value;
        if (chosen !== "") void dispatch(actForCompany(chosen));
      }}
      sx={{ minWidth: 200, display: { xs: "none", md: "block" } }}
    >
      <MenuItem value="" disabled>
        {t("header.myself")}
      </MenuItem>
      {memberships.map((company) => (
        <MenuItem key={company.id} value={company.id}>
          {company.name}
        </MenuItem>
      ))}
    </TextField>
  );
}
