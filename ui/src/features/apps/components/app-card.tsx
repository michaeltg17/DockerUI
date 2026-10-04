import {
  Pencil,
  Play,
  RefreshCw,
  ScrollText,
  Square,
  Trash2,
} from 'lucide-react';
import { useState } from 'react';

import {
  ContextMenu,
  type ContextMenuItem,
} from '@/components/ui/context-menu';
import { cn } from '@/utils/cn';

import { getAppLogs } from '../api/get-logs';
import { useRestartApp, useStartApp, useStopApp } from '../hooks/use-apps';
import { useDeleteShortcut } from '../hooks/use-shortcuts';
import type { App } from '../types';

import { AppIcon } from './app-icon';
import { LogsDialog } from './logs-dialog';
import { ShortcutDialog } from './shortcut-dialog';

type AppCardProps = {
  app: App;
};

export const AppCard = ({ app }: AppCardProps) => {
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();
  const deleteShortcut = useDeleteShortcut();
  const [logsOpen, setLogsOpen] = useState(false);
  const [editing, setEditing] = useState(false);

  const isRunning = app.state === 'running';
  const isBusy =
    startApp.isPending || stopApp.isPending || restartApp.isPending;

  if (app.isShortcut) {
    const shortcutActions: ContextMenuItem[] = [
      {
        id: 'edit',
        label: 'Edit',
        icon: <Pencil className="size-4" aria-hidden="true" />,
        onSelect: () => setEditing(true),
      },
      {
        id: 'delete',
        label: 'Delete',
        icon: <Trash2 className="size-4" aria-hidden="true" />,
        isLoading: deleteShortcut.isPending,
        onSelect: () => void deleteShortcut.mutate(app.name),
      },
    ];

    const openShortcut = () => {
      if (app.url) {
        window.open(app.url, '_blank', 'noopener,noreferrer');
      }
    };

    return (
      <>
        <ContextMenu label={`Actions for ${app.name}`} items={shortcutActions}>
          <button
            type="button"
            onClick={openShortcut}
            data-state={app.state}
            className="group flex size-full flex-col items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-accent hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <AppIcon
              icon={app.icon}
              name={app.name}
              className="size-24 text-3xl"
            />
            <h3
              className="max-w-full truncate text-sm font-medium"
              title={app.name}
            >
              {app.name}
            </h3>
          </button>
        </ContextMenu>
        <ShortcutDialog
          open={editing}
          initial={{ name: app.name, icon: app.icon, url: app.url ?? '' }}
          onClose={() => setEditing(false)}
        />
      </>
    );
  }

  const actions: ContextMenuItem[] = [
    {
      id: 'start',
      label: 'Start',
      icon: <Play className="size-4" aria-hidden="true" />,
      disabled: isBusy || isRunning,
      isLoading: startApp.isPending,
      onSelect: () => void startApp.mutate(app.name),
    },
    {
      id: 'stop',
      label: 'Stop',
      icon: <Square className="size-4" aria-hidden="true" />,
      disabled: isBusy || !isRunning,
      isLoading: stopApp.isPending,
      onSelect: () => void stopApp.mutate(app.name),
    },
    {
      id: 'restart',
      label: 'Restart',
      icon: (
        <RefreshCw
          className={cn('size-4', isBusy && 'animate-spin')}
          aria-hidden="true"
        />
      ),
      disabled: isBusy,
      isLoading: restartApp.isPending,
      onSelect: () => void restartApp.mutate(app.name),
    },
    {
      id: 'logs',
      label: 'View logs',
      icon: <ScrollText className="size-4" aria-hidden="true" />,
      onSelect: () => setLogsOpen(true),
    },
  ];

  const openApp = () => {
    if (app.url) {
      window.open(app.url, '_blank', 'noopener,noreferrer');
    }
  };

  return (
    <>
      <ContextMenu label={`Actions for ${app.name}`} items={actions}>
        <button
          type="button"
          onClick={openApp}
          data-state={app.state}
          className="group flex size-full flex-col items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-accent hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <div className="relative">
            <AppIcon
              icon={app.icon}
              name={app.name}
              className={cn(
                'size-24 text-3xl transition-opacity',
                app.state === 'stopped' && 'opacity-50 grayscale',
              )}
            />
            {app.state === 'stopped' && (
              <span
                role="button"
                tabIndex={0}
                aria-label={`Start ${app.name}`}
                onClick={(event) => {
                  event.stopPropagation();
                  void startApp.mutate(app.name);
                }}
                onKeyDown={(event) => {
                  if (event.key !== 'Enter' && event.key !== ' ') return;
                  event.preventDefault();
                  event.stopPropagation();
                  void startApp.mutate(app.name);
                }}
                className="absolute inset-0 flex cursor-pointer items-center justify-center opacity-0 transition-opacity group-hover:opacity-100"
              >
                <span className="flex size-10 items-center justify-center rounded-full bg-slate-900/40">
                  <Play
                    className="size-4 fill-white text-white"
                    aria-hidden="true"
                  />
                </span>
              </span>
            )}
            {isBusy && (
              <span
                role="progressbar"
                aria-label={`Updating ${app.name}`}
                className="absolute inset-x-3 bottom-2 h-1 overflow-hidden rounded-full bg-slate-900/30"
              >
                <span className="block h-full w-1/3 animate-progress-slide rounded-full bg-sky-500" />
              </span>
            )}
          </div>

          <h3
            className="max-w-full truncate text-sm font-medium"
            title={app.name}
          >
            {app.name}
          </h3>
        </button>
      </ContextMenu>
      <LogsDialog
        open={logsOpen}
        title={`${app.name} logs`}
        scope={app.name}
        fetchLogs={() => getAppLogs(app.name)}
        onClose={() => setLogsOpen(false)}
      />
    </>
  );
};
