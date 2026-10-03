import { Search, Ship } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Helmet } from 'react-helmet-async';

import { Spinner } from '@/components/ui/spinner';

import { AppsGrid } from './components/apps-grid';
import { filterApps } from './filter-apps';
import { useApps } from './hooks/use-apps';
import { useAppsHub } from './hooks/use-apps-hub';

export const AppsPage = () => {
  const { data, isPending, isError, refetch } = useApps();
  const [query, setQuery] = useState('');

  useAppsHub();

  const apps = useMemo(() => filterApps(data ?? [], query), [data, query]);
  const hasQuery = query.trim().length > 0;

  return (
    <div className="min-h-screen bg-slate-50">
      <Helmet>
        <title>Docker UI</title>
        <meta name="description" content="Your Docker stacks at a glance" />
      </Helmet>

      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-6 py-4">
          <Ship className="size-7 text-sky-600" aria-hidden="true" />
          <div className="mr-auto">
            <h1 className="text-lg font-semibold leading-tight">Docker UI</h1>
            <p className="text-xs text-slate-500">
              Your Docker stacks at a glance
            </p>
          </div>
          <label className="relative block">
            <span className="sr-only">Search apps</span>
            <Search
              className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400"
              aria-hidden="true"
            />
            <input
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search apps…"
              className="w-56 rounded-md border border-slate-300 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 shadow-sm transition-colors placeholder:text-slate-400 focus:border-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/30 sm:w-64"
            />
          </label>
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
        ) : apps.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-24 text-center text-slate-500">
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
    </div>
  );
};
