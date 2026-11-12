import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import { APPS_QUERY_KEY, getApps } from '@/features/apps';
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
  const queryClient = useQueryClient();

  useEffect(() => {
    void check();

    // Overlaps the boot's two requests: the session check is one trivial
    // round-trip, while the apps fetch is the real boot cost (a live daemon
    // query) and does not depend on the check's outcome, so boot drops from
    // check + fetch to the slower of the two. When signed out the prefetch
    // 401s, which is expected and harmless: the error leaves no data behind,
    // so the query is stale and the dashboard's own useApps refetches on
    // mount once signed in.
    void queryClient.prefetchQuery({
      queryKey: APPS_QUERY_KEY,
      queryFn: getApps,
    });
  }, [check, queryClient]);

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
