import { useEffect, useRef } from 'react';

import { Dialog } from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';

import type { Logs } from '../api/get-logs';
import { useLogs } from '../hooks/use-logs';

type LogsDialogProps = {
  open: boolean;
  title: string;
  scope: string;
  fetchLogs: () => Promise<Logs>;
  onClose: () => void;
};

function extractErrorMessage(error: unknown): string {
  const data = (
    error as { response?: { data?: { detail?: unknown; message?: unknown } } }
  )?.response?.data;
  const detail = data?.detail ?? data?.message;
  return typeof detail === 'string' && detail.length > 0
    ? detail
    : 'Could not load logs.';
}

export const LogsDialog = ({
  open,
  title,
  scope,
  fetchLogs,
  onClose,
}: LogsDialogProps) => {
  const { data, isPending, isError, error, refetch } = useLogs(
    open,
    scope,
    fetchLogs,
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLPreElement>(null);
  // While true the view stays pinned to the newest lines; the user scrolling
  // up releases the pin so their position is respected.
  const stickToBottomRef = useRef(true);

  // Re-arm the pin every time the dialog opens, so a freshly mounted scroll
  // area starts at the end even when the logs are served from the cache.
  useEffect(() => {
    if (open) stickToBottomRef.current = true;
  }, [open]);

  // Pin to the most recent lines as the content is laid out and grows. A
  // requestAnimationFrame covers the initial layout and a ResizeObserver
  // covers late reflows (font swap) and appended log lines.
  useEffect(() => {
    if (!open) return;

    const scroller = scrollRef.current;
    if (!scroller) return;

    const stick = () => {
      if (stickToBottomRef.current) scroller.scrollTop = scroller.scrollHeight;
    };
    stick();
    const frame = requestAnimationFrame(stick);

    const content = contentRef.current;
    if (!content) return () => cancelAnimationFrame(frame);

    const observer = new ResizeObserver(stick);
    observer.observe(content);
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
    };
  }, [open, data]);

  const releasePinIfScrolledUp = () => {
    const scroller = scrollRef.current;
    if (!scroller) return;
    stickToBottomRef.current =
      scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 4;
  };

  if (!open) return null;

  return (
    <Dialog
      title={title}
      onClose={onClose}
      className="flex max-h-[80vh] max-w-3xl flex-col"
    >
      <div
        ref={scrollRef}
        onScroll={releasePinIfScrolledUp}
        className="overflow-auto p-4"
      >
        {isPending ? (
          <div className="flex justify-center py-16">
            <Spinner size="xl" />
          </div>
        ) : isError ? (
          <div className="flex flex-col items-center gap-4 py-12 text-center">
            <p className="text-sm text-muted-foreground">
              {extractErrorMessage(error)}
            </p>
            <button
              type="button"
              onClick={() => void refetch()}
              className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
            >
              Retry
            </button>
          </div>
        ) : data?.available === false ? (
          <p className="py-12 text-center text-sm text-muted-foreground">
            Logs are only available when the dashboard runs in a Docker
            container.
          </p>
        ) : data?.logs ? (
          <pre
            ref={contentRef}
            className="whitespace-pre-wrap break-words font-mono text-xs leading-5"
          >
            {data.logs}
          </pre>
        ) : (
          <p className="py-12 text-center text-sm text-muted-foreground">
            No logs yet.
          </p>
        )}
      </div>
    </Dialog>
  );
};
