import Avatar from "@mui/material/Avatar";
import type { SxProps, Theme } from "@mui/material/styles";

/**
 * Das Bild einer Person — und die Initialen, solange es keines gibt.
 *
 * <strong>Initialen und nicht ein graues Symbol.</strong> Ein Platzhalter, der
 * für alle gleich aussieht, sagt nichts; „AB" auf farbigem Grund sagt, wessen
 * Konto gerade offen ist. Das ist auf einer Plattform mit
 * Unternehmenswechsel keine Zierde: wer für eine Firma handelt, muss sehen, als
 * wer er das tut.
 *
 * <strong>Die Farbe folgt dem Namen, nicht dem Zufall.</strong> Sie muss über
 * Sitzungen und Geräte hinweg dieselbe sein, sonst ist sie kein Erkennungs-
 * merkmal, sondern Flackern.
 *
 * <c>src</c> steht schon da, obwohl noch niemand ein Bild hochladen kann. Der
 * Tag, an dem es geht, soll ein Feld füllen und keine Komponente umbauen.
 */
export function ProfilAvatar({
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
  const initialen = initialenAus(name);

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
          bgcolor: src != null ? undefined : farbeFuer(name),
          color: "#fff",
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
    >
      {src == null ? initialen : null}
    </Avatar>
  );
}

/**
 * Höchstens zwei Buchstaben: der erste und der letzte Namensteil.
 *
 * Aus einer Adresse würde nichts Gutes — `max.werber@…` ergibt „M" und nicht
 * „MW". Deshalb trägt die Sitzung seit heute den Anzeigenamen.
 */
export function initialenAus(name: string): string {
  const teile = name.trim().split(/\s+/).filter((teil) => teil.length > 0);
  if (teile.length === 0) return "?";
  if (teile.length === 1) return (teile[0] as string).charAt(0).toUpperCase();

  const erster = teile[0] as string;
  const letzter = teile[teile.length - 1] as string;
  return (erster.charAt(0) + letzter.charAt(0)).toUpperCase();
}

/**
 * Eine Farbe aus dem Namen — beständig, und dunkel genug für weisse Schrift.
 *
 * Der Farbton kommt aus einer Prüfsumme des Namens; Sättigung und Helligkeit
 * stehen fest. Ohne die feste Helligkeit wäre jede zweite Farbe zu hell für
 * weissen Text, und dann müsste die Schriftfarbe raten.
 */
export function farbeFuer(name: string): string {
  let summe = 0;
  for (let i = 0; i < name.length; i += 1) {
    summe = name.charCodeAt(i) + ((summe << 5) - summe);
  }
  return `hsl(${Math.abs(summe) % 360} 46% 38%)`;
}
