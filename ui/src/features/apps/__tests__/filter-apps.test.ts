import { describe, expect, it } from 'vitest';

import { filterApps } from '../filter-apps';
import type { App } from '../types';

const apps: App[] = [
  {
    name: 'Jellyfin',
    icon: null,
    state: 'running',
    url: 'http://localhost:8096',
    services: [
      {
        name: 'jellyfin',
        containerId: 'c1',
        image: 'jellyfin:latest',
        isRunning: true,
      },
    ],
  },
  {
    name: 'pi-hole',
    icon: null,
    state: 'stopped',
    url: null,
    services: [
      {
        name: 'pihole',
        containerId: 'c2',
        image: 'pihole:latest',
        isRunning: false,
      },
    ],
  },
];

describe('filterApps', () => {
  it('returns all apps for an empty query', () => {
    expect(filterApps(apps, '')).toEqual(apps);
    expect(filterApps(apps, '   ')).toEqual(apps);
  });

  it('matches case-insensitively', () => {
    expect(filterApps(apps, 'jellyfin')).toHaveLength(1);
    expect(filterApps(apps, 'JELLYFIN')).toHaveLength(1);
  });

  it('matches partial names', () => {
    expect(filterApps(apps, 'hole')).toHaveLength(1);
    expect(filterApps(apps, 'pi')).toHaveLength(1);
  });

  it('trims surrounding whitespace', () => {
    expect(filterApps(apps, '  pi-hole  ')).toHaveLength(1);
  });

  it('returns no apps when nothing matches', () => {
    expect(filterApps(apps, 'does-not-exist')).toHaveLength(0);
  });
});
