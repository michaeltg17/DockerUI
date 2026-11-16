import { useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { FieldError } from '@/components/ui/field-error';
import { Input } from '@/components/ui/input';

import { useSettings } from '../hooks/use-settings';

/** The subnet scanned when the field is left empty (the most common home LAN). */
const DEFAULT_SUBNET = '192.168.1.0/24';

type LanScanDialogProps = {
  open: boolean;
  /** Starts a scan over the effective subnet (the field's value, or the default). */
  onScan: (subnet: string) => void;
  onClose: () => void;
};

export const LanScanDialog = ({
  open,
  onScan,
  onClose,
}: LanScanDialogProps) => {
  if (!open) return null;
  return <LanScanDialogForm onScan={onScan} onClose={onClose} />;
};

/**
 * Mounted fresh every time the dialog opens, so the field always starts from
 * the saved subnet instead of leaking state from the previous session (an
 * async reset effect could race a fast re-open).
 */
const LanScanDialogForm = ({
  onScan,
  onClose,
}: Omit<LanScanDialogProps, 'open'>) => {
  const { data: settings } = useSettings();
  const savedSubnet = settings?.lan?.subnet ?? '';
  const [subnet, setSubnet] = useState(savedSubnet);

  const trimmedSubnet = subnet.trim();
  const subnetError = validateSubnet(trimmedSubnet);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (subnetError !== null) return;

    onScan(trimmedSubnet.length > 0 ? trimmedSubnet : DEFAULT_SUBNET);
    onClose();
  };

  return (
    <Dialog title="Scan LAN" dirty={subnet !== savedSubnet} onClose={onClose}>
      <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-4">
        <div className="flex flex-col gap-1.5 text-sm">
          <label className="flex flex-col gap-1.5">
            <span className="font-medium">Subnet</span>
            <Input
              type="text"
              autoComplete="off"
              spellCheck={false}
              placeholder={DEFAULT_SUBNET}
              aria-invalid={subnetError !== null}
              value={subnet}
              onChange={(event) => setSubnet(event.target.value)}
            />
            <FieldError
              error={subnetError}
              hint={`Leave empty to scan ${DEFAULT_SUBNET}.`}
            />
          </label>
          <p className="text-xs text-muted-foreground">
            The CIDR range to probe for web services; the scanned subnet is
            saved for the next scan.
          </p>
        </div>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={subnetError !== null}>
            Scan
          </Button>
        </div>
      </form>
    </Dialog>
  );
};

/**
 * The dialog's CIDR check: an IPv4 network address (four octets of at most
 * 255) and a prefix length of at most 32. An empty value is valid and scans
 * the default subnet.
 */
const validateSubnet = (value: string): string | null => {
  if (value.length === 0) return null;

  const [address, prefix, ...rest] = value.split('/');

  if (rest.length > 0 || !isIpv4Address(address))
    return `Use an IPv4 CIDR, e.g. '${DEFAULT_SUBNET}'.`;

  return isPrefixLength(prefix)
    ? null
    : 'The prefix length must be between 0 and 32.';
};

const isIpv4Address = (value: string | undefined): boolean =>
  value !== undefined &&
  value.split('.').length === 4 &&
  value
    .split('.')
    .every((octet) => /^\d{1,3}$/.test(octet) && Number(octet) <= 255);

const isPrefixLength = (value: string | undefined): boolean =>
  value !== undefined && /^\d{1,2}$/.test(value) && Number(value) <= 32;
