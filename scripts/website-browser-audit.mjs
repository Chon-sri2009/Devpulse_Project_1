import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import lighthouse from 'lighthouse';
import * as chromeLauncher from 'chrome-launcher';
import { chromium, devices } from 'playwright-core';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';

const require = createRequire(import.meta.url);
const args = process.argv.slice(2);
const value = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const url = value('--url');
const artifactDir = path.resolve(value('--artifact-dir') || '.');
const key = value('--key') || 'website';
const updateBaseline = args.includes('--update-baseline');

function findChromium(explicit) {
  const candidates = [
    explicit,
    process.env.CHROME_PATH,
    process.env.WebsiteAudit__ChromiumPath,
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
    '/usr/bin/google-chrome',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application', 'chrome.exe')
  ].filter(Boolean);
  return candidates.find(candidate => fs.existsSync(candidate));
}

function artifact(name) { return path.join(artifactDir, `${key}-${name}.png`); }
function baseName(file) { return file ? path.basename(file) : null; }
function isBlockedHost(host) {
  const value = host.replace(/^\[|\]$/g, '').toLowerCase();
  if (value === 'localhost' || value.endsWith('.localhost') || value.endsWith('.local')) return true;
  if (/^127\./.test(value) || /^10\./.test(value) || /^169\.254\./.test(value) || /^192\.168\./.test(value)) return true;
  const match = value.match(/^172\.(\d+)\./);
  if (match && Number(match[1]) >= 16 && Number(match[1]) <= 31) return true;
  return value === '::1' || value === '::' || /^f[cd][0-9a-f]*:/.test(value) || /^fe[89ab][0-9a-f]*:/.test(value);
}
async function protectNetwork(context) {
  await context.route('**/*', async route => {
    try {
      const requestUrl = new URL(route.request().url());
      if (!['http:', 'https:'].includes(requestUrl.protocol) || isBlockedHost(requestUrl.hostname)) return await route.abort('blockedbyclient');
      await route.continue();
    } catch { await route.abort('blockedbyclient'); }
  });
}

async function lighthouseScores(chromePath) {
  let chrome;
  const profile = path.join(artifactDir, `${key}-lighthouse-profile`);
  try {
    fs.rmSync(profile, { recursive: true, force: true, maxRetries: 3 });
    fs.mkdirSync(profile, { recursive: true });
    chrome = await chromeLauncher.launch({
      chromePath,
      userDataDir: profile,
      chromeFlags: ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
    });
    const result = await lighthouse(url, {
      port: chrome.port,
      output: 'json',
      logLevel: 'silent',
      onlyCategories: ['performance', 'accessibility', 'best-practices', 'seo']
    });
    const categories = result?.lhr?.categories || {};
    return Object.fromEntries(Object.entries(categories).map(([name, category]) => [name, Math.round((category.score || 0) * 100)]));
  } finally {
    if (chrome) await chrome.kill();
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); } catch { }
  }
}

function compareOrCreateBaseline(current, baseline, diff, update) {
  const existed = fs.existsSync(baseline);
  if (!existed || update) {
    fs.copyFileSync(current, baseline);
    if (fs.existsSync(diff)) fs.rmSync(diff);
    return { baselineCreated: !existed, baselineUpdated: existed && update, differencePercent: 0 };
  }
  const before = PNG.sync.read(fs.readFileSync(baseline));
  const after = PNG.sync.read(fs.readFileSync(current));
  if (before.width !== after.width || before.height !== after.height) {
    fs.copyFileSync(current, diff);
    return { baselineCreated: false, baselineUpdated: false, differencePercent: 100 };
  }
  const output = new PNG({ width: before.width, height: before.height });
  const changed = pixelmatch(before.data, after.data, output.data, before.width, before.height, { threshold: 0.1 });
  fs.writeFileSync(diff, PNG.sync.write(output));
  return { baselineCreated: false, baselineUpdated: false, differencePercent: Number((changed * 100 / (before.width * before.height)).toFixed(3)) };
}

