import * as React from 'react';

import {
  applyTheme,
  getStoredTheme,
  storeTheme,
  type Theme,
} from '@/lib/theme';

export const useTheme = () => {
  const [theme, setThemeState] = React.useState<Theme>(getStoredTheme);

  React.useEffect(() => {
    applyTheme(theme);
    storeTheme(theme);
  }, [theme]);

  const setTheme = React.useCallback((next: Theme) => {
    setThemeState(next);
  }, []);

  return { theme, setTheme };
};
