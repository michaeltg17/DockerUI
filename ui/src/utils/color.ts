export const hslToRgb = (
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
export const relativeLuminance = ([r, g, b]: readonly [
  number,
  number,
  number,
]): number =>
  0.2126 * srgbChannelToLinear(r) +
  0.7152 * srgbChannelToLinear(g) +
  0.0722 * srgbChannelToLinear(b);

export const wcagContrast = (
  luminanceA: number,
  luminanceB: number,
): number => {
  const [lighter, darker] =
    luminanceA >= luminanceB
      ? [luminanceA, luminanceB]
      : [luminanceB, luminanceA];
  return (lighter + 0.05) / (darker + 0.05);
};

// Near-black (stone-900) for bright hues, where white fails WCAG's 3:1 for
// large tile text.
const Ink = [28, 25, 23] as const;

/**
 * Picks the text color with the higher WCAG contrast against a tile of the
 * given hsl color, so text stays legible on bright hues (yellow, green) as
 * well as dark ones.
 */
export const readableTileTextColor = (
  hue: number,
  saturation: number,
  lightness: number,
): string => {
  const tileLuminance = relativeLuminance(hslToRgb(hue, saturation, lightness));
  const inkLuminance = relativeLuminance(Ink);
  return wcagContrast(1, tileLuminance) >=
    wcagContrast(inkLuminance, tileLuminance)
    ? '#ffffff'
    : 'rgb(28, 25, 23)';
};
