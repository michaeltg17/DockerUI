/** Prefix a scheme-less url with https so bare domains like 'google.com' work. */
export const normalizeUrl = (value: string) => {
  const trimmed = value.trim();
  return /^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
};

/**
 * Minimal url validation: the value must parse as an http(s) url with a real
 * host. A host needs a dot (a domain or ip) so that a bare word like
 * 'dfdfdf' is rejected while 'google.com' is accepted; 'localhost' is allowed
 * for local stacks.
 */
export const isValidUrl = (value: string) => {
  try {
    const url = new URL(normalizeUrl(value));
    const host = url.hostname;
    return (
      (url.protocol === 'http:' || url.protocol === 'https:') &&
      host.length > 0 &&
      (host.includes('.') || host === 'localhost')
    );
  } catch {
    return false;
  }
};
