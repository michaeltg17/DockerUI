import { Check, ChevronRight } from 'lucide-react';
import {
  Fragment,
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
  /** Marks the item as the active choice; a check is shown in the icon slot. */
  selected?: boolean;
  onSelect?: () => void;
  /** When present, the item opens a submenu with these entries instead of acting. */
  children?: ContextMenuItem[];
};

type ContextMenuProps = {
  children: ReactNode;
  items: ContextMenuItem[];
  label?: string;
  /** Extra classes for the wrapper that defines the menu's trigger zone. */
  className?: string;
};

const VIEWPORT_MARGIN = 8;
const SUBMENU_OPEN_DELAY_MS = 120;
const SUBMENU_CLOSE_DELAY_MS = 200;
const SUBMENU_GAP = 4;

type MenuPosition = { x: number; y: number };

type SubmenuAnchor = { top: number; left: number; right: number };

const toAnchor = (rect: DOMRect): SubmenuAnchor => ({
  top: rect.top,
  left: rect.left,
  right: rect.right,
});

export const ContextMenu = ({
  children,
  items,
  label = 'Actions',
  className,
}: ContextMenuProps) => {
  const [position, setPosition] = useState<MenuPosition | null>(null);
  const [isReady, setIsReady] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const [submenuId, setSubmenuId] = useState<string | null>(null);
  const [submenuAnchor, setSubmenuAnchor] = useState<SubmenuAnchor | null>(
    null,
  );
  const [submenuPos, setSubmenuPos] = useState<MenuPosition | null>(null);
  const [submenuReady, setSubmenuReady] = useState(false);
  const submenuRef = useRef<HTMLDivElement>(null);
  const openTimer = useRef<number | null>(null);
  const closeTimer = useRef<number | null>(null);

  const isOpen = position !== null;

  const clearTimers = useCallback(() => {
    if (openTimer.current !== null) window.clearTimeout(openTimer.current);
    if (closeTimer.current !== null) window.clearTimeout(closeTimer.current);
    openTimer.current = null;
    closeTimer.current = null;
  }, []);

  const closeSubmenu = useCallback(() => {
    clearTimers();
    setSubmenuId(null);
    setSubmenuAnchor(null);
    setSubmenuPos(null);
    setSubmenuReady(false);
  }, [clearTimers]);

  const close = useCallback(() => {
    closeSubmenu();
    setPosition(null);
    setIsReady(false);
  }, [closeSubmenu]);

  const openSubmenu = useCallback(
    (id: string, anchor: SubmenuAnchor) => {
      clearTimers();
      setSubmenuPos(null);
      setSubmenuReady(false);
      setSubmenuAnchor(anchor);
      setSubmenuId(id);
    },
    [clearTimers],
  );

  const scheduleSubmenuClose = useCallback(() => {
    clearTimers();
    closeTimer.current = window.setTimeout(
      closeSubmenu,
      SUBMENU_CLOSE_DELAY_MS,
    );
  }, [clearTimers, closeSubmenu]);

  const scheduleSubmenuOpen = useCallback(
    (item: ContextMenuItem, anchor: HTMLElement) => {
      clearTimers();
      openTimer.current = window.setTimeout(
        () => openSubmenu(item.id, toAnchor(anchor.getBoundingClientRect())),
        SUBMENU_OPEN_DELAY_MS,
      );
    },
    [clearTimers, openSubmenu],
  );

  const handleContextMenu = (event: MouseEvent<HTMLDivElement>) => {
    // Nested menus (e.g. a card menu inside the dashboard menu) must not both open.
    event.preventDefault();
    event.stopPropagation();
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

  // Position the submenu beside its parent item, flipping left when there is not
  // enough room on the right.
  useLayoutEffect(() => {
    if (!isOpen || !submenuId || !submenuAnchor || submenuReady) return;

    const submenu = submenuRef.current;
    if (!submenu) return;

    const { width, height } = submenu.getBoundingClientRect();
    const fitsOnRight =
      submenuAnchor.right + SUBMENU_GAP + width + VIEWPORT_MARGIN <=
      window.innerWidth;
    const x = fitsOnRight
      ? submenuAnchor.right + SUBMENU_GAP
      : submenuAnchor.left - width - SUBMENU_GAP;
    const y = Math.max(
      VIEWPORT_MARGIN,
      Math.min(
        submenuAnchor.top,
        window.innerHeight - height - VIEWPORT_MARGIN,
      ),
    );

    setSubmenuPos({ x, y });
    setSubmenuReady(true);
  }, [isOpen, submenuId, submenuAnchor, submenuReady]);

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
      clearTimers();
      document.removeEventListener('mousedown', handleMouseDown);
      document.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('blur', close);
      window.removeEventListener('resize', close);
      window.removeEventListener('scroll', close, true);
    };
  }, [isOpen, close, clearTimers]);

  const renderMenuItem = (item: ContextMenuItem) => {
    const children = item.children;
    const hasChildren = children !== undefined;

    return (
      <Fragment key={item.id}>
        <button
          type="button"
          role="menuitem"
          aria-haspopup={hasChildren ? 'menu' : undefined}
          disabled={item.disabled || item.isLoading}
          onClick={() => {
            if (hasChildren) return;
            close();
            item.onSelect?.();
          }}
          onMouseEnter={
            hasChildren
              ? (event) => scheduleSubmenuOpen(item, event.currentTarget)
              : undefined
          }
          onMouseLeave={hasChildren ? scheduleSubmenuClose : undefined}
          onFocus={
            hasChildren
              ? (event) =>
                  openSubmenu(
                    item.id,
                    toAnchor(event.currentTarget.getBoundingClientRect()),
                  )
              : undefined
          }
          onBlur={
            hasChildren
              ? (event) => {
                  // Keep the submenu open while focus moves into it.
                  const next = event.relatedTarget as Node | null;
                  if (next !== null && submenuRef.current?.contains(next))
                    return;
                  closeSubmenu();
                }
              : undefined
          }
          className="flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left text-sm transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50"
        >
          <span className="flex w-4 shrink-0 items-center justify-center">
            {item.isLoading ? (
              <Spinner size="sm" />
            ) : item.selected ? (
              <Check className="size-4" aria-hidden="true" />
            ) : (
              item.icon
            )}
          </span>
          <span className="truncate">{item.label}</span>
          {hasChildren && (
            <ChevronRight
              className="ml-auto size-4 shrink-0 text-muted-foreground"
              aria-hidden="true"
            />
          )}
        </button>
        {children && submenuId === item.id && submenuAnchor && (
          <div
            ref={submenuRef}
            role="menu"
            aria-label={item.label}
            tabIndex={-1}
            style={
              submenuPos ? { left: submenuPos.x, top: submenuPos.y } : undefined
            }
            className={cn(
              'fixed z-50 min-w-40 overflow-hidden rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-lg',
              !submenuReady && 'invisible',
            )}
            onMouseEnter={clearTimers}
            onMouseLeave={scheduleSubmenuClose}
          >
            {children.map(renderMenuItem)}
          </div>
        )}
      </Fragment>
    );
  };

  return (
    <div className={cn(className)} onContextMenu={handleContextMenu}>
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
            {items.map(renderMenuItem)}
          </div>,
          document.body,
        )}
    </div>
  );
};
