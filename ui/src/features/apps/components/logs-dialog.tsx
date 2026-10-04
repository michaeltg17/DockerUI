import { X } from 'lucide-react';
import { useEffect } from 'react';
import { createPortal } from 'react-dom';

import { Spinner } from '@/components/ui/spinner';

import { useLogs } from '../hooks/use-logs';

type LogsDialogProps = {
  open: boolean;
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

export const LogsDialog = ({ open, onClose }: LogsDialogProps) => {
  const { data, isPending, isError, error, refetch } = useLogs(open);

  useEffect(() => {
    if (!open) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Docker UI logs"
        className="flex max-h-[80vh] w-full max-w-3xl flex-col overflow-hidden rounded-lg border border-border bg-background shadow-lg"
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold">Docker UI logs</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close logs"
            className="rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>
        <div className="overflow-auto p-4">
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
            <pre className="whitespace-pre-wrap break-words font-mono text-xs leading-5">
              {data.logs}
            </pre>
          ) : (
            <p className="py-12 text-center text-sm text-muted-foreground">
              No logs yet.
            </p>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
};
