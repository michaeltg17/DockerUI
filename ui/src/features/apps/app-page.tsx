import { Search, Ship } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Helmet } from 'react-helmet-async';

import { Spinner } from '@/components/ui/spinner';

import { getLogs } from './api/get-logs';
import { AppsGrid } from './components/apps-grid';
import { DashboardMenu } from './components/dashboard-menu';
import { LogsDialog } from './components/logs-dialog';
import { ShortcutDialog } from './components/shortcut-dialog';
import { filterApps } from './filter-apps';
import { useApps } from './hooks/use-apps';
import { useAppsHub } from './hooks/use-apps-hub';
import { useSettings } from './hooks/use-settings';

export const AppsPage = () => {
  const { data, isPending, isError, refetch } = useApps();
  const { data: settings } = useSettings();
  const [query, setQuery] = useState('');
  const [logsOpen, setLogsOpen] = useState(false);
  const [shortcutOpen, setShortcutOpen] = useState(false);

  useAppsHub();

  const apps = useMemo(() => filterApps(data ?? [], query), [data, query]);
  const hasQuery = query.trim().length > 0;

  return (
    <div className="min-h-screen bg-background">
      <Helmet>
        <title>{settings?.name ?? 'Docker UI'}</title>
        <meta name="description" content="Your Docker stacks at a glance" />
      </Helmet>

      <header className="border-b border-border bg-header text-header-foreground">
        <div className="mx-auto grid max-w-7xl grid-cols-[1fr_auto_1fr] items-center gap-3 px-6 py-4">
          <div aria-hidden="true" />
          <label className="relative block">
            <span className="sr-only">Search apps</span>
            <Search
              className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden="true"
            />
            <input
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search apps…"
              className="h-9 w-56 rounded-md border border-input bg-background pl-9 pr-3 text-sm text-foreground shadow-sm transition-colors placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30 sm:w-64"
            />
          </label>
          <div aria-hidden="true" />
        </div>
      </header>

      <LogsDialog
        open={logsOpen}
        title="Docker UI logs"
        scope="dashboard"
        fetchLogs={getLogs}
        onClose={() => setLogsOpen(false)}
      />

      <ShortcutDialog
        open={shortcutOpen}
        initial={null}
        onClose={() => setShortcutOpen(false)}
      />

      <DashboardMenu
        onAddShortcut={() => setShortcutOpen(true)}
        onViewLogs={() => setLogsOpen(true)}
      >
        <main className="mx-auto max-w-7xl px-6 py-8">
          {isPending ? (
            <div className="flex justify-center py-24">
              <Spinner size="xl" />
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center gap-4 py-24 text-center">
              <Ship
                className="size-10 text-muted-foreground"
                aria-hidden="true"
              />
              <p className="text-sm text-muted-foreground">
                Could not load apps. Is the Docker daemon reachable?
              </p>
              <button
                type="button"
                onClick={() => void refetch()}
                className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
              >
                Retry
              </button>
            </div>
          ) : apps.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-24 text-center text-muted-foreground">
              <Ship className="size-10" aria-hidden="true" />
              {hasQuery ? (
                <p className="text-sm">
                  No apps match &ldquo;{query.trim()}&rdquo;.
                </p>
              ) : (
                <p className="text-sm">
                  No apps found. Start a Docker Compose stack to see it here.
                </p>
              )}
            </div>
          ) : (
            <AppsGrid apps={apps} />
          )}
        </main>
      </DashboardMenu>
    </div>
  );
};
