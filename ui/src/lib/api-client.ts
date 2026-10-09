import Axios, { type InternalAxiosRequestConfig } from 'axios';

import { useNotifications } from '@/components/ui/notifications';

// Requests flagged 'silentError' skip the global error toast; the caller handles
// the (expected) failure itself, e.g. the dashboard restart dropping the socket.
declare module 'axios' {
  export interface AxiosRequestConfig {
    silentError?: boolean;
  }
}

function requestInterceptor(config: InternalAxiosRequestConfig) {
  if (config.headers) {
    config.headers.Accept = 'application/json';
  }

  return config;
}

export const api = Axios.create({
  baseURL: '/api',
});

api.interceptors.request.use(requestInterceptor);
api.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
    if (error.config?.silentError) {
      return Promise.reject(error);
    }

    const data = error.response?.data;
    const message =
      (typeof data === 'object' &&
        data !== null &&
        (data.detail || data.message)) ||
      error.message;

    useNotifications.getState().addNotification({
      type: 'error',
      title: 'Error',
      message,
    });

    return Promise.reject(error);
  },
);

export const http = {
  get: <T>(url: string) => api.get<T, T>(url),
  post: <T>(url: string, body?: unknown) => api.post<T, T>(url, body),
  put: <T>(url: string, body?: unknown) => api.put<T, T>(url, body),
  delete: <T>(url: string) => api.delete<T, T>(url),
};
