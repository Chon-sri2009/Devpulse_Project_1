import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const workspaceDir = "D:/VS_Project/MiniProject_Everything_1";
const SKILL_DIR = "C:/Users/Admin/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.11809/skills/presentations";
const TMP_DIR = path.join(workspaceDir, ".artifact-build/deck");
const FINAL_PPTX = path.join(workspaceDir, "deliverables/DevPulse_V1_Presentation_With_Credits.pptx");
const RUNTIME_PYTHON = "C:/Users/Admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe";
const screenshots = path.join(workspaceDir, ".artifact-build/screenshots");

const { makeNativeBulletParagraphs, finalizePresentation } = await import(
  pathToFileURL(path.join(SKILL_DIR, "container_tools/artifact_tool_utils.mjs")).href,
);

await fs.mkdir(TMP_DIR, { recursive: true });
await fs.mkdir(path.dirname(FINAL_PPTX), { recursive: true });

const deck = Presentation.create({ slideSize: { width: 1280, height: 720 } });
const FONT = "Arial";
const C = {
  navy: "#111C34", blue: "#3976ED", blue2: "#6D9BFF", ink: "#172033",
  muted: "#5F6F8C", pale: "#F3F6FB", line: "#D8E1EE", white: "#FFFFFF",
  green: "#17965A", amber: "#D4861A", softBlue: "#EAF1FF", softGreen: "#E8F6EE",
};

function rect(slide, x, y, w, h, fill, radius = 0, line = "none") {
  return slide.shapes.add({
    geometry: radius ? "roundRect" : "rect",
    position: { left: x, top: y, width: w, height: h },
    fill,
    line: line === "none" ? { fill: "none", width: 0 } : { fill: line, width: 1 },
    ...(radius ? { borderRadius: radius } : {}),
  });
}

