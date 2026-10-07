import { X } from 'lucide-react';
import {
  useEffect,
  useId,
  useRef,
  type ReactNode,
  type RefObject,
} from 'react';
import { createPortal } from 'react-dom';

import { cn } from '@/utils/cn';

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Every open dialog registers here in the order it opens. Only the last
 * entry (the topmost dialog) owns the keyboard while it is open, so a
 * picker layered on a form closes first on Escape and traps Tab on its own.
 */
const dialogStack: Array<() => void> = [];

const isTopmost = (entry: () => void) => dialogStack.at(-1) === entry;

export type DialogProps = {
  /** The header title; it is also the dialog's accessible name. */
  title: string;
  onClose: () => void;
  /**
   * Set while the dialog holds unsaved changes: clicking the backdrop then
   * keeps the dialog open so the edits cannot be lost by accident.
   */
  dirty?: boolean;
  /** Renders above plain dialogs, e.g. a picker opened from a form. */
  layered?: boolean;
  /** Extra classes for the panel, e.g. its width or scroll layout. */
  className?: string;
  /** Focus this element when the dialog opens. */
  initialFocusRef?: RefObject<HTMLElement | null>;
  children: ReactNode;
};

export const Dialog = ({
  title,
  onClose,
  dirty = false,
  layered = false,
  className,
  initialFocusRef,
  children,
}: DialogProps) => {
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    const entry = () => onCloseRef.current();
    dialogStack.push(entry);

    const previouslyFocused =
      document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
    initialFocusRef?.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (!isTopmost(entry)) return;

      if (event.key === 'Escape') {
        entry();
        return;
      }

      if (event.key !== 'Tab') return;
      const panel = panelRef.current;
      if (!panel) return;

      const focusable = [
        ...panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ];
      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;
      const inside = active instanceof HTMLElement && panel.contains(active);

      if (event.shiftKey) {
        if (!inside || active === first) {
          event.preventDefault();
          last.focus();
        }
      } else if (!inside || active === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown);

    return () => {
      const index = dialogStack.indexOf(entry);
      if (index !== -1) dialogStack.splice(index, 1);
      document.removeEventListener('keydown', handleKeyDown);
      if (previouslyFocused?.isConnected) previouslyFocused.focus();
    };
  }, [initialFocusRef]);

  return createPortal(
    <div
      className={cn(
        'fixed inset-0 flex items-center justify-center bg-black/40 p-6',
        layered ? 'z-[60]' : 'z-50',
      )}
      role="presentation"
      onPointerDown={(event) => {
        if (event.target === event.currentTarget && !dirty) onClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={cn(
          'w-full max-w-md overflow-hidden rounded-lg border border-border bg-background shadow-lg',
          className,
        )}
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 id={titleId} className="text-sm font-semibold">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>
        {children}
      </div>
    </div>,
    document.body,
  );
};
