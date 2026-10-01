import Axios, {
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';

import { useNotifications } from '@/components/ui/notifications';
import { env } from '@/config/env';

function requestInterceptor(config: InternalAxiosRequestConfig) {
  if (config.headers) {
    config.headers.Accept = 'application/json';
  }

  return config;
}

export const api = Axios.create({
  baseURL: env.API_URL,
});

api.interceptors.request.use(requestInterceptor);
api.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
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

/**
 * The response interceptor above unwraps `response.data` at runtime,
 * but axios still types the calls as `AxiosResponse<T>`. These helpers
 * make that unwrapping explicit in the type system.
 */
export const http = {
  get: <T>(url: string) =>
    api.get<T, AxiosResponse<T>>(url).then((response) => response.data as T),
  post: <T>(url: string) =>
    api.post<T, AxiosResponse<T>>(url).then((response) => response.data as T),
};
