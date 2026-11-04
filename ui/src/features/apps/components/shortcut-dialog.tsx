import { useRef, useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { ErrorSlot } from '@/components/ui/error-slot';
import { FieldError } from '@/components/ui/field-error';
import { Input } from '@/components/ui/input';
import { getErrorMessage } from '@/lib/api-client';
import { hasUrlScheme, isValidUrl, normalizeUrl } from '@/lib/url';

import {
  useAddShortcut,
  useIcons,
  useUpdateShortcut,
} from '../hooks/use-shortcuts';
import type { Shortcut } from '../types';

import { AppIcon, randomHue } from './app-icon';
import { IconPickerDialog } from './icon-picker-dialog';

type ShortcutDialogProps = {
  open: boolean;
  /** The shortcut to edit; when null a new shortcut is created. */
  initial?: Shortcut | null;
  onClose: () => void;
};

const isEditingShortcut = (initial?: Shortcut | null) => Boolean(initial?.name);

type ShortcutValidation = {
  name: string | null;
  url: string | null;
};

/** Single source of truth for the dialog's field validation. */
const validateShortcut = (name: string, url: string): ShortcutValidation => ({
  name: name.trim() ? null : 'Enter a name.',
  url: isValidUrl(url) ? null : 'Enter a valid URL, e.g. https://example.com.',
});

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
  const [nameTouched, setNameTouched] = useState(false);
  const [urlTouched, setUrlTouched] = useState(false);

  // A new shortcut gets a random hue, an existing one keeps its stored color;
  // it never changes afterwards.
  const [color] = useState(() => initial?.color ?? randomHue());

  const nameInputRef = useRef<HTMLInputElement>(null);

  const isSaving = addShortcut.isPending || updateShortcut.isPending;

  const trimmedName = name.trim();
  const validation = validateShortcut(name, url);
  const canSave =
    validation.name === null && validation.url === null && !isSaving;

  // Anything that would survive a save differs from what was shown on open;
  // while that is the case the backdrop must not close the dialog and lose it.
  const dirty =
    trimmedName !== initialName.trim() ||
    url.trim() !== initialUrl.trim() ||
    (icon === '' ? null : icon) !== initialIcon;

  // A field's message shows once the user has typed in it or left it, and keeps
  // showing (recomputed) until the value is valid; both fields validate together
  // instead of one at a time.
  const { name: nameError, url: urlError } = validation;
  const showNameError =
    (name !== initialName || nameTouched) && nameError !== null;
  const showUrlError = (url !== initialUrl || urlTouched) && urlError !== null;

  // A scheme-less url is stored with https prefixed, so show the result while
  // the user is deciding what to type instead of surprising them on save.
  const urlSchemeHint =
    url.trim() !== '' && !hasUrlScheme(url) && validation.url === null
      ? normalizeUrl(url)
      : null;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    const shortcut: Shortcut = {
      name: trimmedName,
      icon: icon === '' ? null : icon,
      url: normalizeUrl(url),
      color,
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
      setError(
        getErrorMessage(submitError, 'Could not save the shortcut. Try again.'),
      );
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
        <button
          type="button"
          onClick={() => setPickerOpen(true)}
          aria-label="Choose icon"
          className="mx-auto rounded-2xl transition-shadow hover:ring-2 hover:ring-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <AppIcon
            icon={icon === '' ? null : icon}
            name={trimmedName || '?'}
            hue={color}
            className="size-24 text-3xl"
          />
        </button>

        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">Name</span>
          <Input
            ref={nameInputRef}
            type="text"
            required
            aria-label="Name"
            aria-invalid={showNameError}
            value={name}
            onChange={(event) => {
              setName(event.target.value);
              setError(null);
            }}
            onBlur={() => setNameTouched(true)}
            placeholder="e.g. GitHub"
          />
          <FieldError error={showNameError ? nameError : null} />
        </label>

        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">URL</span>
          <Input
            type="text"
            inputMode="url"
            required
            aria-label="URL"
            aria-invalid={showUrlError}
            value={url}
            onChange={(event) => {
              setUrl(event.target.value);
              setError(null);
            }}
            onBlur={() => setUrlTouched(true)}
            placeholder="e.g. https://example.com or example.com"
          />
          <FieldError
            error={showUrlError ? urlError : null}
            hint={urlSchemeHint ? `Will open as ${urlSchemeHint}` : null}
          />
        </label>

        <div className="flex flex-col gap-2 pt-1">
          <ErrorSlot error={error} />

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
