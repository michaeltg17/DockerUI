import { RefreshCw, Play, Square } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { cn } from '@/utils/cn';

import { useRestartApp, useStartApp, useStopApp } from '../hooks/use-apps';
import type { App, AppState } from '../types';

import { AppIcon } from './app-icon';

const stateStyles: Record<AppState, string> = {
  running: 'bg-emerald-100 text-emerald-700',
  partial: 'bg-amber-100 text-amber-700',
  stopped: 'bg-slate-200 text-slate-600',
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
    <div className="flex flex-col gap-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-2">
        <AppIcon icon={app.icon} name={app.name} />
        <span
          className={cn(
            'rounded-full px-2 py-0.5 text-xs font-medium capitalize',
            stateStyles[app.state],
          )}
        >
          {app.state}
        </span>
      </div>

      <div className="min-w-0">
        <h3 className="truncate text-base font-semibold" title={app.name}>
          {app.name}
        </h3>
        <p className="truncate text-xs text-slate-500">
          {app.services.length} service
          {app.services.length === 1 ? '' : 's'}
        </p>
      </div>

      <div className="mt-auto flex gap-2">
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy || !isRunning}
          isLoading={stopApp.isPending}
          onClick={() => void stopApp.mutate(app.name)}
          className="flex-1"
        >
          <Square className="mr-1 size-3.5" aria-hidden="true" />
          Stop
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy || isRunning}
          isLoading={startApp.isPending}
          onClick={() => void startApp.mutate(app.name)}
          className="flex-1"
        >
          <Play className="mr-1 size-3.5" aria-hidden="true" />
          Start
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={isBusy}
          isLoading={restartApp.isPending}
          onClick={() => void restartApp.mutate(app.name)}
          className="flex-1"
        >
          <RefreshCw
            className={cn('mr-1 h-3.5 w-3.5', isBusy && 'animate-spin')}
            aria-hidden="true"
          />
          Restart
        </Button>
      </div>
    </div>
  );
};
