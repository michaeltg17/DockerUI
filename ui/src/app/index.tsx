import { useEffect } from 'react';

import { LoginPage, useAuth } from '@/features/auth';

import { AppProvider } from './provider';
import { AppRouter } from './router';

// Keeps the router out until the session is known: signed-out shows the login
// page, signed-in shows the dashboard. A 401 on any request (an expired
// session) drops the app back to the login page. While the session is still
// being checked nothing renders: the check is one local round-trip, and the
// dashboard shows its own loading spinner once signed in, so a page refresh
// never shows two spinners.
const AuthGate = () => {
  const state = useAuth((auth) => auth.state);
  const check = useAuth((auth) => auth.check);

  useEffect(() => {
    void check();
  }, [check]);

  if (state === 'checking') {
    return null;
  }

  if (state === 'signed-out') {
    return <LoginPage />;
  }

  return <AppRouter />;
};

export const App = () => {
  return (
    <AppProvider>
      <AuthGate />
    </AppProvider>
  );
};
