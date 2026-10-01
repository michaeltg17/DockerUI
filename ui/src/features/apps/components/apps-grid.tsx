import type { App } from '../types';

import { AppCard } from './app-card';

type AppsGridProps = {
  apps: App[];
};

export const AppsGrid = ({ apps }: AppsGridProps) => {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {apps.map((app) => (
        <AppCard key={app.name} app={app} />
      ))}
    </div>
  );
};
