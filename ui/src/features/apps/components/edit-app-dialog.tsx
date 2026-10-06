import { X } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/components/ui/button';

import { useSetAppSettings } from '../hooks/use-apps';
import { useIcons } from '../hooks/use-shortcuts';
import type { App } from '../types';

import { AppIcon } from './app-icon';
import { IconPickerDialog } from './icon-picker-dialog';

type EditAppDialogProps = {
  open: boolean;
  app: App | null;
  onClose: () => void;
};

const normalizeUrl = (value: string) => {
  const trimmed = value.trim();
  return /^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
};

const isSaveableUrl = (value: string) => {
  if (value.trim().length === 0) return true;
  try {
    const url = new URL(normalizeUrl(value));
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
};

/** The API reports problems as RFC 9457 documents; surface the human message. */
const getErrorMessage = (error: unknown) => {
  const data = (error as { response?: { data?: unknown } } | null)?.response
    ?.data;

  if (typeof data === 'object' && data !== null) {
    const { detail, message } = data as { detail?: unknown; message?: unknown };

    if (typeof detail === 'string' && detail.length > 0) return detail;
    if (typeof message === 'string' && message.length > 0) return message;
  }

  return 'Could not save the app. Try again.';
};

const fieldClasses =
  'h-9 rounded-md border border-input bg-background px-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring';

const focusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export const EditAppDialog = ({ open, app, onClose }: EditAppDialogProps) => {
  if (!open || app === null) return null;
  return <EditAppDialogForm app={app} onClose={onClose} />;
};

/**
 * Mounted fresh every time the dialog opens, so the fields, the saving flag
 * and the icon picker always start clean instead of leaking state from the
 * previous session (an async reset effect could race a fast re-open).
 */
const EditAppDialogForm = ({
  app,
  onClose,
}: {
  app: App;
  onClose: () => void;
}) => {
  const { data: icons = [] } = useIcons();
  const setSettings = useSetAppSettings();

  const [displayName, setDisplayName] = useState(app.displayName);
  const [icon, setIcon] = useState(app.icon ?? '');
  const [url, setUrl] = useState(app.url ?? '');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dialogRef = useRef<HTMLDivElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);

  const isSaving = setSettings.isPending;

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      // Escape closes the icon picker first; the dialog closes only when no
      // other dialog is layered on top of it.
      if (event.key === 'Escape' && !pickerOpen) onClose();

      if (event.key !== 'Tab' || pickerOpen || !dialogRef.current) return;

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
  }, [onClose, pickerOpen]);

  useEffect(() => {
    const previouslyFocused =
      document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
    nameInputRef.current?.focus();

    return () => {
      if (previouslyFocused?.isConnected) previouslyFocused.focus();
    };
  }, []);

  const canSave = isSaveableUrl(url) && !isSaving;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    try {
      await setSettings.mutateAsync({
        name: app.name,
        settings: {
          displayName: displayName.trim(),
          icon,
          url: url.trim().length === 0 ? '' : normalizeUrl(url),
        },
      });
      onClose();
    } catch (submitError) {
      // The api-client interceptor also surfaces the error as a toast; the
      // dialog stays open so the user can fix the input and retry.
      setError(getErrorMessage(submitError));
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
        aria-labelledby="edit-app-dialog-title"
        className="w-full max-w-md overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 id="edit-app-dialog-title" className="text-sm font-semibold">
            Edit {app.displayName}
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
          <div className="flex gap-3">
            <button
              type="button"
              onClick={() => setPickerOpen(true)}
              aria-label="Choose icon"
              className="shrink-0 self-start rounded-2xl transition-shadow hover:ring-2 hover:ring-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <AppIcon
                icon={icon === '' ? null : icon}
                name={displayName.trim() || app.name}
                className="size-16 text-xl"
              />
            </button>

            <div className="flex min-w-0 flex-1 flex-col gap-4">
              <label className="flex flex-col gap-1.5 text-sm">
                <span className="font-medium">Name</span>
                <input
                  ref={nameInputRef}
                  type="text"
                  value={displayName}
                  onChange={(event) => {
                    setDisplayName(event.target.value);
                    setError(null);
                  }}
                  placeholder={app.name}
                  className={fieldClasses}
                />
              </label>

              <label className="flex flex-col gap-1.5 text-sm">
                <span className="font-medium">URL</span>
                <input
                  type="text"
                  inputMode="url"
                  value={url}
                  onChange={(event) => {
                    setUrl(event.target.value);
                    setError(null);
                  }}
                  placeholder="Leave empty to auto-detect"
                  className={fieldClasses}
                />
              </label>
            </div>
          </div>

          <div className="flex flex-col gap-2 pt-1">
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
              <Button type="submit" disabled={!canSave} isLoading={isSaving}>
                Save
              </Button>
            </div>
          </div>
        </form>

        {pickerOpen && (
          <IconPickerDialog
            icons={icons}
            selected={icon === '' ? null : icon}
            onSelect={(path) => {
              setIcon(path ?? '');
              setPickerOpen(false);
            }}
            onClose={() => setPickerOpen(false)}
          />
        )}
      </div>
    </div>,
    document.body,
  );
};
