import { fireEvent, render, screen } from '@testing-library/react';

import { ContextMenu, type ContextMenuItem } from '../context-menu';

const renderMenu = (items: ContextMenuItem[]) =>
  render(
    <ContextMenu label="Actions for demo" items={items}>
      <div>trigger</div>
    </ContextMenu>,
  );

const openMenu = () => {
  fireEvent.contextMenu(screen.getByText('trigger'), {
    clientX: 100,
    clientY: 100,
  });
};

test('renders no menu until the context menu is requested', () => {
  renderMenu([{ id: 'start', label: 'Start' }]);

  expect(screen.queryByRole('menu')).not.toBeInTheDocument();

  openMenu();

  expect(screen.getByRole('menu')).toBeInTheDocument();
  expect(screen.getByRole('menuitem', { name: 'Start' })).toBeInTheDocument();
});

test('selecting an item runs its callback and closes the menu', () => {
  const start = vi.fn();
  const stop = vi.fn();

  renderMenu([
    { id: 'start', label: 'Start', onSelect: start },
    { id: 'stop', label: 'Stop', onSelect: stop },
  ]);

  openMenu();
  fireEvent.click(screen.getByRole('menuitem', { name: 'Start' }));

  expect(start).toHaveBeenCalledTimes(1);
  expect(stop).not.toHaveBeenCalled();
  expect(screen.queryByRole('menu')).not.toBeInTheDocument();
});

test('disabled items do not run their callback', () => {
  const stop = vi.fn();

  renderMenu([{ id: 'stop', label: 'Stop', disabled: true, onSelect: stop }]);

  openMenu();
  fireEvent.click(screen.getByRole('menuitem', { name: 'Stop' }));

  expect(stop).not.toHaveBeenCalled();
});

test('escape closes the menu', () => {
  renderMenu([{ id: 'start', label: 'Start' }]);

  openMenu();
  expect(screen.getByRole('menu')).toBeInTheDocument();

  fireEvent.keyDown(document, { key: 'Escape' });

  expect(screen.queryByRole('menu')).not.toBeInTheDocument();
});
