import { Boxes } from 'lucide-react';
import { useEffect, useState } from 'react';

import { cn } from '@/utils/cn';
import { readableTileTextColor } from '@/utils/color';

type AppIconProps = {
  icon: string | null;
  name: string;
  /** A persisted icon hue; when set it wins over the neutral fallback. */
  hue?: number;
  className?: string;
};

// The hue for the should-never-happen case of an app with no persisted color.
const NeutralHue = 222;

// The initials tile's background; the tile text color is derived from it
// (readableTileTextColor in utils/color).
const TileSaturation = 0.6;
const TileLightness = 0.55;

/** A random hue (0–359); the color a new app or shortcut gets, decided once. */
export const randomHue = () => Math.floor(Math.random() * 360);

const getInitials = (name: string) =>
  name
    .split(/[\s._/-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

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
        loading="lazy"
        onError={() => setFailed(true)}
        className={cn('rounded-2xl object-cover', className)}
      />
    );
  }

  const initials = getInitials(name);
  const resolvedHue = hue ?? NeutralHue;

  return (
    <div
      className={cn(
        'flex items-center justify-center rounded-2xl font-semibold',
        className,
      )}
      style={{
        backgroundColor: `hsl(${resolvedHue} ${TileSaturation * 100}% ${TileLightness * 100}%)`,
        color: readableTileTextColor(
          resolvedHue,
          TileSaturation,
          TileLightness,
        ),
      }}
    >
      {initials ? initials : <Boxes className="size-6" aria-hidden="true" />}
    </div>
  );
};
