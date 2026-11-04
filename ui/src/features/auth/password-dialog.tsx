import { useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { ErrorSlot } from '@/components/ui/error-slot';
import { FieldError } from '@/components/ui/field-error';
import { Input } from '@/components/ui/input';
import { getErrorMessage } from '@/lib/api-client';

import {
  assessPasswordStrength,
  type PasswordStrength,
} from './password-strength';
import { useAuth } from './use-auth';

type PasswordDialogProps = {
  open: boolean;
  onClose: () => void;
};

export const PasswordDialog = ({ open, onClose }: PasswordDialogProps) => {
  if (!open) return null;
  return <PasswordDialogForm onClose={onClose} />;
};

// The strength rating's presentation; the rating itself is informational only.
const strengthPresentation: Record<
  PasswordStrength,
  { label: string; className: string }
> = {
  weak: { label: 'Weak', className: 'text-destructive' },
  medium: { label: 'Medium', className: 'text-warning' },
  strong: { label: 'Strong', className: 'text-success' },
};

/**
 * Mounted fresh every time the dialog opens, so the fields, the saving flag
 * and any error start clean instead of leaking state from the previous
 * session (an async reset effect could race a fast re-open).
 */
const PasswordDialogForm = ({ onClose }: Omit<PasswordDialogProps, 'open'>) => {
  const passwordSet = useAuth((auth) => auth.passwordSet);
  const setPassword = useAuth((auth) => auth.setPassword);

  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const currentInputRef = useRef<HTMLInputElement>(null);
  const nextInputRef = useRef<HTMLInputElement>(null);

  const trimmedCurrent = current.trim();
  const trimmedNext = next.trim();
  const trimmedConfirm = confirm.trim();

  // In change mode an empty new password removes the password.
  const isRemoving = passwordSet && trimmedNext === '';

  // The strength rating shows while a new password is being typed; it never
  // blocks the save (the API accepts any non-empty password).
  const strength =
    trimmedNext.length > 0 ? assessPasswordStrength(trimmedNext) : null;

  const matchError =
    trimmedNext === trimmedConfirm ? null : 'The new passwords do not match.';
  const currentError =
    passwordSet && trimmedCurrent === '' ? 'Enter the current password.' : null;

  const canSave =
    matchError === null &&
    currentError === null &&
    // Open mode has no 'remove' escape: an empty new password is not a valid
    // save there. In change mode an empty new password is exactly how the
    // password is removed, so only the match and current checks apply.
    (passwordSet || trimmedNext.length > 0) &&
    !isSaving;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    setIsSaving(true);
    setError(null);

    try {
      await setPassword(
        passwordSet ? trimmedCurrent : null,
        isRemoving ? null : trimmedNext,
      );
      onClose();
    } catch (submitError) {
      // The api-client also keeps the session state correct; the dialog
      // stays open so the user can fix the input and retry.
      setError(
        getErrorMessage(submitError, 'Could not save the password. Try again.'),
      );
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Dialog
      title={passwordSet ? 'Change password' : 'Set password'}
      initialFocusRef={passwordSet ? currentInputRef : nextInputRef}
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-4">
        {passwordSet && (
          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">Current password</span>
            <Input
              ref={currentInputRef}
              type="password"
              name="CurrentPassword"
              aria-label="Current password"
              required
              autoComplete="current-password"
              value={current}
              onChange={(event) => {
                setCurrent(event.target.value);
                setError(null);
              }}
            />
            <FieldError error={currentError} />
          </label>
        )}

        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            New password{passwordSet ? ' (optional)' : ''}
          </span>
          <Input
            ref={nextInputRef}
            type="password"
            name="NewPassword"
            aria-label="New password"
            autoComplete="new-password"
            value={next}
            onChange={(event) => {
              setNext(event.target.value);
              setError(null);
            }}
          />
          <p className="min-h-4 text-xs">
            {strength ? (
              <span className={strengthPresentation[strength].className}>
                {strengthPresentation[strength].label}
              </span>
            ) : isRemoving ? (
              <span className="text-muted-foreground">
                Leave empty to remove the password.
              </span>
            ) : null}
          </p>
        </label>

        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Confirm new password{passwordSet ? ' (optional)' : ''}
          </span>
          <Input
            type="password"
            name="ConfirmNewPassword"
            aria-label="Confirm new password"
            autoComplete="new-password"
            value={confirm}
            onChange={(event) => {
              setConfirm(event.target.value);
              setError(null);
            }}
          />
          <FieldError error={matchError} />
        </label>

        <div className="flex flex-col gap-2 pt-1">
          <ErrorSlot error={error} />

          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!canSave} isLoading={isSaving}>
              {passwordSet ? 'Save' : 'Set password'}
            </Button>
          </div>
        </div>
      </form>
    </Dialog>
  );
};
