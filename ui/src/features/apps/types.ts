export type AppState = 'running' | 'stopped';

export type AppSource = 'docker' | 'shortcut' | 'lan';

export interface AppService {
  name: string;
  containerId: string;
  image: string | null;
  isRunning: boolean;
}

export interface App {
  name: string;
  /**
   * The name shown on the card: for docker apps the per-app override, for
   * shortcuts the enriched page title (LAN) or the shortcut name, else `name`.
   */
  displayName: string;
  icon: string | null;
  state: AppState;
  url: string | null;
  services: AppService[];
  source: AppSource;
  /**
   * The hue of the initials icon: a random hue decided once (when the app was
   * first seen, or the shortcut was created), persisted server-side and never
   * changed by the app. The API resolves a hue for every app before it reaches
   * the wire, so this is never null.
   */
  color: number;
  /**
   * The human-readable problem the app is in (crashed, crash-looping, unhealthy,
   * ...), shown as a badge on the card. Null when healthy; always null for
   * shortcuts and lan apps.
   */
  problem: string | null;
  /**
   * The compose project a split part was split out of (the part's name is
   * 'project-service'). Null for whole stacks, standalone containers, and
   * shortcuts.
   */
  splitFrom: string | null;
  /**
   * The services split out of this stack, shown checked in its Split submenu;
   * empty when none are split.
   */
  splitServices: string[];
}

export interface Shortcut {
  name: string;
  icon: string | null;
  url: string;
  /** The hue of the initials icon, decided once at creation and never changed. */
  color: number;
}
