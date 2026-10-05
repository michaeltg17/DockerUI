import { Check, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/components/ui/button';

import { AppIcon } from './app-icon';

type IconPickerDialogProps = {
  icons: string[];
  selected: string | null;
  onSelect: (icon: string | null) => void;
  onClose: () => void;
};

export const iconLabel = (path: string) => {
  const fileName = path.split('/').pop();
  return fileName ? fileName.replace(/\.[a-z0-9]+$/i, '') : path;
};

const searchInputClasses =
  'h-9 w-full rounded-md border border-input bg-background pl-8 pr-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring';

export const IconPickerDialog = ({
  icons,
  selected,
  onSelect,
  onClose,
}: IconPickerDialogProps) => {
  const [search, setSearch] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    searchRef.current?.focus();
  }, []);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const visibleIcons = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (query.length === 0) return icons;
    return icons.filter((path) =>
      iconLabel(path).toLowerCase().includes(query),
    );
  }, [icons, search]);

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40 p-6"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Choose an icon"
        className="flex max-h-[80vh] w-full max-w-2xl flex-col overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold">Choose an icon</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>

        <div className="flex items-center gap-2 border-b border-border p-3">
          <div className="relative min-w-0 flex-1">
            <Search
              className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden="true"
            />
            <input
              ref={searchRef}
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              aria-label="Search icons by name"
              placeholder="Search icons by name"
              className={searchInputClasses}
            />
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={() => onSelect(null)}
          >
            None
          </Button>
        </div>

        <div className="grid flex-1 grid-cols-3 content-start gap-2 overflow-auto p-4 sm:grid-cols-4">
          {visibleIcons.map((path) => (
            <button
              key={path}
              type="button"
              onClick={() => onSelect(path)}
              title={iconLabel(path)}
              className="relative flex flex-col items-center gap-2 rounded-lg p-2 transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            >
              <AppIcon
                icon={path}
                name={iconLabel(path)}
                className="size-24 text-3xl"
              />
              <span className="max-w-full truncate text-xs text-muted-foreground">
                {iconLabel(path)}
              </span>
              {selected === path && (
                <span className="absolute right-1 top-1 flex size-5 items-center justify-center rounded-full bg-primary text-primary-foreground">
                  <Check className="size-3" aria-hidden="true" />
                </span>
              )}
            </button>
          ))}
          {visibleIcons.length === 0 && (
            <p className="col-span-full py-8 text-center text-sm text-muted-foreground">
              No icons match &ldquo;{search.trim()}&rdquo;.
            </p>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
};