function text(slide, value, x, y, w, h, size = 24, color = C.ink, bold = false, align = "left") {
  const shape = slide.shapes.add({
    geometry: "textbox",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  shape.text = value;
  shape.text.style = {
    typeface: FONT, fontSize: size, color, bold, alignment: align,
    verticalAlignment: "top", autoFit: "shrinkText", insets: { left: 0, right: 0, top: 0, bottom: 0 },
  };
  return shape;
}

function title(slide, value, section) {
  if (section) text(slide, section.toUpperCase(), 72, 42, 1120, 24, 15, C.blue, true);
  text(slide, value, 72, 72, 1136, 58, 43, C.ink, true);
}

function footer(slide, number) {
  text(slide, "DevPulse Version 1", 72, 682, 300, 18, 12, C.muted);
  text(slide, String(number + 1).padStart(2, "0"), 1160, 682, 48, 18, 12, C.muted, true, "right");
}

function bullets(slide, items, x, y, w, h, size = 22, color = C.ink) {
  const shape = slide.shapes.add({
    geometry: "textbox", position: { left: x, top: y, width: w, height: h },
    fill: "none", line: { fill: "none", width: 0 },
  });
  shape.text = makeNativeBulletParagraphs(items, { marginLeftPoints: 19, hangingPoints: 9, spaceAfterPoints: 10 });
  shape.text.style = { typeface: FONT, fontSize: size, color, autoFit: "shrinkText", insets: { left: 0, right: 0, top: 0, bottom: 0 } };
  return shape;
}

async function image(slide, filename, x, y, w, h, alt, fit = "cover", crop) {
  const bytes = new Uint8Array(await fs.readFile(path.join(screenshots, filename)));
  return slide.images.add({
    blob: bytes, contentType: "image/png", alt, fit,
    position: { left: x, top: y, width: w, height: h },
    geometry: "roundRect", borderRadius: 16,
    ...(crop ? { crop } : {}),
  });
}

function notes(slide, body) {
  slide.speakerNotes.textFrame.setText(body);
}

// 1. Cover
{
  const slide = deck.slides.add();
  slide.background.fill = C.navy;
  rect(slide, 0, 0, 24, 720, C.blue);
  text(slide, "DP", 82, 74, 64, 64, 25, C.white, true, "center");
  text(slide, "DevPulse", 82, 196, 1040, 96, 72, C.white, true);
  text(slide, "Version 1 project presentation", 84, 300, 820, 42, 29, C.blue2, true);
  text(slide, "IT diagnostics, testing, and developer focus in one web workspace", 84, 372, 830, 88, 29, "#DCE6F8");
  text(slide, "Built with .NET 9 and Blazor Server", 84, 612, 520, 26, 18, "#9FB1CF");
  rect(slide, 1014, 518, 148, 148, C.blue, 30);
  text(slide, "V1", 1014, 550, 148, 70, 48, C.white, true, "center");
  notes(slide, "Open by introducing DevPulse as a personal IT operations workspace. Version 1 combines system diagnostics, network tools, website testing, API checks, and a developer wellness area. Source: project README and running application.");
}

// 2. Credits
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Project creators", "Credits");
  text(slide, "DevPulse Version 1", 72, 148, 520, 34, 25, C.muted);

  text(slide, "Student team", 72, 220, 520, 40, 30, C.ink, true);
  text(slide, "Student 1", 72, 294, 160, 28, 18, C.blue, true);
  text(slide, "________________________________", 248, 286, 430, 34, 25, C.ink);
  text(slide, "Student 2", 72, 372, 160, 28, 18, C.blue, true);
  text(slide, "________________________________", 248, 364, 430, 34, 25, C.ink);
  text(slide, "Student 3", 72, 450, 160, 28, 18, C.blue, true);
  text(slide, "________________________________", 248, 442, 430, 34, 25, C.ink);

  text(slide, "Project advisor", 778, 220, 390, 40, 30, C.ink, true);
  text(slide, "Advisor", 778, 294, 130, 28, 18, C.green, true);
  text(slide, "________________________", 778, 338, 390, 34, 25, C.ink);
  text(slide, "Replace each blank line with the correct name before presenting.", 778, 420, 390, 70, 19, C.muted);
  footer(slide, 1);
  notes(slide, "Replace the three student blanks and the advisor blank before presenting. In PowerPoint, open slide 2, click a blank line, and type the name over the underscores.");
}

// 2. Goal
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "The project goal", "Purpose");
  text(slide, "Developers often switch between operating system tools, network calculators, API clients, website auditors, and music applications.", 72, 168, 470, 150, 28, C.ink, true);
  text(slide, "DevPulse brings these tasks into one controlled browser workspace. The application emphasizes bounded tests, clear ownership checks, and results that are easy to demonstrate.", 72, 345, 470, 160, 23, C.muted);
  await image(slide, "overview.png", 594, 154, 614, 454, "DevPulse operations overview screenshot", "cover", { left: 0.12, top: 0, right: 0, bottom: 0.02 });
  text(slide, "One workspace for day-to-day IT checks", 594, 626, 614, 26, 18, C.blue, true);
  footer(slide, 2);
  notes(slide, "Frame the problem as tool switching and fragmented evidence. DevPulse does not replace enterprise monitoring. It gives students, developers, and small teams one place to run bounded checks and understand what the server sees.");
}

// 3. Scope
{
  const slide = deck.slides.add(); slide.background.fill = C.pale;
  title(slide, "Version 1 scope", "Product");
  const rows = [
    ["OBSERVE", "Host metrics, storage, processes, telemetry, incidents", C.blue],
    ["TEST", "Ping, DNS, TLS, ports, databases, websites, APIs", C.green],
    ["INSPECT", "JSON, JWT, SHA-256, encrypted files, DNS email policy", "#7C4DCC"],
    ["PLAN", "IPv4, IPv6, VLSM, routes, bandwidth, MTU and MSS", C.amber],
    ["FOCUS", "Timer, breathing, ambient sound, reflection, Spotify", "#13795B"],
  ];
  let y = 154;
  for (const [label, detail, color] of rows) {
    rect(slide, 72, y, 10, 78, color, 5);
    text(slide, label, 108, y + 8, 165, 28, 18, color, true);
    text(slide, detail, 278, y + 5, 870, 48, 27, C.ink, false);
    y += 94;
  }
  text(slide, "The same navigation supports technical demonstrations and daily use.", 72, 636, 980, 28, 19, C.muted);
  footer(slide, 3);
  notes(slide, "Use this slide as the feature map. Avoid reading every item. Pick one example from each row, then transition into the architecture and live screens.");
}

