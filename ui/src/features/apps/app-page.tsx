import { Search, Ship, X } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { Helmet } from 'react-helmet-async';

import { useNotifications } from '@/components/ui/notifications';
import { Spinner } from '@/components/ui/spinner';
import { cn } from '@/utils/cn';

import { getLogs } from './api/get-logs';
import { AppsGrid } from './components/apps-grid';
import { DashboardMenu } from './components/dashboard-menu';
import { HiddenAppsDialog } from './components/hidden-apps-dialog';
import { LogsDialog } from './components/logs-dialog';
import { RenameDialog } from './components/rename-dialog';
import { ShortcutDialog } from './components/shortcut-dialog';
import { filterApps } from './filter-apps';
import { useApps, useSetAppOrder } from './hooks/use-apps';
import { useAppsHub } from './hooks/use-apps-hub';
import { useScanLan } from './hooks/use-lan';
import { useSettings } from './hooks/use-settings';

export const AppsPage = () => {
  const { data, isPending, isError, refetch } = useApps();
  const setOrder = useSetAppOrder();
  const { data: settings } = useSettings();
  const [query, setQuery] = useState('');
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [logsOpen, setLogsOpen] = useState(false);
  const [shortcutOpen, setShortcutOpen] = useState(false);
  const [renameOpen, setRenameOpen] = useState(false);
  const [hiddenAppsOpen, setHiddenAppsOpen] = useState(false);

  useAppsHub();

  const scanLan = useScanLan();

  const runScanLan = () => {
    // A LAN scan can take a while, so show a persistent notification with a
    // spinner while it runs and update it in place once the result arrives.
    const pendingId = useNotifications.getState().addNotification({
      type: 'info',
      title: 'Scanning LAN…',
      message: 'Looking for services on your network.',
      pending: true,
    });

    void scanLan
      .mutateAsync()
      .then((result) => {
        useNotifications.getState().updateNotification(pendingId, {
          type: 'success',
          title: 'LAN scan complete',
          message: `Found ${result.found} service${
            result.found === 1 ? '' : 's'
          } and added ${result.added} new.`,
          pending: false,
        });
      })
      .catch(() => {
        // The api-client interceptor already surfaces the error as a toast;
        // clear the in-progress notification so its spinner doesn't linger.
        useNotifications.getState().dismissNotification(pendingId);
      });
  };

  const apps = useMemo(() => filterApps(data ?? [], query), [data, query]);
  const hasQuery = query.trim().length > 0;

  return (
    <div className="flex min-h-screen flex-col bg-background">
      <Helmet>
        <title>{settings?.name}</title>
        <meta
          name="description"
          content="A lightweight user interface for your Docker stacks and more."
        />
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
              ref={searchInputRef}
              type="search"
              aria-label="Search apps"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search apps…"
              className={cn(
                'h-9 w-56 rounded-md border border-input bg-card pl-9 pr-3 text-sm text-foreground shadow-sm transition-colors placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30 sm:w-64',
                hasQuery && 'pr-8',
              )}
            />
            {hasQuery && (
              <button
                type="button"
                onClick={() => {
                  setQuery('');
                  searchInputRef.current?.focus();
                }}
                aria-label="Clear search"
                className="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              >
                <X className="size-4" aria-hidden="true" />
              </button>
            )}
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

      <RenameDialog
        open={renameOpen}
        currentName={settings?.name ?? 'Docker UI'}
        onClose={() => setRenameOpen(false)}
      />

      <HiddenAppsDialog
        open={hiddenAppsOpen}
        onClose={() => setHiddenAppsOpen(false)}
      />

      <DashboardMenu
        // The whole area below the header (cards, gaps, and empty space)
        // is the dashboard menu's trigger zone.
        className="flex-1"
        onAddShortcut={() => setShortcutOpen(true)}
        onScanLan={runScanLan}
        isScanningLan={scanLan.isPending}
        onViewLogs={() => setLogsOpen(true)}
        onRename={() => setRenameOpen(true)}
        onViewHiddenApps={() => setHiddenAppsOpen(true)}
      >
        <main
          className={cn(
            'mx-auto max-w-7xl px-6 py-8',
            // The content fades in once the apps finish loading, so the page
            // settles gently instead of popping in.
            !isPending && 'animate-fade-in',
          )}
        >
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
            <AppsGrid
              apps={apps}
              // Reordering a filtered subset would only pin that subset, so dragging
              // is enabled while the whole list is visible.
              onReorder={
                hasQuery ? undefined : (names) => void setOrder.mutate(names)
              }
            />
          )}
        </main>
      </DashboardMenu>
    </div>
  );
};
