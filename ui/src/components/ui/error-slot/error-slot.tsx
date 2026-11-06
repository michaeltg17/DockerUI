import { cn } from '@/utils/cn';

type ErrorSlotProps = {
  /** The submit error to show; null keeps the slot reserved and invisible. */
  error: string | null;
};

/**
 * The submit-error slot of a form. The slot is always reserved (hidden, not
 * removed, while empty) so an error appearing cannot resize the dialog or
 * page around it.
 */
export const ErrorSlot = ({ error }: ErrorSlotProps) => (
  <p
    role="alert"
    className={cn(
      'min-h-10 rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive',
      error === null && 'invisible',
    )}
  >
    {error}
  </p>
);
