/**
 * True when the value carries an explicit scheme: '://' (vscode://, whatsapp://)
 * or '://' with a query directly after the colon (magnet:?xt=...). Only these
 * two shapes count, so a bare 'host:port' like 'localhost:8080' is never
 * misread as a scheme and still gets the https:// prefix.
 */
export const hasUrlScheme = (value: string) => {
  const trimmed = value.trim();
  return trimmed.includes('://') || trimmed.includes(':?');
};

/** Prefix a scheme-less url with https so bare domains like 'google.com' work. */
export const normalizeUrl = (value: string) => {
  const trimmed = value.trim();
  return hasUrlScheme(trimmed) ? trimmed : `https://${trimmed}`;
};

/**
 * Minimal url validation. An explicit scheme only has to parse as an absolute
 * url (protocol handlers like obs:// are accepted as-is). A scheme-less value
 * is checked as http(s) against a real host: the host needs a dot (a domain or
 * ip) so that a bare word like 'dfdfdf' is rejected while 'google.com' is
 * accepted; 'localhost' is allowed for local stacks.
 */
export const isValidUrl = (value: string) => {
  const trimmed = value.trim();
  try {
    if (hasUrlScheme(trimmed)) {
      return new URL(trimmed).protocol.length > 0;
    }
    const host = new URL(`https://${trimmed}`).hostname;
    return host.length > 0 && (host.includes('.') || host === 'localhost');
  } catch {
    return false;
  }
};
