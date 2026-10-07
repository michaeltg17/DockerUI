import { useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';

import {
  useAddShortcut,
  useIcons,
  useUpdateShortcut,
} from '../hooks/use-shortcuts';
import type { Shortcut } from '../types';

import { AppIcon } from './app-icon';
import { IconPickerDialog } from './icon-picker-dialog';

type ShortcutDialogProps = {
  open: boolean;
  /** The shortcut to edit; when null a new shortcut is created. */
  initial?: Shortcut | null;
  onClose: () => void;
};

const isEditingShortcut = (initial?: Shortcut | null) => Boolean(initial?.name);

const normalizeUrl = (value: string) => {
  const trimmed = value.trim();
  return /^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
};

const isSaveableUrl = (value: string) => {
  try {
    const url = new URL(normalizeUrl(value));
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
};

/** The API reports problems as RFC 9457 documents; surface the human message. */
export const getErrorMessage = (error: unknown) => {
  const data = (error as { response?: { data?: unknown } } | null)?.response
    ?.data;

  if (typeof data === 'object' && data !== null) {
    const { detail, message } = data as { detail?: unknown; message?: unknown };

    if (typeof detail === 'string' && detail.length > 0) return detail;
    if (typeof message === 'string' && message.length > 0) return message;
  }

  return 'Could not save the shortcut. Try again.';
};

const fieldClasses =
  'h-9 rounded-md border border-input bg-background px-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring';

export const ShortcutDialog = ({
  open,
  initial,
  onClose,
}: ShortcutDialogProps) => {
  if (!open) return null;
  return <ShortcutDialogForm initial={initial} onClose={onClose} />;
};

/**
 * Mounted fresh every time the dialog opens, so the fields, the saving flag
 * and the icon picker always start clean instead of leaking state from the
 * previous session (an async reset effect could race a fast re-open).
 */
const ShortcutDialogForm = ({
  initial,
  onClose,
}: Omit<ShortcutDialogProps, 'open'>) => {
  const isEditing = isEditingShortcut(initial);
  const { data: icons = [] } = useIcons();
  const addShortcut = useAddShortcut();
  const updateShortcut = useUpdateShortcut();

  const initialName = initial?.name ?? '';
  const initialIcon = initial?.icon ?? null;
  const initialUrl = initial?.url ?? '';

  const [name, setName] = useState(initialName);
  const [url, setUrl] = useState(initialUrl);
  const [icon, setIcon] = useState(initialIcon ?? '');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const nameInputRef = useRef<HTMLInputElement>(null);

  const isSaving = addShortcut.isPending || updateShortcut.isPending;

  const trimmedName = name.trim();
  const canSave = trimmedName.length > 0 && isSaveableUrl(url) && !isSaving;

  // Anything that would survive a save differs from what was shown on open;
  // while that is the case the backdrop must not close the dialog and lose it.
  const dirty =
    trimmedName !== initialName.trim() ||
    url.trim() !== initialUrl.trim() ||
    (icon === '' ? null : icon) !== initialIcon;

  // While saving is blocked, say why: the disabled button would otherwise
  // leave the user guessing what is missing.
  const saveHint =
    !canSave && !isSaving
      ? trimmedName.length === 0
        ? 'Enter a name.'
        : 'Enter a valid URL, e.g. https://example.com.'
      : null;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    const shortcut: Shortcut = {
      name: trimmedName,
      icon: icon === '' ? null : icon,
      url: normalizeUrl(url),
    };

    try {
      if (isEditing) {
        await updateShortcut.mutateAsync({ name: initialName, shortcut });
      } else {
        await addShortcut.mutateAsync(shortcut);
      }
      onClose();
    } catch (submitError) {
      // The api-client interceptor also surfaces the error as a toast; the
      // dialog stays open so the user can fix the input and retry.
      setError(getErrorMessage(submitError));
    }
  };

  return (
    <Dialog
      title={isEditing ? `Edit ${initialName}` : 'Add shortcut'}
      dirty={dirty}
      initialFocusRef={nameInputRef}
      onClose={onClose}
    >
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
              name={trimmedName || '?'}
              className="size-16 text-xl"
            />
          </button>

          <div className="flex min-w-0 flex-1 flex-col gap-4">
            <label className="flex flex-col gap-1.5 text-sm">
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
                placeholder="e.g. GitHub"
                className={fieldClasses}
              />
            </label>

            <label className="flex flex-col gap-1.5 text-sm">
              <span className="font-medium">URL</span>
              <input
                type="text"
                inputMode="url"
                required
                value={url}
                onChange={(event) => {
                  setUrl(event.target.value);
                  setError(null);
                }}
                placeholder="e.g. https://example.com or example.com"
                className={fieldClasses}
              />
            </label>
          </div>
        </div>

        <div className="flex flex-col gap-2 pt-1">
          {saveHint && (
            <p className="text-sm text-muted-foreground">{saveHint}</p>
          )}

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
              {isEditing ? 'Save' : 'Create'}
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
    </Dialog>
  );
};
