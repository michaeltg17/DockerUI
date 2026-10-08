import { Eye, Search } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { Dialog } from '@/components/ui/dialog';

import { useHiddenApps, useSetAppVisibility } from '../hooks/use-apps';
import type { App } from '../types';

import { AppIcon } from './app-icon';

type HiddenAppsDialogProps = {
  open: boolean;
  onClose: () => void;
};

const searchInputClasses =
  'h-9 w-full rounded-md border border-input bg-background pl-8 pr-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring';

// Mirrors the dashboard card so hidden apps look identical to the grid.
const cardClasses =
  'group flex size-full flex-col items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-accent hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-60';

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

  useEffect(() => {
    // The base dialog focuses the search box on open.
    searchRef.current?.focus();
  }, []);

  const hiddenApps = useMemo(() => {
    const apps = data ?? [];
    const query = search.trim().toLowerCase();
    if (query.length === 0) return apps;
    return apps.filter((app) => app.name.toLowerCase().includes(query));
  }, [data, search]);

  const showApp = (app: App) => {
    void setVisibility.mutate({ name: app.name, hidden: false });
  };

  return (
    <Dialog
      title="Hidden apps"
      layered
      onClose={onClose}
      className="flex max-h-[80vh] max-w-2xl flex-col"
    >
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

      <div className="overflow-auto p-4">
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
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4">
            {hiddenApps.map((app) => (
              <button
                key={app.name}
                type="button"
                onClick={() => showApp(app)}
                disabled={setVisibility.isPending}
                aria-label={`Show ${app.name}`}
                className={cardClasses}
              >
                <div className="relative">
                  <AppIcon
                    icon={app.icon}
                    name={app.displayName}
                    className="size-24 text-3xl"
                  />
                  {/* The eye is the affordance for re-showing the app on hover. */}
                  <span
                    className="absolute inset-0 flex items-center justify-center opacity-0 transition-opacity duration-150 group-hover:opacity-100"
                    aria-hidden="true"
                  >
                    <span className="flex size-10 items-center justify-center rounded-full bg-slate-900/40">
                      <Eye className="size-5 text-white" />
                    </span>
                  </span>
                </div>
                <h3 className="max-w-full truncate text-sm font-medium">
                  {app.displayName}
                </h3>
              </button>
            ))}
          </div>
        )}
      </div>
    </Dialog>
  );
};
