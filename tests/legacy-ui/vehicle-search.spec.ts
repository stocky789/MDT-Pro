import { expect, Page, test } from "@playwright/test";
import path from "node:path";
import { readFile } from "node:fs/promises";

const root = process.cwd();
const htmlPath = path.join(root, "MDTPro/main/pages/vehicleSearch.html");
const scriptPath = path.join(root, "MDTPro/main/scripts/vehicleSearch.js");
const rootCssPath = path.join(root, "MDTPro/main/styles/root.css");
const vehicleCssPath = path.join(root, "MDTPro/main/styles/vehicleSearch.css");

const language = {
  values: { empty: "" },
  vehicleSearch: {
    static: {
      title: "Vehicle Search",
      bolosTitle: "BOLOs",
      addBOLO: "Add BOLO",
      removeBOLO: "Remove",
    },
    labels: {},
    notifications: {
      emptySearchInput: "Enter a plate",
      vehicleNotFound: "Vehicle not found",
      stolen: "ALERT",
      vehicleStolen: "Vehicle",
      reportedStolen: "reported STOLEN",
      boloRemoved: "BOLO removed.",
    },
  },
};

async function openVehicleSearch(
  page: Page,
  vehicle: Record<string, unknown>,
  options: { viewport?: { width: number; height: number } } = {},
) {
  if (options.viewport) await page.setViewportSize(options.viewport);
  await page.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    if (
      url.pathname === "/pages/vehicleSearch.html" ||
      url.pathname === "/vehicleSearch.html"
    ) {
      return route.fulfill({
        contentType: "text/html",
        body: await readFile(htmlPath, "utf8"),
      });
    }
    if (url.pathname === "/script/root.js") {
      return route.fulfill({
        contentType: "text/javascript",
        body: `
          window.topWindow = { showNotification: () => {}, openIdInReport: () => {} };
          window.getConfig = async () => ({ updateDomWithLanguageOnLoad: false });
          window.getLanguage = async () => (${JSON.stringify(language)});
          window.getLanguageValue = async (value) => value == null ? '' : String(value);
          window.getColorForValue = () => 'var(--color-text-primary)';
          window.updateDomWithLanguage = async () => {};
          window.showLoadingOnButton = (button) => button.classList.add('loading');
          window.hideLoadingOnButton = (button) => button.classList.remove('loading');
          window.openInPedSearch = () => {};
        `,
      });
    }
    if (url.pathname === "/script/vehicleSearch.js") {
      return route.fulfill({
        contentType: "text/javascript",
        body: await readFile(scriptPath, "utf8"),
      });
    }
    if (
      url.pathname === "/style/root.css" ||
      url.pathname === "/style/vehicleSearch.css"
    ) {
      if (url.pathname === "/style/root.css") {
        return route.fulfill({
          contentType: "text/css",
          body: await readFile(rootCssPath, "utf8").catch(() => ""),
        });
      }
      if (url.pathname === "/style/vehicleSearch.css") {
        return route.fulfill({
          contentType: "text/css",
          body: await readFile(vehicleCssPath, "utf8").catch(() => ""),
        });
      }
    }
    if (url.pathname === "/integration")
      return route.fulfill({ json: { stopEventsProvider: "StopThePed" } });
    if (url.pathname === "/data/contextVehicle")
      return route.fulfill({ status: 404, body: "" });
    if (url.pathname === "/data/specificVehicle")
      return route.fulfill({ json: vehicle });
    if (url.pathname === "/data/vehicleSearchByPlate")
      return route.fulfill({ json: [] });
    if (url.pathname === "/data/impoundReportsByPlate")
      return route.fulfill({ json: [] });
    if (url.pathname === "/data/searchHistory")
      return route.fulfill({ json: [] });
    if (url.pathname === "/data/nearbyVehicles")
      return route.fulfill({ json: [] });
    if (url.pathname === "/post/removeBOLO")
      return route.fulfill({ json: { success: true } });
    return route.fulfill({ status: 204, body: "" });
  });
  await page.goto("http://legacy-mdt.test/pages/vehicleSearch.html");
}

async function search(page: Page, query = "ABC123") {
  await page.locator("#vehicleSearchInput").fill(query);
  await page.locator(".searchInputWrapper button").click();
  await expect(page.locator('[data-property="LicensePlate"]')).not.toHaveValue(
    "",
  );
}

