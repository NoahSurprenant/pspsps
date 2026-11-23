/** Asks the server (same origin, so the admin's cookie goes along); throws the server's own message when it refuses. */
export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const refusal = (await response.json().catch(() => null)) as { message?: string } | null;
    throw new Error(refusal?.message ?? `That didn't work (${response.status}).`);
  }
  return (response.status === 204 ? null : await response.json().catch(() => null)) as T;
}

/** The words for whatever went wrong, for the page to show. */
export const messageOf = (error: unknown): string => (error instanceof Error ? error.message : String(error));
