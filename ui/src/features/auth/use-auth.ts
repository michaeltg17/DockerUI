import { create } from 'zustand';

import { setUnauthorizedHandler } from '@/lib/api-client';

import {
  changePassword as changePasswordRequest,
  getAuthStatus,
  login as loginRequest,
  type AuthStatus,
} from './auth-api';

/**
 * Whether the dashboard is usable in this browser: the session is checked on
 * boot, a 401 on any request ends it, and the login page re-enters it.
 */
export type AuthGateState = 'checking' | 'signed-out' | 'signed-in';

type AuthStore = {
  state: AuthGateState;
  /** Whether an admin password is configured on the dashboard. */
  passwordSet: boolean;
  /** Checks the session against the API, e.g. on app boot. */
  check: () => Promise<void>;
  /** The api-client's 401 hook: the session is gone, show the login page. */
  markSignedOut: () => void;
  /** Signs in with the dashboard's password; an incorrect one rejects. */
  signIn: (password: string) => Promise<AuthStatus>;
  /** Sets, changes, or removes the password (a null next password removes it). */
  setPassword: (
    current: string | null,
    next: string | null,
  ) => Promise<AuthStatus>;
};

export const useAuth = create<AuthStore>((set) => ({
  state: 'checking',
  passwordSet: false,
  check: async () => {
    set({ state: 'checking' });

    try {
      const status = await getAuthStatus();
      set({ state: 'signed-in', passwordSet: status.passwordSet });
    } catch {
      // A 401 already switched the app to the login page through the
      // api-client; any other failure keeps the user out as well.
      set({ state: 'signed-out' });
    }
  },
  markSignedOut: () => set({ state: 'signed-out' }),
  signIn: async (password) => {
    const status = await loginRequest(password);
    set({ state: 'signed-in', passwordSet: status.passwordSet });
    return status;
  },
  setPassword: async (current, next) => {
    const status = await changePasswordRequest(current, next);
    set({ passwordSet: status.passwordSet });
    return status;
  },
}));

// Registered once when the feature loads; the api-client calls back through
// this store instead of importing the auth feature itself.
setUnauthorizedHandler(() => useAuth.getState().markSignedOut());