// 4. Architecture
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Application architecture", "Design");
  const browser = rect(slide, 74, 206, 240, 154, C.softBlue, 18, "#AFC6F5");
  text(slide, "Browser", 98, 230, 190, 34, 28, C.ink, true, "center");
  text(slide, "Interactive Blazor circuit\nUser actions and live updates", 98, 280, 190, 58, 18, C.muted, false, "center");
  const app = rect(slide, 454, 168, 350, 230, C.navy, 22);
  text(slide, ".NET 9 Blazor Server", 484, 202, 290, 40, 30, C.white, true, "center");
  text(slide, "Razor components\nApplication services\nSafety validation", 490, 272, 280, 90, 22, "#DCE6F8", false, "center");
  const sources = rect(slide, 944, 114, 262, 180, C.softGreen, 18, "#B7DDC8");
  text(slide, "System and storage", 970, 144, 210, 34, 25, C.ink, true, "center");
  text(slide, "Runtime metrics\nProcesses and files", 970, 200, 210, 56, 18, C.muted, false, "center");
  const external = rect(slide, 944, 358, 262, 180, "#FFF5E6", 18, "#E8C991");
  text(slide, "Approved services", 970, 388, 210, 34, 25, C.ink, true, "center");
  text(slide, "Websites, APIs, DNS\nSpotify and OTLP", 970, 444, 210, 56, 18, C.muted, false, "center");
  slide.shapes.connect(browser, app, { kind: "straight", fromSide: "right", toSide: "left", line: { style: "solid", fill: C.blue, width: 3 }, tail: { type: "triangle", width: "sm", length: "sm" } });
  slide.shapes.connect(app, sources, { kind: "elbow", fromSide: "right", toSide: "left", line: { style: "solid", fill: C.green, width: 3 }, tail: { type: "triangle", width: "sm", length: "sm" } });
  slide.shapes.connect(app, external, { kind: "elbow", fromSide: "right", toSide: "left", line: { style: "solid", fill: C.amber, width: 3 }, tail: { type: "triangle", width: "sm", length: "sm" } });
  text(slide, "Encrypted session state and bounded telemetry journals use the configured data directory.", 394, 514, 470, 64, 20, C.muted, false, "center");
  footer(slide, 4);
  notes(slide, "Explain that Blazor Server executes diagnostics on the machine hosting DevPulse. A Render deployment therefore reports Render's container, not the visitor's computer. External requests pass through validation and bounded HTTP clients. Sources: Program.cs, PublicHttpTarget.cs, TelemetryService.cs.");
}

// 5. Operations
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Operations and telemetry", "Observe");
  await image(slide, "operations.png", 72, 152, 548, 388, "Service operations screenshot", "cover", { left: 0.13, top: 0.06, right: 0, bottom: 0.08 });
  await image(slide, "telemetry.png", 660, 152, 548, 388, "Telemetry explorer screenshot", "cover", { left: 0.13, top: 0.06, right: 0, bottom: 0.08 });
  text(slide, "Bounded checks", 72, 564, 220, 28, 22, C.blue, true);
  text(slide, "HTTP, TCP, ping, DNS, TLS, database connectivity, and approved log files", 72, 598, 500, 54, 18, C.muted);
  text(slide, "Evidence over time", 660, 564, 220, 28, 22, C.green, true);
  text(slide, "Runtime metrics, request traces, deployment identity, threshold incidents, and optional OTLP export", 660, 598, 530, 54, 18, C.muted);
  footer(slide, 5);
  notes(slide, "Demonstrate that Operations answers whether a service can be reached now, while Telemetry preserves evidence about application behavior over time. Clarify that private addresses need explicit owner configuration.");
}

