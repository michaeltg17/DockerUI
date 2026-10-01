import { Ship } from 'lucide-react';
import { Helmet } from 'react-helmet-async';

import { Spinner } from '@/components/ui/spinner';

import { AppsGrid } from './components/apps-grid';
import { useApps } from './hooks/use-apps';
import { useAppsHub } from './hooks/use-apps-hub';

export const AppsPage = () => {
  const { data, isPending, isError, refetch } = useApps();

  useAppsHub();

  return (
    <div className="min-h-screen bg-slate-50">
      <Helmet>
        <title>Docker UI</title>
        <meta name="description" content="Your Docker stacks at a glance" />
      </Helmet>

      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl items-center gap-3 px-6 py-4">
          <Ship className="size-7 text-sky-600" aria-hidden="true" />
          <div>
            <h1 className="text-lg font-semibold leading-tight">Docker UI</h1>
            <p className="text-xs text-slate-500">
              Your Docker stacks at a glance
            </p>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-6 py-8">
        {isPending ? (
          <div className="flex justify-center py-24">
            <Spinner size="xl" />
          </div>
        ) : isError ? (
          <div className="flex flex-col items-center gap-4 py-24 text-center">
            <Ship className="size-10 text-slate-400" aria-hidden="true" />
            <p className="text-sm text-slate-600">
              Could not load apps. Is the Docker daemon reachable?
            </p>
            <button
              type="button"
              onClick={() => void refetch()}
              className="rounded-md bg-sky-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-sky-700"
            >
              Retry
            </button>
          </div>
        ) : data && data.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-24 text-center text-slate-500">
            <Ship className="size-10" aria-hidden="true" />
            <p className="text-sm">
              No apps found. Start a Docker Compose stack to see it here.
            </p>
          </div>
        ) : (
          data && <AppsGrid apps={data} />
        )}
      </main>
    </div>
  );
};
