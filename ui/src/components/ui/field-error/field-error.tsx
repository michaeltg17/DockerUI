type FieldErrorProps = {
  /** The error shown in red; takes precedence over the hint. */
  error?: string | null;
  /** The muted guidance shown when there is no error. */
  hint?: string | null;
};

/**
 * The reserved line under a form input: the error in red, or the muted hint,
 * or nothing — the height is kept so an error appearing cannot shift the
 * form below it. The error is announced to assistive technology as an alert.
 */
export const FieldError = ({ error, hint }: FieldErrorProps) => (
  <p className="min-h-4 text-xs">
    {error ? (
      <span role="alert" className="text-destructive">
        {error}
      </span>
    ) : hint ? (
      <span className="text-muted-foreground">{hint}</span>
    ) : null}
  </p>
);
