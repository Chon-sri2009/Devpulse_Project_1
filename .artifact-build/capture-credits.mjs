import { chromium } from "playwright-core";

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
  args: ["--disable-dev-shm-usage", "--no-first-run"],
});

const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
await page.goto("http://127.0.0.1:5214/credits", { waitUntil: "networkidle", timeout: 30000 });
await page.screenshot({ path: ".artifact-build/screenshots/credits.png", fullPage: true });
await browser.close();
