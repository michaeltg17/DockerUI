import { useEffect, useState, type DragEvent } from 'react';

import type { App } from '../types';

import { AppCard } from './app-card';

type AppsGridProps = {
  apps: App[];
  /** The names of the docker apps still showing their 'new' tag. */
  newAppNames: ReadonlySet<string>;
  /** Reports the first interaction with an app, clearing its 'new' tag. */
  onInteractApp: (name: string) => void;
  /** Persists a new full order of the visible app names. When absent, dragging is disabled. */
  onReorder?: (names: string[]) => void;
};

export const AppsGrid = ({
  apps,
  newAppNames,
  onInteractApp,
  onReorder,
}: AppsGridProps) => {
  const [dragged, setDragged] = useState<string | null>(null);
  const [dragOrder, setDragOrder] = useState<App[] | null>(null);

  // A fresh app list (the server confirming a reorder, or a state push) replaces
  // the local drag order.
  useEffect(() => {
    setDragOrder(null);
  }, [apps]);

  const list = dragOrder ?? apps;
  const reorderEnabled = onReorder !== undefined;

  const handleDragStart = (
    event: DragEvent<HTMLButtonElement>,
    name: string,
  ) => {
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', name);
    setDragged(name);
  };

  const handleDragEnter = (name: string) => {
    if (dragged === null || dragged === name) {
      return;
    }

    setDragOrder((previous) => {
      const base = [...(previous ?? apps)];
      const fromIndex = base.findIndex((app) => app.name === dragged);
      const toIndex = base.findIndex((app) => app.name === name);

      if (fromIndex === -1 || toIndex === -1) {
        return previous;
      }

      const [moved] = base.splice(fromIndex, 1);
      base.splice(toIndex, 0, moved);
      return base;
    });
  };

  const handleDragEnd = () => {
    const from = dragged;
    setDragged(null);

    if (from === null || onReorder === undefined) {
      return;
    }

    const names = (dragOrder ?? apps).map((app) => app.name);

    if (names.some((name, index) => name !== apps[index]?.name)) {
      onReorder(names);
    }
  };

  return (
    <div
      className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6"
      onDragOver={(event) => {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
      }}
      onDrop={(event) => event.preventDefault()}
    >
      {list.map((app) => (
        <AppCard
          key={app.name}
          app={app}
          isNew={newAppNames.has(app.name)}
          onInteract={() => onInteractApp(app.name)}
          draggable={reorderEnabled}
          isDragging={dragged === app.name}
          dragActive={dragged !== null}
          onDragStart={(event) => handleDragStart(event, app.name)}
          onDragEnter={() => handleDragEnter(app.name)}
          onDragEnd={handleDragEnd}
        />
      ))}
    </div>
  );
};
