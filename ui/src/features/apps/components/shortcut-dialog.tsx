import { X } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/components/ui/button';

import {
  useAddShortcut,
  useIcons,
  useUpdateShortcut,
} from '../hooks/use-shortcuts';
import type { Shortcut } from '../types';

import { AppIcon } from './app-icon';

type ShortcutDialogProps = {
  open: boolean;
  /** The shortcut to edit; when null a new shortcut is created. */
  initial: Shortcut | null;
  onClose: () => void;
};

const isAbsoluteUrl = (value: string) => {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
};

const iconLabel = (path: string) => {
  const file = path.split('/').pop() ?? path;
  return file.replace(/\.[a-z]+$/i, '');
};

const fieldClasses =
  'h-9 rounded-md border border-input bg-background px-3 text-sm text-foreground shadow-sm transition-colors placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30';

export const ShortcutDialog = ({
  open,
  initial,
  onClose,
}: ShortcutDialogProps) => {
  const { data: icons = [] } = useIcons();
  const addShortcut = useAddShortcut();
  const updateShortcut = useUpdateShortcut();

  const [name, setName] = useState('');
  const [url, setUrl] = useState('');
  const [icon, setIcon] = useState('');

  const isEditing = initial !== null;
  const isSaving = addShortcut.isPending || updateShortcut.isPending;

  const initialName = initial?.name ?? '';
  const initialIcon = initial?.icon ?? '';
  const initialUrl = initial?.url ?? '';

  useEffect(() => {
    if (!open) return;
    setName(initialName);
    setUrl(initialUrl);
    setIcon(initialIcon);
  }, [open, initialName, initialIcon, initialUrl]);

  useEffect(() => {
    if (!open) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  const trimmedName = name.trim();
  const trimmedUrl = url.trim();
  const canSave =
    trimmedName.length > 0 && isAbsoluteUrl(trimmedUrl) && !isSaving;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSave) return;

    const shortcut: Shortcut = {
      name: trimmedName,
      icon: icon === '' ? null : icon,
      url: trimmedUrl,
    };

    try {
      if (isEditing) {
        await updateShortcut.mutateAsync({ name: initialName, shortcut });
      } else {
        await addShortcut.mutateAsync(shortcut);
      }
      onClose();
    } catch {
      // The api-client interceptor already surfaces the error as a toast.
    }
  };

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={isEditing ? `Edit ${initialName}` : 'Add shortcut'}
        className="w-full max-w-md overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold">
            {isEditing ? `Edit ${initialName}` : 'Add shortcut'}
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
          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">Name</span>
            <input
              type="text"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="e.g. GitHub"
              className={fieldClasses}
            />
          </label>

          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium">URL</span>
            <input
              type="url"
              value={url}
              onChange={(event) => setUrl(event.target.value)}
              placeholder="https://example.com"
              className={fieldClasses}
            />
          </label>

          <div className="flex items-center gap-3">
            <AppIcon
              icon={icon === '' ? null : icon}
              name={trimmedName || '?'}
              className="size-12 shrink-0 text-base"
            />
            <label className="flex flex-1 flex-col gap-1.5 text-sm">
              <span className="font-medium">Icon</span>
              <select
                value={icon}
                onChange={(event) => setIcon(event.target.value)}
                className={fieldClasses}
              >
                <option value="">None</option>
                {icons.map((path) => (
                  <option key={path} value={path}>
                    {iconLabel(path)}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="flex justify-end gap-2 pt-1">
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!canSave} isLoading={isSaving}>
              {isEditing ? 'Save' : 'Create'}
            </Button>
          </div>
        </form>
      </div>
    </div>,
    document.body,
  );
};
