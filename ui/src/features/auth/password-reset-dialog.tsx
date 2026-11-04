import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';

type PasswordResetDialogProps = {
  open: boolean;
  onClose: () => void;
};

const codeClass =
  'rounded bg-accent px-1 py-0.5 font-mono text-xs text-accent-foreground';

/**
 * Explains how to remove the dashboard password manually from the settings
 * file. Offered from the login page (a locked-out user) and from the
 * dashboard menu (a signed-in user): whoever can edit the file is the
 * admin, so the file is the reset path in both cases.
 */
export const PasswordResetDialog = ({
  open,
  onClose,
}: PasswordResetDialogProps) => {
  if (!open) return null;

  return (
    <Dialog title="Remove the dashboard password" onClose={onClose}>
      <div className="flex flex-col gap-3 p-4 text-sm">
        <p>
          The password is stored in the dashboard&rsquo;s{' '}
          <code className={codeClass}>appsettings.json</code> — in the{' '}
          <code className={codeClass}>AdminPasswordHash</code> key of the{' '}
          <code className={codeClass}>DockerUI:Auth</code> section.
        </p>

        <ol className="list-decimal space-y-1 pl-5">
          <li>
            Remove the <code className={codeClass}>AdminPasswordHash</code> key
            from that section — or the whole section if it holds nothing else.
          </li>
          <li>
            Save the file. The dashboard watches it and reloads automatically,
            so no restart is needed.
          </li>
        </ol>

        <p>
          The dashboard is then open to everyone who can reach it — set a new
          password from the dashboard menu any time.
        </p>

        <div className="flex justify-end pt-1">
          <Button onClick={onClose}>Close</Button>
        </div>
      </div>
    </Dialog>
  );
};
