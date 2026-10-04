import type { ChangeEvent } from 'react';

import { useTheme } from '@/hooks/use-theme';
import { isTheme, themes, type Theme } from '@/lib/theme';

const themeLabels: Record<Theme, string> = {
  light: 'Light',
  dark: 'Dark',
  docker: 'Docker',
};

export const ThemePicker = () => {
  const { theme, setTheme } = useTheme();

  const handleChange = (event: ChangeEvent<HTMLSelectElement>) => {
    const value = event.target.value;
    if (isTheme(value)) {
      setTheme(value);
    }
  };

  return (
    <label className="flex items-center gap-2 text-sm text-header-foreground">
      <span>Theme</span>
      <select
        value={theme}
        onChange={handleChange}
        className="h-9 rounded-md border border-input bg-background px-2 text-sm text-foreground shadow-sm transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30"
      >
        {themes.map((option) => (
          <option key={option} value={option}>
            {themeLabels[option]}
          </option>
        ))}
      </select>
    </label>
  );
};