async function run() {
  if (!url) throw new Error('A target URL is required.');
  const chromePath = findChromium(value('--chromium'));
  if (!chromePath) throw new Error('Chromium or Chrome was not found.');
  fs.mkdirSync(artifactDir, { recursive: true });
  const started = Date.now();
  const report = {
    success: false,
    scores: {},
    accessibilityViolations: [],
    journey: [],
    consoleErrors: [],
    failedRequests: [],
    visual: {},
    durationMs: 0
  };

  try { report.scores = await lighthouseScores(chromePath); }
  catch (error) { report.journey.push(`Lighthouse unavailable: ${error.message}`); }

  const browser = await chromium.launch({ executablePath: chromePath, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu'] });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'th-TH', ignoreHTTPSErrors: false });
    await protectNetwork(context);
    const page = await context.newPage();
    page.on('console', message => { if (message.type() === 'error' && report.consoleErrors.length < 30) report.consoleErrors.push(message.text().slice(0, 400)); });
    page.on('pageerror', error => { if (report.consoleErrors.length < 30) report.consoleErrors.push(error.message.slice(0, 400)); });
    page.on('requestfailed', request => { if (report.failedRequests.length < 30) report.failedRequests.push(`${request.method()} ${request.url()} — ${request.failure()?.errorText || 'failed'}`); });
    page.on('response', response => { if (response.status() >= 400 && report.failedRequests.length < 30) report.failedRequests.push(`${response.status()} ${response.url()}`); });
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
    try { await page.waitForLoadState('networkidle', { timeout: 10000 }); } catch { }
    report.journey.push('Page loaded in Chromium.');

    const search = page.locator('#search-input, input[type="search"]').first();
    if (await search.count()) {
      await search.fill('ESP32'); await page.waitForTimeout(500);
      report.journey.push('Search interaction completed.');
      await search.fill('');
    }
    for (const selector of ['#btn-sensor', '#btn-video', '#btn-mcu']) {
      const control = page.locator(selector);
      if (await control.count()) { await control.click(); await page.waitForTimeout(250); report.journey.push(`Clicked ${selector}.`); }
    }
    const theme = page.locator('#theme-toggle').first();
    if (await theme.count()) { await theme.click(); report.journey.push('Theme toggle completed.'); }
    const firstCard = page.locator('.card').first();
    if (await firstCard.count()) {
      await firstCard.click(); await page.waitForTimeout(300);
      const modal = page.locator('.modal-overlay.active, [role="dialog"]:visible').first();
      report.journey.push(await modal.count() ? 'Detail modal opened.' : 'First content card was clicked.');
      await page.keyboard.press('Escape');
    }

    const axePath = require.resolve('axe-core/axe.min.js');
    await page.addScriptTag({ path: axePath });
    const axe = await page.evaluate(async () => await globalThis.axe.run(document, { resultTypes: ['violations'] }));
    report.accessibilityViolations = axe.violations.slice(0, 30).map(item => ({
      id: item.id,
      impact: item.impact || 'unknown',
      description: item.description,
      nodes: item.nodes.length
    }));

    const desktopCurrent = artifact('desktop-current');
    const desktopBaseline = artifact('desktop-baseline');
    const desktopDiff = artifact('desktop-diff');
    await page.screenshot({ path: desktopCurrent, fullPage: true });
    const visual = compareOrCreateBaseline(desktopCurrent, desktopBaseline, desktopDiff, updateBaseline);

    const mobile = await browser.newContext({ ...devices['Pixel 5'], locale: 'th-TH' });
    await protectNetwork(mobile);
    const mobilePage = await mobile.newPage();
    await mobilePage.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
    try { await mobilePage.waitForLoadState('networkidle', { timeout: 10000 }); } catch { }
    const mobileCurrent = artifact('mobile-current');
    await mobilePage.screenshot({ path: mobileCurrent, fullPage: true });
    await mobile.close();

    report.visual = {
      ...visual,
      desktopCurrent: baseName(desktopCurrent),
      desktopBaseline: baseName(desktopBaseline),
      desktopDiff: fs.existsSync(desktopDiff) ? baseName(desktopDiff) : null,
      mobileCurrent: baseName(mobileCurrent)
    };
    await context.close();
    report.success = true;
  } finally {
    await browser.close();
  }
  report.durationMs = Date.now() - started;
  return report;
}

try {
  console.log(JSON.stringify(await run()));
} catch (error) {
  console.error(error?.stack || error?.message || String(error));
  console.log(JSON.stringify({ success: false, error: error?.message || 'Browser audit failed.', durationMs: 0 }));
  process.exitCode = 1;
}
