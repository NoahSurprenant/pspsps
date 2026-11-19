/**
 * Query parameters whose values never go into a log line, a span or an error report: sign-in tokens, codes, keys, and
 * who a request is for. Matched anywhere in the name, in any case, so "access_token", "code" and "userId" all count. A
 * few harmless names match too ("countryCode"); a redacted value costs nothing. A parameter named just "id" counts too:
 * SignalR's connection token goes there (/hubs/game?id=...), in its errors' URLs. The server redacts again
 * (QueryRedaction, in src/Pspsps.Server/Telemetry/Browser/ClientTelemetryOtlp.cs).
 */
const SENSITIVE = /token|code|secret|passw|pwd|key|auth|session|sig|credential|cookie|email|user|state|nonce|otp|ticket|hash|jwt|^id$/i;

export const REDACTED = "REDACTED";

/** Whether a query parameter's value is kept out of telemetry. */
export function isSensitiveParam(name: string): boolean {
  return SENSITIVE.test(name);
}

/**
 * A URL as telemetry may carry it: its path and query, every sensitive parameter's value REDACTED. Absolute URLs lose
 * their scheme and host (`base` resolves relative ones), and every URL its fragment. An unparseable URL gives "".
 */
export function redactUrl(url: string | null | undefined, base = "http://localhost"): string {
  if (!url) {
    return "";
  }
  let parsed: URL;
  try {
    parsed = new URL(url, base);
  } catch {
    return "";
  }
  const kept = [...parsed.searchParams].map(([name, value]) =>
    `${encodeURIComponent(name)}=${isSensitiveParam(name) ? REDACTED : encodeURIComponent(value)}`);
  return parsed.pathname + (kept.length ? "?" + kept.join("&") : "");
}

/** A query string in text: what follows a "?" up to a space, quote, bracket or "#". */
const QUERY_IN_TEXT = /\?([^\s#'"<>()]+)/g;

/**
 * `text` (an error's message, a stack) with the sensitive values of every query string in it REDACTED, whatever the URL
 * around it: "see /auth/callback?code=abc&page=2" becomes "see /auth/callback?code=REDACTED&page=2".
 */
export function redactUrlsIn(text: string): string {
  return text.replace(QUERY_IN_TEXT, (_, query: string) => "?" + query.split("&").map((pair) => {
    const equals = pair.indexOf("=");
    if (equals < 0) {
      return pair;
    }
    let name = pair.slice(0, equals);
    try {
      name = decodeURIComponent(name);
    } catch {
      // Not URL-encoded after all: matched as it is.
    }
    return isSensitiveParam(name) ? `${pair.slice(0, equals)}=${REDACTED}` : pair;
  }).join("&"));
}
