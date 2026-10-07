import { useEffect, useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';

import { useRenameDashboard } from '../hooks/use-rename-dashboard';

import { getErrorMessage } from './shortcut-dialog';

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
      setError(getErrorMessage(renameError));
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
            <input
              ref={nameInputRef}
              type="text"
              required
              aria-invalid={nameError !== null}
              value={name}
              onChange={(event) => {
                setName(event.target.value);
                setError(null);
              }}
              className="h-9 rounded-md border border-input bg-background px-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            />
            {nameError && (
              <p className="text-xs text-destructive">{nameError}</p>
            )}
          </label>
          <p className="text-xs text-muted-foreground">
            Shown as the page title and saved to the dashboard settings.
          </p>
        </div>

        {error && (
          <p
            role="alert"
            className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive"
          >
            {error}
          </p>
        )}

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