// 6. Planning and inspection
{
  const slide = deck.slides.add(); slide.background.fill = C.pale;
  title(slide, "Network planning and local inspectors", "Plan and inspect");
  await image(slide, "network-calculator.png", 72, 152, 660, 470, "Network calculator screenshot", "cover", { left: 0.12, top: 0.02, right: 0, bottom: 0.04 });
  text(slide, "Network calculations", 782, 164, 390, 34, 27, C.ink, true);
  bullets(slide, ["IPv4 and IPv6 subnet details", "VLSM allocation and route summaries", "Bandwidth, transfer time, MTU, and MSS"], 782, 214, 400, 170, 21);
  text(slide, "Local data inspection", 782, 412, 390, 34, 27, C.ink, true);
  bullets(slide, ["JSON tree and JWT payload decoding", "SHA-256 integrity checks", "AES-256-GCM file encryption and decryption"], 782, 462, 400, 170, 21);
  footer(slide, 6);
  notes(slide, "The calculator performs local arithmetic and contacts no target. Uploaded inspector files stay in memory and are not stored. SHA-256 proves integrity but cannot be decoded, while AES encryption can be reversed only with the password.");
}

// 7. Website and API assurance
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Website and API assurance", "Test");
  await image(slide, "website-audit.png", 72, 150, 640, 330, "Website audit screenshot", "cover", { left: 0.12, top: 0.03, right: 0, bottom: 0.05 });
  await image(slide, "api-runner.png", 752, 150, 456, 330, "API collection runner screenshot", "cover", { left: 0.15, top: 0.02, right: 0, bottom: 0.04 });
  text(slide, "Website Audit", 72, 512, 250, 30, 23, C.blue, true);
  text(slide, "Crawl checks, headers, JSON validation, Lighthouse, accessibility, screenshots, visual baselines, and scheduled uptime", 72, 550, 600, 72, 19, C.muted);
  text(slide, "API and DNS", 752, 512, 250, 30, 23, C.green, true);
  text(slide, "Temporary request collections, assertions, response previews, and public DNS email-policy inspection", 752, 550, 430, 72, 19, C.muted);
  footer(slide, 7);
  notes(slide, "The Website Audit combines a bounded crawler with an optional browser worker. The API Runner supports read-only public requests and restricts write methods to exact approved URLs. The DNS inspector checks MX, SPF, DMARC, DKIM, CAA, and addressing records.");
}

// 8. Focus
{
  const slide = deck.slides.add(); slide.background.fill = "#F4FAF6";
  title(slide, "Focus Lounge and Spotify", "Developer wellness");
  await image(slide, "focus-lounge.png", 72, 148, 730, 500, "Focus Lounge screenshot", "cover", { left: 0.12, top: 0.01, right: 0, bottom: 0.03 });
  text(slide, "A quieter workspace", 850, 170, 330, 38, 30, "#124B32", true);
  bullets(slide, ["Focus and break timer", "Guided box breathing", "Generated ambient sound", "Private browser-local notes", "Spotify Connect and optional browser playback"], 850, 238, 330, 280, 22, "#244D3B");
  text(slide, "Spotify credentials remain server-side. OAuth sessions use encrypted storage and opaque cookies.", 850, 548, 330, 76, 19, "#4D6B5B");
  footer(slide, 8);
  notes(slide, "This module gives the presentation a human side. Explain that Spotify playback can remain on the user's phone or computer through Spotify Connect. Browser playback requires Premium and an explicit user action.");
}

