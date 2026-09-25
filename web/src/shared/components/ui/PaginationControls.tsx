import { useId } from "react";
import Box from "@mui/material/Box";
import MenuItem from "@mui/material/MenuItem";
import Pagination from "@mui/material/Pagination";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import useMediaQuery from "@mui/material/useMediaQuery";
import { useTheme } from "@mui/material/styles";
import { useTranslation } from "react-i18next";

/** The sizes on offer. Twelve is the default. */
export const PAGE_SIZES = [12, 24, 48, 96] as const;

/**
 * The pagination bar: where you are, how many there are, and how many per page.
 *
 * <strong>The range stands on the left and not as "page 3 of 9".</strong>
 * "25–36 of 104" answers two questions at once — where am I, and how much is
 * still to come. The page number alone answers neither as long as one does not
 * have the page size in mind.
 *
 * <strong>It does not vanish at one page.</strong> The choice of the page size
 * stays visible, because exactly then somebody may want to make it smaller;
 * only the number bar itself goes, because it offers nothing.
 *
 * <strong>Two groups, not three things side by side.</strong> On the left
 * stands WHERE you are — the range and the numbers belong together and are read
 * that way. On the right stands HOW MUCH goes on a page. Before, a
 * <c>space-between</c> drove the range fully left, the numbers into the middle
 * and the size to the right: three islands whose connection nobody sees.
 */
export function PaginationControls({
  page,
  pageSize,
  totalItems,
  totalPages,
  onPage,
  onPageSize,
}: {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  onPage: (page: number) => void;
  onPageSize: (pageSize: number) => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down("sm"));
  const sizeFieldId = useId();

  // NOTHING TO PAGE, SO NO BAR.
  //
  // "Nothing found" used to stand here — and beside it the empty block said
  // "Nothing was found for that." The same information twice, one below the
  // other, and the E2E journey tripped over it: two hits on the same text. The
  // empty block says it better (it also offers a way out), so the bar stays
  // silent. There is nothing to sort here anyway.
  if (totalItems === 0) {
    return null;
  }

  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalItems);

  return (
    <Box
      sx={{
        display: "flex",
        flexDirection: { xs: "column", sm: "row" },
        // `flex-start` and NOT `stretch`: stretched, every group fills the
        // whole width, and its `space-between` then tore "per page" and the
        // field to opposite edges — two things that belong together, as far
        // apart as possible.
        alignItems: { xs: "flex-start", sm: "center" },
        justifyContent: "space-between",
        gap: { xs: 1.5, sm: 2 },
        // Tighter than before (mt 4 / pt 3). The bar is a footer of the list,
        // not a section of its own — it should hang on to it.
        mt: 3,
        pt: 2,
        borderTop: 1,
        borderColor: "divider",
      }}
    >
      <Box
        // LEFT: where you are. Range and numbers are ONE statement, so they
        // share a box.
        sx={{
          display: "flex",
          alignItems: "center",
          gap: { xs: 1.5, sm: 2 },
          flexWrap: "wrap",
        }}
      >
        <Typography
          variant="body2"
          color="text.secondary"
          role="status"
          sx={{ whiteSpace: "nowrap" }}
        >
          {/* `von`, `bis`, `gesamt` are the catalogue's placeholder names. */}
          {t("pagination.range", { von: first, bis: last, gesamt: totalItems })}
        </Typography>

        {totalPages > 1 ? (
          <Pagination
            count={totalPages}
            page={page}
            onChange={(_event, target) => onPage(target)}
            // On narrow screens only the neighbours: otherwise the bar wraps
            // and pushes the content away.
            siblingCount={narrow ? 0 : 1}
            boundaryCount={narrow ? 1 : 2}
            size={narrow ? "small" : "medium"}
            getItemAriaLabel={(type, pageNumber) =>
              type === "previous"
                ? t("pagination.previous")
                : type === "next"
                  ? t("pagination.next")
                  : t("pagination.page", { nummer: pageNumber })
            }
          />
        ) : null}
      </Box>

      <Box
        sx={{
          display: "flex",
          alignItems: "center",
          gap: 1,
          flexShrink: 0,
        }}
      >
        <Typography
          // RIGHT: how much fits on a page — the label stands BESIDE the field
          // as its own `label` and not as the field's `label` property: this UI
          // puts field labels ABOVE the field (see `MuiInputLabel` in the
          // theme), which here would be a two-line island next to a one-line
          // bar. `htmlFor` keeps it a real label.
          component="label"
          htmlFor={sizeFieldId}
          variant="body2"
          color="text.secondary"
          sx={{ whiteSpace: "nowrap" }}
        >
          {t("pagination.perPage")}
        </Typography>

        <TextField
          select
          size="small"
          id={sizeFieldId}
          value={String(pageSize)}
          onChange={(event) => onPageSize(Number(event.target.value))}
          sx={{ width: 88, flexShrink: 0 }}
        >
          {PAGE_SIZES.map((size) => (
            <MenuItem key={size} value={size}>
              {size}
            </MenuItem>
          ))}
        </TextField>
      </Box>
    </Box>
  );
}
