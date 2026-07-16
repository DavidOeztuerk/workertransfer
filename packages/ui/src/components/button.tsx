import type { ButtonHTMLAttributes, ReactNode } from "react";

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  children: ReactNode;
  variant?: "primary" | "secondary" | "quiet";
};

export function Button({ children, className, variant = "primary", ...props }: ButtonProps) {
  const classes = ["wt-button", `wt-button--${variant}`, className].filter(Boolean).join(" ");

  return (
    <button className={classes} type="button" {...props}>
      {children}
    </button>
  );
}
