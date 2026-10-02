import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type MouseEvent,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';

import { cn } from '@/utils/cn';

import { Spinner } from '../spinner';

export type ContextMenuItem = {
  id: string;
  label: string;
  icon?: ReactNode;
  disabled?: boolean;
  isLoading?: boolean;
  onSelect?: () => void;
};

type ContextMenuProps = {
  children: ReactNode;
  items: ContextMenuItem[];
  label?: string;
};

const VIEWPORT_MARGIN = 8;

type MenuPosition = { x: number; y: number };

export const ContextMenu = ({
  children,
  items,
  label = 'Actions',
}: ContextMenuProps) => {
  const [position, setPosition] = useState<MenuPosition | null>(null);
  const [isReady, setIsReady] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const isOpen = position !== null;

  const close = useCallback(() => {
    setPosition(null);
    setIsReady(false);
  }, []);

  const handleContextMenu = (event: MouseEvent<HTMLDivElement>) => {
    event.preventDefault();
    setIsReady(false);
    setPosition({ x: event.clientX, y: event.clientY });
  };

  // Measure the (hidden) menu once it is rendered, then clamp it into the viewport.
  useLayoutEffect(() => {
    if (!isOpen || isReady || !position) return;

    const menu = menuRef.current;
    if (!menu) return;

    const { width, height } = menu.getBoundingClientRect();
    const x = Math.max(
      VIEWPORT_MARGIN,
      Math.min(position.x, window.innerWidth - width - VIEWPORT_MARGIN),
    );
    const y = Math.max(
      VIEWPORT_MARGIN,
      Math.min(position.y, window.innerHeight - height - VIEWPORT_MARGIN),
    );

    setPosition({ x, y });
    setIsReady(true);
  }, [isOpen, isReady, position]);

  useEffect(() => {
    if (!isOpen) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        close();
      }
    };

    const handleMouseDown = (event: globalThis.MouseEvent) => {
      if (menuRef.current?.contains(event.target as Node)) return;
      close();
    };

    document.addEventListener('mousedown', handleMouseDown);
    document.addEventListener('keydown', handleKeyDown);
    window.addEventListener('blur', close);
    window.addEventListener('resize', close);
    window.addEventListener('scroll', close, true);

    return () => {
      document.removeEventListener('mousedown', handleMouseDown);
      document.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('blur', close);
      window.removeEventListener('resize', close);
      window.removeEventListener('scroll', close, true);
    };
  }, [isOpen, close]);

  return (
    <div onContextMenu={handleContextMenu}>
      {children}
      {isOpen &&
        position &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            aria-label={label}
            style={{ left: position.x, top: position.y }}
            className={cn(
              'fixed z-50 min-w-40 overflow-hidden rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-lg',
              !isReady && 'invisible',
            )}
          >
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                role="menuitem"
                disabled={item.disabled || item.isLoading}
                onClick={() => {
                  close();
                  item.onSelect?.();
                }}
                className="flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50"
              >
                <span className="flex w-4 shrink-0 items-center justify-center">
                  {item.isLoading ? <Spinner size="sm" /> : item.icon}
                </span>
                <span className="truncate">{item.label}</span>
              </button>
            ))}
          </div>,
          document.body,
        )}
    </div>
  );
};
