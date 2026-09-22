import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const workspaceDir = "D:/VS_Project/MiniProject_Everything_1";
const SKILL_DIR = "C:/Users/Admin/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.11809/skills/presentations";
const TMP_DIR = path.join(workspaceDir, ".artifact-build/deck");
const FINAL_PPTX = path.join(workspaceDir, "deliverables/DevPulse_V1_Team_3_Devils_Bilingual_Presentation.pptx");
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

function bilingualNotes(slide, presenter, english, thai, reference = "") {
  notes(slide, [
    `PRESENTER / ผู้นำเสนอ: ${presenter}`,
    "",
    "ENGLISH SCRIPT",
    english,
    "",
    "บทพูดภาษาไทย",
    thai,
    ...(reference ? ["", `REFERENCE / ข้อมูลอ้างอิง: ${reference}`] : []),
  ].join("\n"));
}

async function portrait(slide, sourcePath, x, y, w, h, alt) {
  const bytes = new Uint8Array(await fs.readFile(sourcePath));
  return slide.images.add({
    blob: bytes,
    contentType: "image/jpeg",
    alt,
    fit: "cover",
    position: { left: x, top: y, width: w, height: h },
    geometry: "roundRect",
    borderRadius: 22,
  });
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
  bilingualNotes(
    slide,
    "Mr. Chonlapol Srichayech",
    "Good morning. We are Team 3 Devils, and this is DevPulse Version 1. DevPulse is a web workspace for IT diagnostics, website and API testing, network planning, and developer focus. I am Chonlapol Srichayech, the project creator and lead developer. The application uses .NET 9 and Blazor Server. Today our team will explain the problem, architecture, main features, safety boundaries, deployment, and release results.",
    "สวัสดีครับ พวกเราคือทีม 3 Devils และนี่คือโครงการ DevPulse เวอร์ชัน 1 DevPulse เป็นเว็บสำหรับงานตรวจสอบระบบไอที การทดสอบเว็บไซต์และ API การวางแผนเครือข่าย และเครื่องมือช่วยในการทำงาน ผมชื่อชลพล ศรีชเยศ เป็นผู้สร้างโครงการและผู้พัฒนาหลัก ระบบนี้พัฒนาด้วย .NET 9 และ Blazor Server วันนี้พวกเราจะนำเสนอปัญหาที่ต้องการแก้ไข สถาปัตยกรรม ฟีเจอร์หลัก ขอบเขตความปลอดภัย การนำระบบขึ้นใช้งาน และผลการทดสอบครับ",
    "Project README and running application"
  );
}