test("vehicle search renders PascalCase payloads", async ({ page }) => {
  await openVehicleSearch(page, {
    LicensePlate: "ABC123",
    ModelDisplayName: "Dominator",
    Owner: "Dylan Kelly",
    Color: "Blue",
    PrimaryColor: "Blue",
    SecondaryColor: "White",
    PrimaryColorSpecific: "Dark Blue",
    SecondaryColorSpecific: "Pearl White",
    RegistrationStatus: "Valid",
    RegistrationExpiration: "2030-01-01T00:00:00",
    InsuranceStatus: "Expired",
  });
  await search(page);

  await expect(page.locator('[data-property="LicensePlate"]')).toHaveValue(
    "ABC123",
  );
  await expect(page.locator('[data-property="ModelDisplayName"]')).toHaveValue(
    "Dominator",
  );
  await expect(page.locator('[data-property="Owner"]')).toHaveValue(
    "Dylan Kelly",
  );
  await expect(page.locator('[data-property="Color"]')).toHaveText("Blue");
  await expect(page.locator('[data-property="PrimaryColor"]')).toHaveValue(
    "Blue",
  );
  await expect(page.locator('[data-property="SecondaryColor"]')).toHaveValue(
    "White",
  );
  await expect(
    page.locator('[data-property="PrimaryColorSpecific"]'),
  ).toHaveValue("Dark Blue");
  await expect(
    page.locator('[data-property="SecondaryColorSpecific"]'),
  ).toHaveValue("Pearl White");
  await expect(
    page.locator('[data-property="RegistrationStatus"]'),
  ).toHaveValue("Valid");
  await expect(
    page.locator('[data-property="RegistrationExpiration"]'),
  ).not.toHaveValue("");
  await expect(page.locator('[data-property="InsuranceStatus"]')).toHaveValue(
    "Expired",
  );
});

test("document section visible when registration or insurance status exists without expiration", async ({
  page,
}) => {
  await openVehicleSearch(page, {
    LicensePlate: "STATUS1",
    ModelDisplayName: "Bison",
    RegistrationStatus: "Expired",
    InsuranceStatus: "",
  });
  await search(page, "STATUS1");

  await expect(page.locator('[data-language="documentsTitle"]')).toBeVisible();
  await expect(
    page.locator('[data-property="RegistrationStatus"]'),
  ).toHaveValue("Expired");
  await expect(
    page
      .locator('[data-property="RegistrationExpiration"]')
      .locator("xpath=.."),
  ).toBeHidden();
});

test("document section hidden when all document fields are empty", async ({
  page,
}) => {
  await openVehicleSearch(page, {
    LicensePlate: "EMPTYDOC",
    ModelDisplayName: "Bison",
  });
  await search(page, "EMPTYDOC");

  await expect(page.locator('[data-language="documentsTitle"]')).toBeHidden();
});

test("fullscreen/maximized window keeps document rows visible and scrollable", async ({
  page,
}) => {
  await openVehicleSearch(
    page,
    {
      LicensePlate: "FULL123",
      ModelDisplayName: "Bison",
      RegistrationStatus: "Expired",
      InsuranceStatus: "Valid",
      InsuranceExpiration: "2030-01-01T00:00:00",
    },
    { viewport: { width: 480, height: 360 } },
  );
  await search(page, "FULL123");

  await expect(page.locator('[data-language="documentsTitle"]')).toBeVisible();
  await expect(
    page.locator('[data-property="RegistrationStatus"]'),
  ).toBeVisible();
  await expect(
    page.locator('[data-property="InsuranceExpiration"]'),
  ).toBeVisible();
  await page.locator('[data-property="InsuranceExpiration"]').scrollIntoViewIfNeeded();
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollHeight > window.innerHeight,
      ),
    )
    .toBeTruthy();
});

test("remove BOLO payload targets selected BOLO reason/issued/expires fields", async ({
  page,
}) => {
  let removePayload: unknown;
  await openVehicleSearch(page, {
    LicensePlate: "BOLO1",
    ModelDisplayName: "Bison",
    CanModifyBOLOs: true,
    BOLOs: [
      {
        Reason: "Armed robbery",
        Issued: "2026-01-01T00:00:00Z",
        Expires: "2026-01-08T00:00:00Z",
      },
      {
        Reason: "Felony stop",
        IssuedAt: "2026-02-01T00:00:00Z",
        ExpiresAt: "2026-02-08T00:00:00Z",
      },
    ],
  });
  await page.route("**/post/removeBOLO", async (route) => {
    removePayload = JSON.parse(route.request().postData() || "{}");
    return route.fulfill({ json: { success: true } });
  });
  await search(page, "BOLO1");
  await expect(
    page.locator(".boloRow").filter({ hasText: "Armed robbery" }),
  ).toBeVisible();
  await expect(
    page.locator(".boloRow").filter({ hasText: "Felony stop" }),
  ).toBeVisible();
  await page
    .locator(".boloRow")
    .filter({ hasText: "Felony stop" })
    .locator(".boloRemoveBtn")
    .click();

  await expect
    .poll(() => removePayload)
    .toEqual({
      LicensePlate: "BOLO1",
      Reason: "Felony stop",
      Issued: "2026-02-01T00:00:00Z",
      Expires: "2026-02-08T00:00:00Z",
    });
});
