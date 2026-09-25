import CheckIcon from "@mui/icons-material/Check";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import Box from "@mui/material/Box";
import Divider from "@mui/material/Divider";
import ListItemIcon from "@mui/material/ListItemIcon";
import MenuItem from "@mui/material/MenuItem";
import Typography from "@mui/material/Typography";

import type { Preference } from "./appearance";
import type { ColorPreference, LanguagePreference } from "../../../core/store/preferencesSlice";

/** Which level of the account menu is open. */
export type MenuLevel = "main" | "appearance" | "language";

/** A preference as one menu row: "Appearance ▸ Dark". */
export function PreferenceRow<T extends string>({
  preference,
  onOpen,
}: {
  preference: Preference<T>;
  onOpen: () => void;
}) {
  return (
    <MenuItem onClick={onOpen}>
      <ListItemIcon>{preference.symbol}</ListItemIcon>
      <Box sx={{ flexGrow: 1 }}>{preference.heading}</Box>
      <Typography variant="body2" color="text.secondary" sx={{ ml: 2 }}>
        {preference.name}
      </Typography>
      <ChevronRightIcon fontSize="small" sx={{ ml: 0.5, color: "text.disabled" }} />
    </MenuItem>
  );
}

/**
 * The opened preference — the same surface, one step deeper. A second menu
 * beside it would have to be positioned and would run over the edge in narrow
 * windows.
 */
export function PreferenceChoice<T extends string>({
  preference,
  onBack,
  onDone,
}: {
  preference: Preference<T>;
  onBack: () => void;
  /** After a choice the menu closes — the effect is visible at once. */
  onDone: () => void;
}) {
  return (
    <>
      <MenuItem onClick={onBack}>
        <ListItemIcon>
          <ChevronLeftIcon fontSize="small" />
        </ListItemIcon>
        <Typography variant="subtitle2" sx={{ fontWeight: 640 }}>
          {preference.heading}
        </Typography>
      </MenuItem>
      <Divider />

      {preference.options.map((option) => (
        <MenuItem
          key={option.value}
          // `menuitemradio`, because exactly one can apply — otherwise a
          // screen reader hears equal commands instead of a choice.
          role="menuitemradio"
          aria-checked={option.value === preference.value}
          selected={option.value === preference.value}
          onClick={() => {
            preference.choose(option.value);
            onDone();
          }}
        >
          <ListItemIcon>{option.symbol}</ListItemIcon>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="body2">{option.name}</Typography>
            {option.hint ? (
              <Typography variant="caption" color="text.secondary" component="div">
                {option.hint}
              </Typography>
            ) : null}
          </Box>
          {option.value === preference.value ? (
            <CheckIcon fontSize="small" sx={{ ml: 1.5, color: "primary.main" }} />
          ) : null}
        </MenuItem>
      ))}
    </>
  );
}

/**
 * Branches with concrete types: `Preference<A> | Preference<B>` cannot be
 * handed to a generic component, because `choose` is contravariant.
 */
export function PreferenceLevel({
  level,
  appearance,
  language,
  onBack,
  onDone,
}: {
  level: MenuLevel;
  appearance: Preference<ColorPreference>;
  language: Preference<LanguagePreference>;
  onBack: () => void;
  onDone: () => void;
}) {
  if (level === "appearance") {
    return <PreferenceChoice preference={appearance} onBack={onBack} onDone={onDone} />;
  }

  if (level === "language") {
    return <PreferenceChoice preference={language} onBack={onBack} onDone={onDone} />;
  }

  return null;
}
