import { http } from '@/lib/api-client';

/**
 * The auth endpoints' answer: whether an admin password is configured on the
 * dashboard.
 */
export type AuthStatus = {
  passwordSet: boolean;
};

/**
 * The current session's state. Answers 401 while a password is set and no
 * valid session exists, so the login page can tell the two modes apart.
 */
export const getAuthStatus = () => http.get<AuthStatus>('/auth/me');

export const login = (password: string) =>
  http.post<AuthStatus>('/auth/login', { password });

/**
 * Sets, changes, or removes the dashboard's password: a null next password
 * removes it, and a set password requires the correct current one. Failures
 * (e.g. a wrong current password, a 409 problem) are handled inline by the
 * password dialog, which skips the global error toast.
 */
export const changePassword = (
  currentPassword: string | null,
  newPassword: string | null,
) =>
  http.post<AuthStatus>(
    '/auth/change-password',
    {
      currentPassword,
      newPassword,
    },
    { silentError: true },
  );
