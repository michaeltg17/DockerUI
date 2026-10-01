import { Boxes } from 'lucide-react';

import { cn } from '@/utils/cn';

type AppIconProps = {
  icon: string | null;
  name: string;
  className?: string;
};

const getInitials = (name: string) =>
  name
    .split(/[\s._/-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

export const AppIcon = ({ icon, name, className }: AppIconProps) => {
  if (icon) {
    return (
      <img
        src={icon}
        alt=""
        className={cn('h-12 w-12 rounded-lg object-cover', className)}
      />
    );
  }

  const initials = getInitials(name);

  return (
    <div
      className={cn(
        'flex h-12 w-12 items-center justify-center rounded-lg bg-slate-200 text-lg font-semibold text-slate-600',
        className,
      )}
    >
      {initials ? initials : <Boxes className="size-6" aria-hidden="true" />}
    </div>
  );
};
