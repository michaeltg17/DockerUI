import type { App } from '../types';

import { AppCard } from './app-card';

type AppsGridProps = {
  apps: App[];
};

export const AppsGrid = ({ apps }: AppsGridProps) => {
  return (
    <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
      {apps.map((app) => (
        <AppCard key={app.name} app={app} />
      ))}
    </div>
  );
};