// 2. Credits
{
  const slide = deck.slides.add(); slide.background.fill = C.white;
  title(slide, "Team 3 Devils", "Project credits");
  text(slide, "PROJECT CREATOR AND LEAD", 72, 154, 620, 24, 14, C.blue, true);
  text(slide, "Mr. Chonlapol Srichayech", 72, 184, 650, 50, 36, C.ink, true);
  text(slide, "Design, development, testing, and documentation", 72, 238, 650, 30, 20, C.muted);

  text(slide, "PRESENTATION TEAM", 72, 310, 620, 24, 14, C.green, true);
  text(slide, "Miss Kanchanit Klanpram", 72, 342, 650, 32, 24, C.ink, true);
  text(slide, "Miss Natthachaporn Anekchaiyaporn", 72, 382, 650, 32, 24, C.ink, true);

  text(slide, "PROJECT ADVISOR", 72, 448, 620, 24, 14, C.amber, true);
  text(slide, "Mr. Suppakit Kongthong", 72, 480, 650, 32, 24, C.ink, true);

  rect(slide, 72, 548, 660, 78, C.softBlue, 14, "#C9D9F7");
  text(slide, "Slides 1–5  Chonlapol    Slides 6–9  Kanchanit    Slides 10–13  Natthachaporn", 94, 568, 616, 38, 17, C.ink, true);

  await portrait(
    slide,
    path.join(workspaceDir, "wwwroot/images/chonlapol-srichayech.jpg"),
    806, 126, 402, 522,
    "Mr. Chonlapol Srichayech holding a medal beside a WorldSkills Thailand flag"
  );
  footer(slide, 1);
  bilingualNotes(
    slide,
    "Mr. Chonlapol Srichayech",
    "Our group is Team 3 Devils. I completed the project design, development, testing, and documentation. Miss Kanchanit Klanpram and Miss Natthachaporn Anekchaiyaporn are the presentation team, and our advisor is Mr. Suppakit Kongthong. I will present the introduction through the architecture. Kanchanit will explain the main user features. Natthachaporn will cover security, deployment, quality, and the conclusion.",
    "กลุ่มของพวกเราชื่อทีม 3 Devils ครับ ผมรับผิดชอบการออกแบบ พัฒนา ทดสอบ และจัดทำเอกสารของโครงการ คุณกัญจนิจ กลั่นพรหม และคุณณัฐชพร เอนกชัยพร เป็นทีมผู้นำเสนอ และอาจารย์ที่ปรึกษาของเราคืออาจารย์ศุภกิจ คงทอง ผมจะนำเสนอตั้งแต่บทนำจนถึงสถาปัตยกรรม จากนั้นคุณกัญจนิจจะอธิบายฟีเจอร์หลัก และคุณณัฐชพรจะนำเสนอเรื่องความปลอดภัย การติดตั้งใช้งาน คุณภาพ และบทสรุปครับ"
  );
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
  bilingualNotes(
    slide,
    "Mr. Chonlapol Srichayech",
    "Developers normally switch between many separate tools for system information, networks, APIs, website auditing, and music. This wastes time and separates the evidence needed to understand a problem. DevPulse brings these tasks into one controlled browser workspace. It does not replace an enterprise monitoring platform. Its purpose is to give students, developers, and small teams a clear place to run limited checks and understand what the DevPulse server can see.",
    "โดยปกตินักพัฒนาต้องสลับใช้หลายโปรแกรมเพื่อดูข้อมูลระบบ ตรวจสอบเครือข่าย ทดสอบ API ตรวจเว็บไซต์ และใช้งานเพลง ทำให้เสียเวลาและข้อมูลที่ต้องใช้วิเคราะห์ปัญหากระจัดกระจาย DevPulse จึงรวมงานเหล่านี้ไว้ในเว็บเดียวที่ควบคุมขอบเขตได้ ระบบนี้ไม่ได้สร้างมาแทนแพลตฟอร์มมอนิเตอร์ระดับองค์กร แต่ช่วยให้นักศึกษา นักพัฒนา และทีมขนาดเล็กตรวจสอบระบบได้อย่างชัดเจนและเข้าใจว่าเซิร์ฟเวอร์ที่รัน DevPulse มองเห็นอะไรบ้างครับ"
  );
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
  bilingualNotes(
    slide,
    "Mr. Chonlapol Srichayech",
    "Version 1 groups its functions into five areas. Observe shows host metrics and telemetry. Test checks approved networks, services, websites, and APIs. Inspect works with JSON, JWT, file hashes, encryption, and DNS email records. Plan calculates IPv4, IPv6, VLSM, routes, bandwidth, and MTU values. Focus provides timers, breathing tools, ambient sound, notes, and Spotify. These areas share one navigation system, which makes the project easier to demonstrate and use.",
    "DevPulse เวอร์ชัน 1 แบ่งความสามารถออกเป็นห้าส่วน ส่วน Observe ใช้ดูข้อมูลเครื่องและเทเลเมทรี ส่วน Test ใช้ตรวจเครือข่าย บริการ เว็บไซต์ และ API ที่ได้รับอนุญาต ส่วน Inspect ใช้ดู JSON, JWT, ค่าแฮชไฟล์ การเข้ารหัส และข้อมูล DNS สำหรับอีเมล ส่วน Plan ใช้คำนวณ IPv4, IPv6, VLSM เส้นทาง แบนด์วิดท์ และค่า MTU ส่วน Focus มีตัวจับเวลา การฝึกหายใจ เสียงบรรยากาศ บันทึกส่วนตัว และ Spotify ทุกส่วนใช้เมนูเดียวกัน จึงสาธิตและใช้งานได้สะดวกครับ"
  );
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
  bilingualNotes(
    slide,
    "Mr. Chonlapol Srichayech",
    "The browser displays Razor components and sends user actions through an interactive Blazor connection. The .NET 9 server performs the actual diagnostics, validation, and service calls. System information therefore belongs to the machine hosting DevPulse. When the project runs on Render, storage and process metrics describe the Render container, not the visitor's computer. Requests to websites, APIs, DNS, and Spotify pass through server-side validation and strict limits. I will now hand the presentation to Kanchanit for the main features.",
    "เบราว์เซอร์ทำหน้าที่แสดง Razor Component และส่งคำสั่งของผู้ใช้ผ่านการเชื่อมต่อแบบ Blazor ส่วนเซิร์ฟเวอร์ .NET 9 เป็นผู้ประมวลผลการตรวจสอบ การตรวจสอบความถูกต้อง และการเรียกใช้บริการต่าง ๆ ดังนั้นข้อมูลระบบจึงเป็นข้อมูลของเครื่องที่รัน DevPulse หากนำขึ้น Render ค่าพื้นที่จัดเก็บและโพรเซสจะเป็นของคอนเทนเนอร์ Render ไม่ใช่คอมพิวเตอร์ของผู้เข้าชม ส่วนคำขอไปยังเว็บไซต์ API DNS และ Spotify จะผ่านการตรวจสอบและจำกัดขอบเขตที่ฝั่งเซิร์ฟเวอร์ ต่อไปผมขอส่งให้คุณกัญจนิจนำเสนอฟีเจอร์หลักครับ",
    "Program.cs, PublicHttpTarget.cs, and TelemetryService.cs"
  );
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
  bilingualNotes(
    slide,
    "Miss Kanchanit Klanpram",
    "The Operations page answers a current question: can the DevPulse server reach this approved service now? It supports bounded HTTP, TCP, ping, DNS, TLS, database, and log-file checks. The Telemetry page answers a different question by preserving application evidence over time. It records runtime metrics, request traces, deployment identity, and threshold incidents. Private addresses remain blocked unless the owner explicitly adds them to the approved configuration.",
    "หน้า Operations ใช้ตอบคำถามว่า ในขณะนี้เซิร์ฟเวอร์ DevPulse สามารถเชื่อมต่อไปยังบริการที่ได้รับอนุญาตได้หรือไม่ โดยรองรับการตรวจ HTTP, TCP, Ping, DNS, TLS, ฐานข้อมูล และไฟล์ล็อกภายใต้ขอบเขตที่กำหนด ส่วนหน้า Telemetry ใช้เก็บหลักฐานการทำงานตามช่วงเวลา เช่น ค่าการทำงานของระบบ ประวัติคำขอ ข้อมูลการติดตั้ง และเหตุการณ์ที่เกินค่ากำหนด สำหรับ IP ภายในจะถูกบล็อกไว้ก่อน จนกว่าเจ้าของระบบจะเพิ่มลงในรายการที่อนุญาตค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Kanchanit Klanpram",
    "The Network Calculator performs local calculations without contacting another computer. It can calculate IPv4 and IPv6 ranges, VLSM allocations, route summaries, transfer time, MTU, and MSS. The inspector tools decode JSON and JWT data locally and calculate SHA-256 file hashes. SHA-256 confirms file integrity but cannot be decoded. The AES-256-GCM tool supports reversible encryption, but decryption requires the correct password. Uploaded files stay in memory instead of becoming stored user content.",
    "Network Calculator คำนวณข้อมูลภายในระบบโดยไม่ติดต่อไปยังเครื่องเป้าหมาย สามารถคำนวณช่วง IPv4 และ IPv6 วางแผน VLSM สรุปเส้นทาง คำนวณเวลาโอนข้อมูล ค่า MTU และ MSS ได้ ส่วนเครื่องมือตรวจสอบสามารถอ่าน JSON และ JWT ภายในระบบ รวมถึงคำนวณค่า SHA-256 ของไฟล์ ค่า SHA-256 ใช้ยืนยันความถูกต้องของไฟล์แต่ไม่สามารถถอดกลับได้ สำหรับ AES-256-GCM สามารถถอดรหัสได้เมื่อมีรหัสผ่านที่ถูกต้อง และไฟล์ที่อัปโหลดจะประมวลผลในหน่วยความจำโดยไม่เก็บเป็นข้อมูลผู้ใช้ค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Kanchanit Klanpram",
    "Website Audit checks a public website that the user owns or has permission to test. It reviews crawl results, response headers, JSON resources, accessibility, screenshots, visual baselines, and scheduled uptime. The optional browser worker adds Lighthouse-style evidence. API Runner creates temporary request collections with assertions and response previews. Public read requests are allowed, while write methods require an exact owner-approved URL. The DNS and Email Inspector checks addressing, MX, SPF, DMARC, DKIM, and CAA records.",
    "Website Audit ใช้ตรวจเว็บไซต์สาธารณะที่ผู้ใช้เป็นเจ้าของหรือได้รับอนุญาต โดยตรวจผลการเก็บข้อมูลส่วนต่าง ๆ ของเว็บ HTTP Header ไฟล์ JSON การเข้าถึงสำหรับผู้พิการ ภาพหน้าจอ ภาพเปรียบเทียบ และสถานะการออนไลน์ตามเวลา หากเปิดใช้ Browser Worker ระบบจะเพิ่มผลตรวจในลักษณะ Lighthouse ส่วน API Runner ใช้สร้างชุดคำขอชั่วคราวพร้อมเงื่อนไขตรวจสอบและตัวอย่างผลลัพธ์ คำขอแบบอ่านข้อมูลสาธารณะทำได้ทั่วไป แต่คำสั่งที่แก้ไขข้อมูลต้องตรงกับ URL ที่เจ้าของอนุญาตเท่านั้น นอกจากนี้ DNS และ Email Inspector ยังตรวจ Address, MX, SPF, DMARC, DKIM และ CAA ได้ค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Kanchanit Klanpram",
    "Focus Lounge supports the person using the technical tools. It includes a focus and break timer, guided box breathing, generated ambient sound, and a private note stored in the browser. Spotify Connect can keep playback on the user's active phone, computer, or speaker, so opening DevPulse does not have to interrupt the current device. Browser playback remains optional, requires Spotify Premium, and starts only after the user takes an explicit action. I will now hand the presentation to Natthachaporn for security and deployment.",
    "Focus Lounge เป็นส่วนที่ช่วยผู้ใช้ระหว่างทำงานด้านเทคนิค ภายในมีตัวจับเวลาสำหรับช่วงทำงานและพัก การฝึกหายใจแบบ Box Breathing เสียงบรรยากาศที่สร้างภายในเว็บ และบันทึกส่วนตัวที่เก็บไว้ในเบราว์เซอร์ Spotify Connect สามารถเล่นเพลงต่อบนโทรศัพท์ คอมพิวเตอร์ หรือลำโพงที่กำลังใช้งานอยู่ได้ จึงไม่จำเป็นต้องย้ายเสียงเข้ามาใน DevPulse ส่วนการเล่นเพลงในเบราว์เซอร์เป็นตัวเลือกเพิ่มเติม ต้องใช้ Spotify Premium และเริ่มได้เมื่อผู้ใช้กดยืนยันเท่านั้น ต่อไปขอส่งให้คุณณัฐชพรนำเสนอเรื่องความปลอดภัยและการติดตั้งใช้งานค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Natthachaporn Anekchaiyaporn",
    "DevPulse uses three main safety boundaries. Network controls block private and reserved targets by default, disable redirects, and limit request volume. Secrets stay in environment variables or external secret files, while Spotify sessions remain encrypted. Administrative actions require separate authentication, rate limiting, and antiforgery protection. Process termination stays disabled until the owner explicitly enables it. A public deployment should protect diagnostic pages with authenticated access before broad promotion.",
    "DevPulse มีขอบเขตความปลอดภัยหลักสามส่วน ส่วนแรกคือการควบคุมเครือข่าย โดยบล็อกปลายทางภายในและ Reserved Address เป็นค่าเริ่มต้น ปิดการ Redirect และจำกัดจำนวนคำขอ ส่วนที่สองคือการเก็บข้อมูลลับไว้ใน Environment Variable หรือไฟล์ลับภายนอก พร้อมเข้ารหัสเซสชัน Spotify ส่วนที่สามคือการควบคุมคำสั่งผู้ดูแลระบบด้วยการยืนยันตัวตน การจำกัดอัตราการใช้งาน และการป้องกันคำขอปลอม ฟังก์ชันปิดโพรเซสจะปิดไว้จนกว่าเจ้าของระบบจะเปิดใช้งาน และเว็บไซต์สาธารณะควรป้องกันหน้าตรวจสอบระบบด้วยการเข้าสู่ระบบค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Natthachaporn Anekchaiyaporn",
    "The deployment path starts with local .NET 9 development, continues through the Docker image, and ends at the Render service. Local credentials use .NET User Secrets. Render receives credentials through environment variables or a mounted secret file. No real secret should enter Git. Before release, we check the health endpoint, Spotify callback URL, trusted proxy configuration, persistent data storage, and public access to diagnostic pages. Persistent storage keeps encryption keys and protected sessions available after a container replacement.",
    "ขั้นตอนการนำระบบขึ้นใช้งานเริ่มจากการพัฒนาด้วย .NET 9 ในเครื่อง จากนั้นสร้าง Docker Image และนำขึ้น Render ข้อมูลลับในเครื่องใช้ .NET User Secrets ส่วนบน Render ใช้ Environment Variable หรือไฟล์ลับที่ Mount เข้ามา โดยต้องไม่บันทึกข้อมูลลับจริงลงใน Git ก่อนปล่อยระบบต้องตรวจ Health Endpoint, Spotify Callback URL, การตั้งค่า Trusted Proxy, พื้นที่จัดเก็บถาวร และสิทธิ์เข้าถึงหน้าตรวจสอบระบบ พื้นที่จัดเก็บถาวรช่วยรักษากุญแจเข้ารหัสและเซสชันที่ป้องกันไว้แม้คอนเทนเนอร์ถูกสร้างใหม่ค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Natthachaporn Anekchaiyaporn",
    "The Version 1 release passed all 79 automated regression checks. The Release build completed with zero warnings and zero errors. Code formatting and the published HTTP smoke suite also passed. The checks cover Spotify sessions, cryptography, network restrictions, website auditing, telemetry persistence, API boundaries, and DNS email analysis. One operational task remains before broad public promotion: complete the Google Search Console or Safe Browsing review and protect the diagnostic pages with authenticated access.",
    "DevPulse เวอร์ชัน 1 ผ่านการทดสอบอัตโนมัติทั้งหมด 79 รายการ การ Build แบบ Release สำเร็จโดยไม่มี Warning และ Error การตรวจรูปแบบโค้ดและการทดสอบ HTTP หลัง Publish ก็ผ่านเช่นกัน ขอบเขตการทดสอบครอบคลุมเซสชัน Spotify การเข้ารหัส ข้อจำกัดเครือข่าย การตรวจเว็บไซต์ การเก็บ Telemetry ขอบเขตของ API และการวิเคราะห์ DNS สำหรับอีเมล ก่อนประชาสัมพันธ์เว็บไซต์ในวงกว้าง ยังต้องตรวจสอบสถานะกับ Google Search Console หรือ Safe Browsing และป้องกันหน้าตรวจสอบระบบด้วยการยืนยันตัวตนค่ะ"
  );
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
  bilingualNotes(
    slide,
    "Miss Natthachaporn Anekchaiyaporn",
    "To conclude, Version 1 already provides a complete demonstration route through Overview, Network Calculator, Website Audit, API Runner, and Focus Lounge. Possible Version 2 work includes SQL Server monitoring, IT asset inventory, a secure remote monitoring agent, incident and backup workflows, and role-based access. These remain future candidates rather than promised delivery items. I will now hand back to Chonlapol for the live demonstration. Thank you for listening, and we welcome your questions after the demo.",
    "สรุปแล้ว DevPulse เวอร์ชัน 1 มีเส้นทางสาธิตที่ครบถ้วน ตั้งแต่ Overview, Network Calculator, Website Audit, API Runner และ Focus Lounge แนวคิดสำหรับเวอร์ชัน 2 ได้แก่ การมอนิเตอร์ SQL Server ระบบทะเบียนอุปกรณ์ไอที Agent สำหรับตรวจเครื่องระยะไกลอย่างปลอดภัย ระบบจัดการเหตุการณ์และการสำรองข้อมูล รวมถึงการแบ่งสิทธิ์ตามบทบาท ฟีเจอร์เหล่านี้เป็นแนวทางในอนาคตและยังไม่ได้กำหนดวันส่งมอบ ต่อไปขอส่งกลับให้คุณชลพลสาธิตระบบจริง ขอบคุณทุกท่านที่รับฟัง และหลังการสาธิตพวกเรายินดีตอบคำถามค่ะ"
  );
}

const requirements = {
  explicitTotalSlideCount: 13,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
};
const fontPolicy = { basis: "design", families: [FONT], scriptFonts: { ea: "Leelawadee UI" } };
const stagingDir = path.join(workspaceDir, ".artifact-build/deck-finalizer-team-bilingual");
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
  receiptPath: path.join(stagingDir, "DevPulse_V1_Team_3_Devils_Bilingual_Presentation.validation.json"),
});

console.log(FINAL_PPTX);
