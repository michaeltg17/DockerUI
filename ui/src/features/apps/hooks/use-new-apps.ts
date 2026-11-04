import { useCallback, useEffect, useRef, useState } from 'react';

import type { App } from '../types';

const KNOWN_APPS_KEY = 'DockerUI:known-apps';
const NEW_APPS_KEY = 'DockerUI:new-apps';

/**
 * Reads a stored string set; null when nothing is stored yet (the first visit)
 * or the value is unreadable.
 */
const readSet = (key: string): Set<string> | null => {
  try {
    const raw = localStorage.getItem(key);
    if (raw === null) return null;

    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed)
      ? new Set(
          parsed.filter((value): value is string => typeof value === 'string'),
        )
      : null;
  } catch {
    return null;
  }
};

const writeSet = (key: string, set: Set<string>): void => {
  try {
    localStorage.setItem(key, JSON.stringify([...set]));
  } catch {
    // Storage may be unavailable; the tag then just does not persist between visits.
  }
};

/**
 * Tracks which docker apps are still 'new': detected after this browser's
 * first visit, not yet interacted with. The first visit baselines the apps it
 * sees (an existing stack is not 'new'), and every docker app that appears
 * later is tagged until the user's first interaction with its card, which
 * remembers it. Both sets live in localStorage, so the tag persists across
 * reloads and visits.
 */
export const useNewApps = (apps: App[]) => {
  const [newAppNames, setNewAppNames] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const lastSeenNamesRef = useRef<string | null>(null);

  useEffect(() => {
    const dockerNames = apps
      .filter((app) => app.source === 'docker')
      .map((app) => app.name);
    const signature = dockerNames.join('\u0000');

    // The app list is refetched and pushed on every state change; the tagging
    // only reacts to the set of docker apps actually changing.
    if (signature === lastSeenNamesRef.current) return;
    lastSeenNamesRef.current = signature;

    if (dockerNames.length === 0) return;

    const known = readSet(KNOWN_APPS_KEY);
    const current = new Set(dockerNames);

    if (known === null) {
      // The first visit ever: the apps it sees are the baseline, none is 'new'.
      writeSet(KNOWN_APPS_KEY, current);
      setNewAppNames(new Set());
      return;
    }

    const tagged = readSet(NEW_APPS_KEY) ?? new Set<string>();
    const next = new Set<string>();

    for (const name of current) {
      // Still tagged and still present, or detected for the first time now.
      if (tagged.has(name) || !known.has(name)) next.add(name);
    }

    writeSet(KNOWN_APPS_KEY, new Set([...known, ...current]));
    writeSet(NEW_APPS_KEY, next);
    setNewAppNames(next);
  }, [apps]);

  /** The first interaction with a card clears its 'new' tag and remembers the app. */
  const markInteracted = useCallback((name: string) => {
    setNewAppNames((current) => {
      if (!current.has(name)) return current;
      const next = new Set(current);
      next.delete(name);
      writeSet(NEW_APPS_KEY, next);
      return next;
    });
  }, []);

  return { newAppNames, markInteracted };
};
