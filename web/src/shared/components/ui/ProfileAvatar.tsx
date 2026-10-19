import Avatar from "@mui/material/Avatar";
import type { SxProps, Theme } from "@mui/material/styles";

/**
 * A person's picture — and the initials as long as there is none.
 *
 * <strong>Initials and not a grey symbol.</strong> A placeholder that looks the
 * same for everybody says nothing; "AB" on a coloured ground says whose
 * account is open right now. On a platform with company switching that is no
 * ornament: whoever acts for a company must see as whom they are doing it.
 *
 * <strong>The colour follows the name, not chance.</strong> It has to be the
 * same across sessions and devices, otherwise it is not a mark of recognition
 * but flicker.
 *
 * <c>src</c> is already there although nobody can upload a picture yet. The day
 * it becomes possible should fill a field and not rebuild a component.
 */
export function ProfileAvatar({
  name,
  src,
  size = 32,
  sx,
}: {
  name: string;
  src?: string | null;
  size?: number;
  sx?: SxProps<Theme>;
}) {
  const initials = initialsOf(name);

  return (
    <Avatar
      src={src ?? undefined}
      alt={name}
      sx={[
        {
          width: size,
          height: size,
          fontSize: size * 0.4,
          fontWeight: 660,
          bgcolor: src != null ? undefined : colorFor(name),
          // The name from the theme, not a literal: `colorFor` yields a
          // lightness of 38 %, so the circle carries light text — but which
          // colour "light" means is decided by the theme and not by this
          // component. A written-out white stood here — the only place in the
          // tree where a colour stood literally.
          color: "common.white",
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
    >
      {src == null ? initials : null}
    </Avatar>
  );
}

/**
 * At most two letters: the first and the last part of the name.
 *
 * Nothing good would come from an address — `max.werber@…` gives "M" and not
 * "MW". That is why the session has carried the display name since then.
 */
export function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter((part) => part.length > 0);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return (parts[0] as string).charAt(0).toUpperCase();

  const first = parts[0] as string;
  const last = parts[parts.length - 1] as string;
  return (first.charAt(0) + last.charAt(0)).toUpperCase();
}

/**
 * A colour from the name — stable, and dark enough for white text.
 *
 * The hue comes from a checksum of the name; saturation and lightness are
 * fixed. Without the fixed lightness every second colour would be too light
 * for white text, and the text colour would then have to guess.
 */
export function colorFor(name: string): string {
  let sum = 0;
  for (let i = 0; i < name.length; i += 1) {
    sum = name.charCodeAt(i) + ((sum << 5) - sum);
  }
  return `hsl(${Math.abs(sum) % 360} 46% 38%)`;
}
