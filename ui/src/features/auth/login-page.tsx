import { useEffect, useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { FieldError } from '@/components/ui/field-error';
import { Input } from '@/components/ui/input';
import { Logo } from '@/components/ui/logo';
import { getErrorMessage } from '@/lib/api-client';

import { PasswordResetDialog } from './password-reset-dialog';
import { useAuth } from './use-auth';

/**
 * Shown while a password is set and no valid session exists. The page title
 * already carries the dashboard name: a fresh load gets it from the SPA
 * fallback, an expired session from the page that was open.
 */
export const LoginPage = () => {
  const signIn = useAuth((auth) => auth.signIn);
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [forgotOpen, setForgotOpen] = useState(false);
  const passwordInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    passwordInputRef.current?.focus();
  }, []);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    setIsSubmitting(true);
    setError(null);

    try {
      await signIn(password);
    } catch (submitError) {
      // The api-client already kept the app on the login page; the form
      // stays open so the user can fix the password and retry.
      setError(getErrorMessage(submitError, 'Could not sign in. Try again.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-6">
      <div className="w-full max-w-sm">
        <div className="flex flex-col items-center gap-3">
          <Logo className="size-12 text-muted-foreground" />
          <h1 className="text-lg font-semibold">{document.title}</h1>
          <p className="text-center text-sm text-muted-foreground">
            Enter the dashboard password to continue.
          </p>
        </div>

        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-2">
          <label className="sr-only" htmlFor="login-password">
            Password
          </label>
          <Input
            id="login-password"
            ref={passwordInputRef}
            type="password"
            name="Password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
              setError(null);
            }}
            className="w-full border-muted-foreground"
          />

          {/* The dialog input error style: one reserved line, so the sign-in
              button stays close and an error appearing cannot shift the form. */}
          <FieldError error={error} />

          <Button
            type="submit"
            isLoading={isSubmitting}
            disabled={password.length === 0}
          >
            Sign in
          </Button>

          <div className="mt-2 flex justify-center">
            <button
              type="button"
              onClick={() => setForgotOpen(true)}
              className="text-sm text-muted-foreground underline-offset-4 transition-colors hover:text-foreground hover:underline"
            >
              Forgot password?
            </button>
          </div>
        </form>

        <PasswordResetDialog
          open={forgotOpen}
          onClose={() => setForgotOpen(false)}
        />
      </div>
    </div>
  );
};
