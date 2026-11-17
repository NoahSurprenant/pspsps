/** Makes the thing on first ask and then hands out the same one; a failed make is forgotten, so the next ask retries. */
export function lazily<T>(make: () => Promise<T>): () => Promise<T> {
  let made: Promise<T> | undefined;
  return () => {
    made ??= make().catch((error: unknown) => {
      made = undefined;
      throw error;
    });
    return made;
  };
}
