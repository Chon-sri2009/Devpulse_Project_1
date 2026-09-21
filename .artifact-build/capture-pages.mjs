import { chromium } from "playwright-core";
import path from "node:path";

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
  args: ["--disable-dev-shm-usage", "--no-first-run"],
});

const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  deviceScaleFactor: 1,
  colorScheme: "light",
});
const page = await context.newPage();
const output = path.resolve(".artifact-build/screenshots");
const captures = [
  ["overview", "/"],
  ["operations", "/operations"],
  ["telemetry", "/telemetry"],
  ["inspectors", "/tools"],
  ["network-calculator", "/network-calculator"],
  ["website-audit", "/website-audit"],
  ["api-runner", "/api-runner"],
  ["dns-email", "/dns-email"],
  ["focus-lounge", "/focus-lounge"],
];

for (const [name, route] of captures) {
  await page.goto(`http://127.0.0.1:5216${route}`, { waitUntil: "domcontentloaded", timeout: 30000 });
  await page.waitForTimeout(route === "/" || route === "/telemetry" ? 3000 : 1300);
  await page.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important}" });
  await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: false });
}

await browser.close();