// 9. Security
{
  const slide = deck.slides.add(); slide.background.fill = C.navy;
  text(slide, "SECURITY", 72, 42, 260, 24, 15, C.blue2, true);
  text(slide, "Safety boundaries in Version 1", 72, 72, 1136, 58, 43, C.white, true);
  const left = rect(slide, 72, 176, 350, 360, "#172846", 20, "#2B4168");
  text(slide, "Network controls", 104, 208, 286, 34, 27, C.white, true);
  bullets(slide, ["Private and reserved targets blocked by default", "Redirects disabled for diagnostic clients", "Owner allowlists permit intentional private checks", "Strict request, response, timeout, and concurrency caps"], 104, 270, 280, 220, 20, "#DCE6F8");
  const mid = rect(slide, 466, 176, 350, 360, "#172846", 20, "#2B4168");
  text(slide, "Secrets and sessions", 498, 208, 286, 34, 27, C.white, true);
  bullets(slide, ["Environment variables or external secret files", "Encrypted Spotify token storage", "No credentials committed to appsettings.json", "Authorization values stay transient in the API Runner"], 498, 270, 280, 220, 20, "#DCE6F8");
  const right = rect(slide, 860, 176, 348, 360, "#172846", 20, "#2B4168");
  text(slide, "Administrative actions", 892, 208, 284, 34, 27, C.white, true);
  bullets(slide, ["Separate administrator authentication", "Rate limits and antiforgery protection", "Process termination disabled unless explicitly enabled", "Production diagnostics can be hidden completely"], 892, 270, 280, 220, 20, "#DCE6F8");
  text(slide, "Public deployments should place diagnostic pages behind authenticated access.", 72, 588, 1136, 38, 24, "#FFCB76", true, "center");
  footer(slide, 9);
  notes(slide, "Version 1 has strong request-level boundaries, but the current public Render deployment exposes diagnostic pages anonymously. Before a public launch, require administrator authentication or a private access gateway. This also supports the ongoing Google Safe Browsing review.");
}

// 10. Deployment
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Deployment and configuration", "Operate");
  const dev = rect(slide, 72, 190, 250, 132, C.softBlue, 18, "#AFC6F5");
  text(slide, "Local development", 96, 215, 202, 30, 24, C.ink, true, "center");
  text(slide, ".NET 9 SDK\nUser Secrets", 96, 262, 202, 45, 18, C.muted, false, "center");
  const imageNode = rect(slide, 515, 190, 250, 132, "#EEF0F6", 18, "#C7CFDD");
  text(slide, "Docker image", 539, 215, 202, 30, 24, C.ink, true, "center");
  text(slide, "Published application\nNon-root container user", 539, 262, 202, 45, 18, C.muted, false, "center");
  const render = rect(slide, 958, 190, 250, 132, C.softGreen, 18, "#B7DDC8");
  text(slide, "Render service", 982, 215, 202, 30, 24, C.ink, true, "center");
  text(slide, "HTTPS proxy\nPersistent data volume", 982, 262, 202, 45, 18, C.muted, false, "center");
  slide.shapes.connect(dev, imageNode, { kind: "straight", fromSide: "right", toSide: "left", line: { style: "solid", fill: C.blue, width: 3 }, tail: { type: "triangle", width: "sm", length: "sm" } });
  slide.shapes.connect(imageNode, render, { kind: "straight", fromSide: "right", toSide: "left", line: { style: "solid", fill: C.green, width: 3 }, tail: { type: "triangle", width: "sm", length: "sm" } });
  text(slide, "Configuration that must stay outside Git", 72, 400, 520, 34, 27, C.ink, true);
  bullets(slide, ["Spotify client ID and secret", "Administrator password", "SMTP credentials and webhook destinations", "External secrets file and persistent encryption keys"], 72, 454, 510, 170, 20);
  text(slide, "Deployment checks", 690, 400, 420, 34, 27, C.ink, true);
  bullets(slide, ["Health endpoint at /healthz", "Correct Spotify callback URL", "Trusted reverse proxy configuration", "Diagnostics exposure reviewed before launch"], 690, 454, 480, 170, 20);
  footer(slide, 10);
  notes(slide, "Local credentials use .NET User Secrets. Render credentials use environment variables or a mounted secret file. Persistent storage keeps data-protection keys and encrypted sessions across container replacement.");
}

