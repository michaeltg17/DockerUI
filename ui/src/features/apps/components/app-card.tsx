import {
  Pencil,
  Play,
  RefreshCw,
  ScrollText,
  Square,
  Trash2,
} from 'lucide-react';
import { useEffect, useState, type DragEvent, type MouseEvent } from 'react';

import { AppMenu, type AppMenuItem } from '@/components/ui/app-menu';
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
  draggable: boolean;
  isDragging: boolean;
  onDragStart: (event: DragEvent<HTMLButtonElement>) => void;
  onDragEnter: (event: DragEvent<HTMLButtonElement>) => void;
  onDragEnd: (event: DragEvent<HTMLButtonElement>) => void;
};

const cardClasses =
  'group flex size-full flex-col items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-accent hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring';

export const AppCard = ({
  app,
  draggable,
  isDragging,
  onDragStart,
  onDragEnter,
  onDragEnd,
}: AppCardProps) => {
  const cardDragProps = {
    draggable,
    onDragStart,
    onDragEnter,
    onDragEnd,
  };
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();
  const deleteShortcut = useDeleteShortcut();
  const [logsOpen, setLogsOpen] = useState(false);
  const [editing, setEditing] = useState(false);

  const isRunning = app.state === 'running';
  const isStarting = startApp.isPending;
  const isStopping = stopApp.isPending;
  const isRestarting = restartApp.isPending;
  const isBusy = isStarting || isStopping || isRestarting;

  // As soon as a stop (or restart) is initiated the app is already treated
  // as not running, so the icon darkens immediately — before the daemon
  // reports the new state.
  const isDimmed = !isRunning || isStopping || isRestarting;

  // A restart first stops the app and then starts it. Track whether the
  // stopped phase was observed so the progress bar can switch from red
  // (stopping) to green (starting).
  const [restartSawStopped, setRestartSawStopped] = useState(false);

  useEffect(() => {
    if (!isRestarting) {
      setRestartSawStopped(false);
      return;
    }
    if (app.state === 'stopped') setRestartSawStopped(true);
  }, [isRestarting, app.state]);

  const progressColor = isRestarting
    ? restartSawStopped
      ? 'bg-green-500'
      : 'bg-red-500'
    : isStarting
      ? 'bg-green-500'
      : 'bg-red-500';

  // A left click fires 'click'; a middle (wheel) click fires 'auxclick' instead, so the
  // card listens to both and opens the app in a new tab either way.
  const openUrl = (event: MouseEvent) => {
    if (event.button !== 0 && event.button !== 1) return;
    if (app.url) {
      window.open(app.url, '_blank', 'noopener,noreferrer');
    }
  };

  if (app.isShortcut) {
    const shortcutActions: AppMenuItem[] = [
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

    return (
      <>
        <div className="relative size-full">
          <AppMenu label={`Actions for ${app.name}`} items={shortcutActions}>
            <button
              type="button"
              onClick={openUrl}
              onAuxClick={openUrl}
              data-state={app.state}
              {...cardDragProps}
              className={cn(cardClasses, isDragging && 'opacity-50')}
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
          </AppMenu>
          {isDragging && <DropIndicator />}
        </div>
        <ShortcutDialog
          open={editing}
          initial={{ name: app.name, icon: app.icon, url: app.url ?? '' }}
          onClose={() => setEditing(false)}
        />
      </>
    );
  }

  const actions: AppMenuItem[] = [
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

  return (
    <>
      <div className="relative size-full">
        <AppMenu label={`Actions for ${app.name}`} items={actions}>
          <button
            type="button"
            onClick={openUrl}
            onAuxClick={openUrl}
            data-state={app.state}
            {...cardDragProps}
            className={cn(cardClasses, isDragging && 'opacity-50')}
          >
            <div className="relative">
              <AppIcon
                icon={app.icon}
                name={app.name}
                className={cn(
                  'size-24 text-3xl transition-[filter] duration-200',
                  isDimmed && 'brightness-50',
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
                  className="absolute inset-0 flex cursor-pointer items-center justify-center"
                >
                  <span className="flex size-10 items-center justify-center rounded-full bg-slate-900/40 transition-opacity duration-150 group-hover:opacity-0">
                    <Square
                      className="size-4 fill-white text-white"
                      aria-hidden="true"
                    />
                  </span>
                  <span className="absolute left-1/2 top-1/2 flex size-10 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-slate-900/40 opacity-0 transition-opacity duration-150 group-hover:opacity-100">
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
                  <span
                    className={cn(
                      'block h-full w-1/3 animate-progress-slide rounded-full',
                      progressColor,
                    )}
                  />
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
        </AppMenu>
        {isDragging && <DropIndicator />}
      </div>
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

/** The vertical bar marking where the dragged card will be placed. */
const DropIndicator = () => (
  <span
    data-drop-indicator
    aria-hidden="true"
    className="pointer-events-none absolute inset-y-3 -left-1.5 w-1 rounded-full bg-primary"
  />
);
