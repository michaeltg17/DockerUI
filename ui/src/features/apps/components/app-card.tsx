import { RefreshCw, Play, Square } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { cn } from '@/utils/cn';

import { useRestartApp, useStartApp, useStopApp } from '../hooks/use-apps';
import type { App, AppState } from '../types';

import { AppIcon } from './app-icon';

const stateDotStyles: Record<AppState, string> = {
  running: 'bg-emerald-500',
  partial: 'bg-amber-500',
  stopped: 'bg-slate-400',
};

const stateLabels: Record<AppState, string> = {
  running: 'Running',
  partial: 'Partially running',
  stopped: 'Stopped',
};

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

  return (
    <div className="group flex flex-col items-center gap-3 rounded-2xl p-4 transition-colors focus-within:bg-white focus-within:shadow-sm hover:bg-white hover:shadow-sm">
      <div className="relative">
        <AppIcon icon={app.icon} name={app.name} className="size-24 text-3xl" />
        <span
          className={cn(
            'absolute -bottom-1 -right-1 size-4 rounded-full border-2 border-white',
            stateDotStyles[app.state],
          )}
          title={stateLabels[app.state]}
        />
      </div>

      <h3 className="max-w-full truncate text-sm font-medium" title={app.name}>
        {app.name}
      </h3>

      <div className="flex w-full gap-1.5 opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100">
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy || !isRunning}
          isLoading={stopApp.isPending}
          onClick={() => void stopApp.mutate(app.name)}
          className="flex-1"
          title="Stop"
          aria-label={`Stop ${app.name}`}
        >
          <Square className="size-3.5" aria-hidden="true" />
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy || isRunning}
          isLoading={startApp.isPending}
          onClick={() => void startApp.mutate(app.name)}
          className="flex-1"
          title="Start"
          aria-label={`Start ${app.name}`}
        >
          <Play className="size-3.5" aria-hidden="true" />
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy}
          isLoading={restartApp.isPending}
          onClick={() => void restartApp.mutate(app.name)}
          className="flex-1"
          title="Restart"
          aria-label={`Restart ${app.name}`}
        >
          <RefreshCw
            className={cn('size-3.5', isBusy && 'animate-spin')}
            aria-hidden="true"
          />
        </Button>
      </div>
    </div>
  );
};
