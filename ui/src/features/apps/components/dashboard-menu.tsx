import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Eye,
  EyeOff,
  Palette,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  ScrollText,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';

import {
  ContextMenu,
  type ContextMenuItem,
} from '@/components/ui/context-menu';
import { Spinner } from '@/components/ui/spinner';
import { useTheme } from '@/hooks/use-theme';
import { themes, type Theme } from '@/lib/theme';

import { restartSelf, setSelfVisibility, stopSelf } from '../api/self-actions';
import { APPS_QUERY_KEY, useApps } from '../hooks/use-apps';
import { useSettings } from '../hooks/use-settings';

const themeLabels: Record<Theme, string> = {
  light: 'Light',
  dark: 'Dark',
  docker: 'Docker',
  'docker-v2': 'Docker V2',
};

const RESTART_TIMEOUT_MS = 120_000;
const RESTART_POLL_MS = 1_000;

type DashboardMenuProps = {
  onAddShortcut: () => void;
  onViewLogs: () => void;
  /** Extra classes for the wrapper that defines the menu's trigger zone. */
  className?: string;
  children: ReactNode;
};

export const DashboardMenu = ({
  onAddShortcut,
  onViewLogs,
  className,
  children,
}: DashboardMenuProps) => {
  const { theme, setTheme } = useTheme();
  const { data: settings } = useSettings();
  const { data: apps } = useApps();
  const queryClient = useQueryClient();
  const [selfAction, setSelfAction] = useState<'restarting' | 'stopped' | null>(
    null,
  );

  const selfProject = settings?.self ?? null;
  const selfVisible =
    selfProject !== null &&
    (apps ?? []).some((app) => app.name === selfProject);

  const setVisibility = useMutation({
    mutationFn: (hidden: boolean) => setSelfVisibility(hidden),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
  });

  const waitForDashboard = async () => {
    const deadline = Date.now() + RESTART_TIMEOUT_MS;

    // Wait for the dashboard to go down first, confirming the restart is underway.
    // Reloading while the API is still up would cancel the in-flight restart request
    // before it reaches the daemon.
    while (Date.now() < deadline) {
      let up = false;
      try {
        const response = await fetch('/api/settings', {
          cache: 'no-store',
        });

        up = response.ok;
      } catch {
        // Connection refused: the dashboard is down, as expected.
      }

      if (!up) break;

      await new Promise((resolve) => setTimeout(resolve, RESTART_POLL_MS));
    }

    // Wait for the dashboard to come back, then reload into the fresh instance.
    while (Date.now() < deadline) {
      try {
        const response = await fetch('/api/settings', {
          cache: 'no-store',
        });

        if (response.ok) {
          window.location.reload();
          return;
        }
      } catch {
        // The dashboard is still down; keep polling.
      }

      await new Promise((resolve) => setTimeout(resolve, RESTART_POLL_MS));
    }

    // The dashboard did not come back; offer the manual reload instead.
    setSelfAction('stopped');
  };

  const runSelfAction = (action: 'restart' | 'stop') => {
    setSelfAction(action === 'restart' ? 'restarting' : 'stopped');
    // The dashboard goes down while this request is in flight, so the error is expected.
    void (action === 'restart' ? restartSelf() : stopSelf()).catch(
      () => undefined,
    );

    if (action === 'restart') {
      void waitForDashboard();
    }
  };

  const items: ContextMenuItem[] = [
    {
      id: 'add-shortcut',
      label: 'Add shortcut',
      icon: <Plus className="size-4" aria-hidden="true" />,
      onSelect: onAddShortcut,
    },
    {
      id: 'view-logs',
      label: 'View logs',
      icon: <ScrollText className="size-4" aria-hidden="true" />,
      onSelect: onViewLogs,
    },
    {
      id: 'theme',
      label: 'Theme',
      icon: <Palette className="size-4" aria-hidden="true" />,
      children: themes.map((t) => ({
        id: `theme-${t}`,
        label: themeLabels[t],
        selected: theme === t,
        onSelect: () => setTheme(t),
      })),
    },
    {
      id: 'toggle-visibility',
      label: selfVisible ? 'Hide Docker UI' : 'Show Docker UI',
      icon: selfVisible ? (
        <EyeOff className="size-4" aria-hidden="true" />
      ) : (
        <Eye className="size-4" aria-hidden="true" />
      ),
      disabled: selfProject === null,
      isLoading: setVisibility.isPending,
      // Toggling: the new 'hidden' value is the current visibility state.
      onSelect: () => void setVisibility.mutate(selfVisible),
    },
    {
      id: 'power',
      label: 'Power',
      icon: <Power className="size-4" aria-hidden="true" />,
      disabled: selfProject === null,
      children: [
        {
          id: 'restart',
          label: 'Restart dashboard',
          icon: <RefreshCw className="size-4" aria-hidden="true" />,
          onSelect: () => runSelfAction('restart'),
        },
        {
          id: 'stop',
          label: 'Stop dashboard',
          icon: <PowerOff className="size-4" aria-hidden="true" />,
          onSelect: () => runSelfAction('stop'),
        },
      ],
    },
  ];

  return (
    <ContextMenu label="Dashboard actions" items={items} className={className}>
      {children}

      {selfAction === 'restarting' && (
        <div className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-4 bg-background/80 backdrop-blur-sm">
          <Spinner size="xl" />
          <p className="text-sm text-muted-foreground">Restarting dashboard…</p>
        </div>
      )}

      {selfAction === 'stopped' && (
        <div className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-4 bg-background/80 backdrop-blur-sm">
          <p className="text-sm text-muted-foreground">
            Dashboard stopped. Start its container to continue.
          </p>
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            Reload
          </button>
        </div>
      )}
    </ContextMenu>
  );
};