// 11. Verification
{
  const slide = deck.slides.add(); slide.background.fill = C.pale;
  title(slide, "Verification and release status", "Quality");
  text(slide, "79 / 79", 76, 180, 360, 90, 72, C.green, true);
  text(slide, "automated regression checks passed", 80, 276, 380, 34, 25, C.ink, true);
  text(slide, "Coverage includes Spotify parsing and sessions, cryptography, network safety, website audit behavior, telemetry persistence, API boundaries, and DNS email-policy analysis.", 80, 338, 470, 136, 21, C.muted);
  rect(slide, 630, 166, 520, 74, C.white, 14, C.line);
  text(slide, "Release build", 658, 185, 250, 28, 22, C.ink, true);
  text(slide, "0 warnings, 0 errors", 910, 185, 210, 28, 21, C.green, true, "right");
  rect(slide, 630, 260, 520, 74, C.white, 14, C.line);
  text(slide, "Code formatting", 658, 279, 250, 28, 22, C.ink, true);
  text(slide, "Verified", 910, 279, 210, 28, 21, C.green, true, "right");
  rect(slide, 630, 354, 520, 74, C.white, 14, C.line);
  text(slide, "Published HTTP smoke suite", 658, 373, 300, 28, 22, C.ink, true);
  text(slide, "Passed", 998, 373, 122, 28, 21, C.green, true, "right");
  rect(slide, 630, 470, 520, 112, "#FFF5E6", 14, "#E8C991");
  text(slide, "Public launch checkpoint", 658, 490, 360, 28, 22, C.amber, true);
  text(slide, "Complete Google Search Console review and protect public diagnostic pages before broad promotion.", 658, 528, 450, 48, 18, "#7A551B");
  footer(slide, 11);
  notes(slide, "The test count reflects the custom regression executable run for this release. The build, formatter, publish, and production HTTP smoke checks also passed. The Chrome Safe Browsing warning remains a launch blocker until Google identifies or clears the reported issue.");
}

// 12. Demo and roadmap
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Presentation route and Version 2", "Next steps");
  text(slide, "Suggested live demonstration", 72, 166, 500, 36, 29, C.ink, true);
  const demo = ["Overview", "Network Calculator", "Website Audit", "API Runner", "Focus Lounge"];
  let y = 226;
  demo.forEach((item, index) => {
    text(slide, String(index + 1).padStart(2, "0"), 78, y, 52, 30, 18, C.blue, true);
    text(slide, item, 144, y - 2, 350, 34, 25, C.ink, index === 0);
    y += 64;
  });
  rect(slide, 594, 150, 614, 440, C.navy, 24);
  text(slide, "Version 2 candidates", 632, 186, 510, 40, 31, C.white, true);
  bullets(slide, ["SQL Server health and performance monitor", "IT asset inventory with IP address management", "Authenticated remote monitoring agent", "Incident response and backup verification", "Role-based access for every diagnostic tool"], 632, 258, 510, 262, 23, "#DCE6F8");
  text(slide, "Version 1 establishes the platform and the safety model needed for these additions.", 632, 532, 510, 44, 19, C.blue2);
  text(slide, "DevPulse Version 1", 72, 622, 470, 42, 31, C.blue, true);
  footer(slide, 12);
  notes(slide, "Close with a short live demonstration. The roadmap extends DevPulse from a single-host diagnostics workspace into a small IT operations platform. Do not promise a delivery date for Version 2 until scope and access controls are agreed.");
}

const requirements = {
  explicitTotalSlideCount: 13,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
};
const fontPolicy = { basis: "design", families: [FONT] };
const stagingDir = path.join(workspaceDir, ".artifact-build/deck-finalizer-credits");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "candidate.pptx");
await (await PresentationFile.exportPptx(deck)).save(candidatePath);

await finalizePresentation({
  ...requirements,
  workspaceDir,
  candidatePath,
  finalPath: FINAL_PPTX,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools/inspect_presentation_layout_geometry.py"),
  layoutArgs: ["--expected-slide-size-emu", "12192000,6858000", "--validate-bullet-geometry", "--validate-heading-fit"],
  requiredNativeTableOwnerSlides: [],
  fontPolicy,
  verifyArtifactToolImport: true,
  receiptPath: path.join(stagingDir, "DevPulse_V1_Presentation_With_Credits.validation.json"),
});

console.log(FINAL_PPTX);
