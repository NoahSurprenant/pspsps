import { expect, test } from "@playwright/test";

// The files a search engine asks for, served as themselves, not as the game's page.
test("robots.txt is a robots file", async ({ request }) => {
  const response = await request.get("/robots.txt");

  expect(response.ok()).toBe(true);
  expect(response.headers()["content-type"]).toContain("text/plain");
  expect(await response.text()).toMatch(/^User-agent: \*$/m);
});
