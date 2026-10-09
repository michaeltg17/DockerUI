import { nanoid } from 'nanoid';
import { create } from 'zustand';

export type Notification = {
  id: string;
  type: 'info' | 'warning' | 'success' | 'error';
  title: string;
  message?: string;
  /**
   * True while the notification reports an in-flight operation: a spinner is
   * shown in place of the type icon and it stays until it is updated or
   * dismissed, e.g. a LAN scan that is still running.
   */
  pending?: boolean;
};

type NotificationsStore = {
  notifications: Notification[];
  /** Adds a notification and returns its id so it can be updated later. */
  addNotification: (notification: Omit<Notification, 'id'>) => string;
  /** Merges a patch into an existing notification, e.g. a scan that finished. */
  updateNotification: (id: string, patch: Omit<Notification, 'id'>) => void;
  dismissNotification: (id: string) => void;
};

export const useNotifications = create<NotificationsStore>((set) => ({
  notifications: [],
  addNotification: (notification) => {
    const id = nanoid();
    set((state) => ({
      notifications: [...state.notifications, { id, ...notification }],
    }));
    return id;
  },
  updateNotification: (id, patch) =>
    set((state) => ({
      notifications: state.notifications.map((notification) =>
        notification.id === id ? { ...notification, ...patch } : notification,
      ),
    })),
  dismissNotification: (id) =>
    set((state) => ({
      notifications: state.notifications.filter(
        (notification) => notification.id !== id,
      ),
    })),
}));
