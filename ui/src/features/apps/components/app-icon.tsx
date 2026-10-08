import { Boxes } from 'lucide-react';
import { useEffect, useState } from 'react';

import { cn } from '@/utils/cn';

type AppIconProps = {
  icon: string | null;
  name: string;
  /** A persisted icon hue; when set it wins over the name-derived fallback. */
  hue?: number | null;
  className?: string;
};

const getInitials = (name: string) =>
  name
    .split(/[\s._/-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

export const getHue = (name: string) =>
  [...name.toLowerCase()].reduce(
    (hash, char) => (hash * 31 + char.charCodeAt(0)) % 997,
    7,
  ) % 360;

export const AppIcon = ({ icon, name, hue, className }: AppIconProps) => {
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
  }, [icon]);

  if (icon && !failed) {
    return (
      <img
        src={icon}
        alt=""
        onError={() => setFailed(true)}
        className={cn('rounded-2xl object-cover', className)}
      />
    );
  }

  const initials = getInitials(name);
  const resolvedHue = hue ?? getHue(name);

  return (
    <div
      className={cn(
        'flex items-center justify-center rounded-2xl font-semibold text-white',
        className,
      )}
      style={{ backgroundColor: `hsl(${resolvedHue} 60% 55%)` }}
    >
      {initials ? initials : <Boxes className="size-6" aria-hidden="true" />}
    </div>
  );
};
