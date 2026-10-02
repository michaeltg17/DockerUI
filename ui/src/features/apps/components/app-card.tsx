import { Play, RefreshCw, Square } from 'lucide-react';

import {
  ContextMenu,
  type ContextMenuItem,
} from '@/components/ui/context-menu';
import { cn } from '@/utils/cn';

import { useRestartApp, useStartApp, useStopApp } from '../hooks/use-apps';
import type { App } from '../types';

import { AppIcon } from './app-icon';

type AppCardProps = {
  app: App;
};

export const AppCard = ({ app }: AppCardProps) => {
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();

  const isRunning = app.state === 'running';
  const isBusy =
    startApp.isPending || stopApp.isPending || restartApp.isPending;

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
  ];

  const openApp = () => {
    if (app.url) {
      window.open(app.url, '_blank', 'noopener,noreferrer');
    }
  };

  return (
    <ContextMenu label={`Actions for ${app.name}`} items={actions}>
      <button
        type="button"
        onClick={openApp}
        className="group flex size-full cursor-pointer flex-col items-center gap-3 rounded-2xl p-4 transition-colors hover:bg-white hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <div className="rounded-[22px] border-2 border-transparent p-1 transition-colors duration-200 group-hover:border-slate-300">
          <div className="relative transition-transform duration-200 ease-out group-hover:scale-105">
            <AppIcon
              icon={app.icon}
              name={app.name}
              className="size-24 text-3xl"
            />
            {app.state === 'stopped' && (
              <span className="pointer-events-none absolute inset-0 flex items-center justify-center">
                <span className="flex size-10 items-center justify-center rounded-full bg-slate-900/40">
                  <Square
                    className="size-4 fill-white text-white"
                    aria-hidden="true"
                  />
                </span>
              </span>
            )}
          </div>
        </div>

        <h3
          className="max-w-full truncate text-sm font-medium"
          title={app.name}
        >
          {app.name}
        </h3>
      </button>
    </ContextMenu>
  );
};
