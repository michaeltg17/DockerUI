import { Eye, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/components/ui/button';

import { useHiddenApps, useSetAppVisibility } from '../hooks/use-apps';
import type { App } from '../types';

import { AppIcon } from './app-icon';

type HiddenAppsDialogProps = {
  open: boolean;
  onClose: () => void;
};

const searchInputClasses =
  'h-9 w-full rounded-md border border-input bg-background pl-8 pr-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring';

export const HiddenAppsDialog = ({ open, onClose }: HiddenAppsDialogProps) => {
  if (!open) return null;
  return <HiddenAppsDialogContent onClose={onClose} />;
};

/**
 * Mounted fresh every time the dialog opens, so the list always starts from the
 * server's current hidden apps and the search box is empty.
 */
const HiddenAppsDialogContent = ({ onClose }: { onClose: () => void }) => {
  const { data, isPending } = useHiddenApps();
  const setVisibility = useSetAppVisibility();
  const [search, setSearch] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);

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

  const hiddenApps = useMemo(() => {
    const apps = data ?? [];
    const query = search.trim().toLowerCase();
    if (query.length === 0) return apps;
    return apps.filter((app) => app.name.toLowerCase().includes(query));
  }, [data, search]);

  const showApp = (app: App) => {
    void setVisibility.mutate({ name: app.name, hidden: false });
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40 p-6"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label="Hidden apps"
        className="flex max-h-[80vh] w-full max-w-2xl flex-col overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold">Hidden apps</h2>
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
              aria-label="Search hidden apps by name"
              placeholder="Search hidden apps by name"
              className={searchInputClasses}
            />
          </div>
        </div>

        <div className="flex flex-col gap-2 overflow-auto p-4">
          {isPending ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              Loading hidden apps…
            </p>
          ) : hiddenApps.length === 0 ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              {search.trim().length > 0
                ? `No hidden apps match &ldquo;${search.trim()}&rdquo;.`
                : 'No hidden apps. Everything is shown on the dashboard.'}
            </p>
          ) : (
            hiddenApps.map((app) => (
              <div
                key={app.name}
                className="flex items-center gap-3 rounded-lg border border-border p-3"
              >
                <AppIcon
                  icon={app.icon}
                  name={app.name}
                  className="size-10 shrink-0"
                />
                <span className="min-w-0 flex-1 truncate text-sm font-medium">
                  {app.name}
                </span>
                <Button
                  type="button"
                  variant="outline"
                  disabled={setVisibility.isPending}
                  onClick={() => showApp(app)}
                >
                  <Eye className="size-4" aria-hidden="true" />
                  Show
                </Button>
              </div>
            ))
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
};
