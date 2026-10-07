import { test, expect } from '../helpers/fixtures.mjs';

test('@smoke dedicated Driver logs in through the UI and reaches the map', async ({ driver }) => {
  await expect(driver.page).toHaveURL(new URL('/driver', driver.settings.baseUrl).href);
  await expect(driver.page.getByRole('heading', { name: /Pikat e karikimit|Charging stations/ })).toBeVisible();
});
