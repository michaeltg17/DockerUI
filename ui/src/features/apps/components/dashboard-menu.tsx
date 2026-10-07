import {
  EyeOff,
  Palette,
  Pencil,
  Plus,
  RefreshCw,
  ScrollText,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';

import { AppMenu, type AppMenuItem } from '@/components/ui/app-menu';
import { Spinner } from '@/components/ui/spinner';
import { useTheme } from '@/hooks/use-theme';
import { themes, type Theme } from '@/lib/theme';

import { restartSelf } from '../api/self-actions';
import { useSettings } from '../hooks/use-settings';

const themeLabels: Record<Theme, string> = {
  light: 'Light',
  dark: 'Dark',
  docker: 'Dark blue',
  'docker-v2': 'Docker',
};

const RESTART_TIMEOUT_MS = 120_000;
const RESTART_POLL_MS = 1_000;

type DashboardMenuProps = {
  onAddShortcut: () => void;
  onViewLogs: () => void;
  onRename: () => void;
  onViewHiddenApps: () => void;
  /** Extra classes for the wrapper that defines the menu's trigger zone. */
  className?: string;
  children: ReactNode;
};

export const DashboardMenu = ({
  onAddShortcut,
  onViewLogs,
  onRename,
  onViewHiddenApps,
  className,
  children,
}: DashboardMenuProps) => {
  const { theme, setTheme } = useTheme();
  const { data: settings } = useSettings();
  const [selfAction, setSelfAction] = useState<'restarting' | 'stopped' | null>(
    null,
  );

  const selfProject = settings?.self ?? null;

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

  const runRestart = () => {
    setSelfAction('restarting');
    // The dashboard goes down while this request is in flight, so the error is expected.
    void restartSelf().catch(() => undefined);
    void waitForDashboard();
  };

  const items: AppMenuItem[] = [
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
      id: 'rename',
      label: 'Rename',
      icon: <Pencil className="size-4" aria-hidden="true" />,
      onSelect: onRename,
    },
    {
      id: 'hidden-apps',
      label: 'View hidden apps',
      icon: <EyeOff className="size-4" aria-hidden="true" />,
      onSelect: onViewHiddenApps,
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
      id: 'restart',
      label: 'Restart',
      icon: <RefreshCw className="size-4" aria-hidden="true" />,
      disabled: selfProject === null,
      onSelect: runRestart,
    },
  ];

  return (
    <AppMenu label="Dashboard actions" items={items} className={className}>
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
    </AppMenu>
  );
};
