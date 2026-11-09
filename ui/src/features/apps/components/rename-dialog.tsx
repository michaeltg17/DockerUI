import { useEffect, useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { ErrorSlot } from '@/components/ui/error-slot';
import { FieldError } from '@/components/ui/field-error';
import { Input } from '@/components/ui/input';
import { getErrorMessage } from '@/lib/api-client';

import { useRenameDashboard } from '../hooks/use-rename-dashboard';

type RenameDialogProps = {
  open: boolean;
  /** The current dashboard name, pre-filled in the field. */
  currentName: string;
  onClose: () => void;
};

export const RenameDialog = ({
  open,
  currentName,
  onClose,
}: RenameDialogProps) => {
  if (!open) return null;
  return <RenameDialogForm currentName={currentName} onClose={onClose} />;
};

/**
 * Mounted fresh every time the dialog opens, so the field always starts from
 * the current name instead of leaking state from the previous session (an
 * async reset effect could race a fast re-open).
 */
const RenameDialogForm = ({
  currentName,
  onClose,
}: Omit<RenameDialogProps, 'open'>) => {
  const rename = useRenameDashboard();
  const [name, setName] = useState(currentName);
  const [error, setError] = useState<string | null>(null);

  const nameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    // The base dialog focuses the input on open; select it so the first
    // keystroke replaces the current name.
    nameInputRef.current?.select();
  }, []);

  const trimmedName = name.trim();
  const canSave = trimmedName.length > 0 && !rename.isPending;
  const dirty = trimmedName !== currentName.trim();

  // Say why saving is blocked instead of leaving the button silently disabled.
  const nameError = trimmedName.length === 0 ? 'Enter a name.' : null;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    try {
      await rename.mutateAsync(trimmedName);
      onClose();
    } catch (renameError) {
      setError(
        getErrorMessage(
          renameError,
          'Could not rename the dashboard. Try again.',
        ),
      );
    }
  };

  return (
    <Dialog
      title="Rename dashboard"
      dirty={dirty}
      initialFocusRef={nameInputRef}
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-4">
        <div className="flex flex-col gap-1.5 text-sm">
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Name</span>
            <Input
              ref={nameInputRef}
              type="text"
              required
              aria-invalid={nameError !== null}
              value={name}
              onChange={(event) => {
                setName(event.target.value);
                setError(null);
              }}
            />
            <FieldError error={nameError} />
          </label>
          <p className="text-xs text-muted-foreground">
            Shown as the page title and saved to the dashboard settings.
          </p>
        </div>

        <ErrorSlot error={error} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            disabled={!canSave}
            isLoading={rename.isPending}
          >
            Save
          </Button>
        </div>
      </form>
    </Dialog>
  );
};
