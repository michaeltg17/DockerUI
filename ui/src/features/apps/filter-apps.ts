import type { App } from './types';

export const filterApps = (apps: App[], query: string): App[] => {
  const q = query.trim().toLowerCase();

  if (!q) {
    return apps;
  }

  return apps.filter((app) => app.name.toLowerCase().includes(q));
};
