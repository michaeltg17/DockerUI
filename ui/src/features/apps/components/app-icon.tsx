import { Boxes } from 'lucide-react';
import { useEffect, useState } from 'react';

import { cn } from '@/utils/cn';

type AppIconProps = {
  icon: string | null;
  name: string;
  /** A persisted icon hue; when set it wins over the neutral fallback. */
  hue?: number;
  className?: string;
};

// The hue for the should-never-happen case of an app with no persisted color.
const NeutralHue = 222;

// The initials tile's background; the text color is derived from it (see below).
const TileSaturation = 0.6;
const TileLightness = 0.55;

// Near-black (stone-900) for bright hues, where white fails WCAG's 3:1 for the
// large tile text.
const Ink = [28, 25, 23] as const;

/** A random hue (0–359); the color a new app or shortcut gets, decided once. */
export const randomHue = () => Math.floor(Math.random() * 360);

const getInitials = (name: string) =>
  name
    .split(/[\s._/-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

const hslToRgb = (
  hue: number,
  saturation: number,
  lightness: number,
): [number, number, number] => {
  const c = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const h = ((hue % 360) + 360) % 360;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = lightness - c / 2;

  const [r, g, b] =
    h < 60
      ? [c, x, 0]
      : h < 120
        ? [x, c, 0]
        : h < 180
          ? [0, c, x]
          : h < 240
            ? [0, x, c]
            : h < 300
              ? [x, 0, c]
              : [c, 0, x];

  return [
    Math.round((r + m) * 255),
    Math.round((g + m) * 255),
    Math.round((b + m) * 255),
  ];
};

const srgbChannelToLinear = (channel: number): number => {
  const v = channel / 255;
  return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
};

// WCAG relative luminance of an sRGB color (channels 0–255).
const relativeLuminance = ([r, g, b]: readonly [
  number,
  number,
  number,
]): number =>
  0.2126 * srgbChannelToLinear(r) +
  0.7152 * srgbChannelToLinear(g) +
  0.0722 * srgbChannelToLinear(b);

const wcagContrast = (luminanceA: number, luminanceB: number): number => {
  const [lighter, darker] =
    luminanceA >= luminanceB
      ? [luminanceA, luminanceB]
      : [luminanceB, luminanceA];
  return (lighter + 0.05) / (darker + 0.05);
};

// Picks the tile text color with the higher WCAG contrast, so the initials stay
// legible on bright hues (yellow, green) as well as dark ones.
const readableTileTextColor = (hue: number): string => {
  const tileLuminance = relativeLuminance(
    hslToRgb(hue, TileSaturation, TileLightness),
  );
  const inkLuminance = relativeLuminance(Ink);
  return wcagContrast(1, tileLuminance) >=
    wcagContrast(inkLuminance, tileLuminance)
    ? '#ffffff'
    : 'rgb(28, 25, 23)';
};

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
        color: readableTileTextColor(resolvedHue),
      }}
    >
      {initials ? initials : <Boxes className="size-6" aria-hidden="true" />}
    </div>
  );
};
