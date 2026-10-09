import type { ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';

export type ConfirmDialogProps = {
  /** The header title; it is also the dialog's accessible name. */
  title: string;
  /** The body copy explaining what will happen. */
  message: ReactNode;
  /** Label of the confirming action, e.g. 'Delete' or 'Save'. */
  confirmLabel: string;
  /** True while the confirming action is in flight; disables both buttons. */
  isPending?: boolean;
  onConfirm: () => void;
  onClose: () => void;
};

export const ConfirmDialog = ({
  title,
  message,
  confirmLabel,
  isPending = false,
  onConfirm,
  onClose,
}: ConfirmDialogProps) => (
  <Dialog title={title} onClose={onClose} className="max-w-sm">
    <div className="flex flex-col gap-4 p-4">
      <p className="text-sm text-muted-foreground">{message}</p>
      <div className="flex justify-end gap-2">
        <Button variant="outline" onClick={onClose} disabled={isPending}>
          Cancel
        </Button>
        <Button variant="destructive" onClick={onConfirm} isLoading={isPending}>
          {confirmLabel}
        </Button>
      </div>
    </div>
  </Dialog>
);
