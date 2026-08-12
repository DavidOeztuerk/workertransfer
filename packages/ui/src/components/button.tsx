import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from "react";

export type ButtonVariant = "primary" | "secondary" | "quiet";

interface Shared {
  children: ReactNode;
  variant?: ButtonVariant;
}

/**
 * Entweder ein Knopf oder ein Link — nie beides.
 *
 * Die Unterscheidung hängt an `href`, und sie ist keine Kosmetik: was
 * navigiert, muss ein `<a>` sein. Ein `<button onClick={navigate}>` nimmt dem
 * Nutzer Mittelklick, neuen Tab, Adresse kopieren und die Vorschau in der
 * Statusleiste — alles, was ein Link von sich aus kann.
 */
export type ButtonProps =
  | (Shared & Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> & { href?: undefined })
  | (Shared & Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "children"> & { href: string });

export function Button({ children, className, variant = "primary", ...props }: ButtonProps) {
  const classes = ["wt-button", `wt-button--${variant}`, className].filter(Boolean).join(" ");

  if (props.href !== undefined) {
    return (
      <a className={classes} {...props}>
        {children}
      </a>
    );
  }

  // `type="button"` als Standard: ein Knopf in einem Formular ohne type ist ein
  // Absende-Knopf, und das trifft dann die falsche Handlung.
  const { href: _ignored, ...buttonProps } = props;
  return (
    <button className={classes} type="button" {...buttonProps}>
      {children}
    </button>
  );
}
