import { useEffect } from 'react';

import { Spinner } from '@/components/ui/spinner';
import { LoginPage, useAuth } from '@/features/auth';

import { AppProvider } from './provider';
import { AppRouter } from './router';

// Keeps the router out until the session is known: checking shows a spinner,
// signed-out shows the login page, signed-in shows the dashboard. A 401 on
// any request (an expired session) drops the app back to the login page.
const AuthGate = () => {
  const state = useAuth((auth) => auth.state);
  const check = useAuth((auth) => auth.check);

  useEffect(() => {
    void check();
  }, [check]);

  if (state === 'checking') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background">
        <Spinner size="xl" />
      </div>
    );
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
