import { X } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/components/ui/button';

import { useRenameDashboard } from '../hooks/use-rename-dashboard';

import { getErrorMessage } from './shortcut-dialog';

type RenameDialogProps = {
  open: boolean;
  /** The current dashboard name, pre-filled in the field. */
  currentName: string;
  onClose: () => void;
};

const focusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export const RenameDialog = ({
  open,
  currentName,
  onClose,
}: RenameDialogProps) => {
  const rename = useRenameDashboard();
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const dialogRef = useRef<HTMLDivElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    setName(currentName);
    setError(null);
  }, [open, currentName]);

  useEffect(() => {
    if (!open) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      if (event.key !== 'Tab' || !dialogRef.current) return;

      const focusable = [
        ...dialogRef.current.querySelectorAll<HTMLElement>(focusableSelector),
      ];
      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;
      const inside =
        active instanceof HTMLElement && dialogRef.current.contains(active);

      if (event.shiftKey) {
        if (!inside || active === first) {
          event.preventDefault();
          last.focus();
        }
      } else if (!inside || active === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  useEffect(() => {
    if (!open) return undefined;

    const previouslyFocused =
      document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
    nameInputRef.current?.focus();
    nameInputRef.current?.select();

    return () => {
      if (previouslyFocused?.isConnected) previouslyFocused.focus();
    };
  }, [open]);

  if (!open) return null;

  const trimmedName = name.trim();
  const canSave = trimmedName.length > 0 && !rename.isPending;

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

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      role="presentation"
      onPointerDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="rename-dialog-title"
        className="w-full max-w-md overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 id="rename-dialog-title" className="text-sm font-semibold">
            Rename dashboard
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-4">
          <div className="flex flex-col gap-1.5 text-sm">
            <label className="flex flex-col gap-1.5">
              <span className="font-medium">Name</span>
              <input
                ref={nameInputRef}
                type="text"
                required
                value={name}
                onChange={(event) => {
                  setName(event.target.value);
                  setError(null);
                }}
                className="h-9 rounded-md border border-input bg-background px-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              />
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
      </div>
    </div>,
    document.body,
  );
};
