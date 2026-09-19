import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const workspaceDir = "D:\\VS_Project\\MiniProject_Everything_1";
const SKILL_DIR = "C:\\Users\\Admin\\.codex\\plugins\\cache\\openai-primary-runtime\\presentations\\26.909.11809\\skills\\presentations";
const TMP_DIR = path.join(workspaceDir, ".codex-build", "siam-siladin");
const FINAL_PPTX = path.join(workspaceDir, "output", "สยามศิลาดิน_นำเสนอธุรกิจ_5หน้า_v2.pptx");
const RUNTIME_PYTHON = "C:\\Users\\Admin\\.cache\\codex-runtimes\\codex-primary-runtime\\dependencies\\python\\python.exe";

const { makeNativeBulletParagraphs, finalizePresentation } = await import(
  pathToFileURL(path.join(SKILL_DIR, "container_tools", "artifact_tool_utils.mjs")).href,
);

await fs.mkdir(TMP_DIR, { recursive: true });
await fs.mkdir(path.dirname(FINAL_PPTX), { recursive: true });

const FONT = "Tahoma";
const C = {
  cream: "#F7F0E5",
  paper: "#FFFDFC",
  clay: "#A94F2C",
  darkClay: "#5E2F24",
  ochre: "#D4A24D",
  charcoal: "#2D2926",
  muted: "#6F655F",
  paleClay: "#E9D2BF",
  paleGold: "#F2E4C5",
  sage: "#526558",
  white: "#FFFFFF",
};

const presentation = Presentation.create({ slideSize: { width: 1280, height: 720 } });

function addRect(slide, x, y, w, h, fill, lineFill = "none", lineWidth = 0, radius = 0) {
  return slide.shapes.add({
    geometry: "rect",
    position: { left: x, top: y, width: w, height: h },
    fill,
    line: { fill: lineFill, width: lineWidth },
    borderRadius: radius,
  });
}

function addText(slide, text, x, y, w, h, opts = {}) {
  const shape = slide.shapes.add({
    geometry: "textbox",
    position: { left: x, top: y, width: w, height: h },
    fill: "none",
    line: { fill: "none", width: 0 },
  });
  shape.text = text;
  shape.text.style = {
    typeface: FONT,
    fontSize: opts.fontSize ?? 24,
    bold: opts.bold ?? false,
    color: opts.color ?? C.charcoal,
    alignment: opts.alignment ?? "left",
    verticalAlignment: opts.verticalAlignment ?? "top",
    autoFit: opts.autoFit ?? "none",
    wrap: "square",
    lineSpacing: opts.lineSpacing ?? 1.08,
    insets: opts.insets ?? { top: 0, right: 0, bottom: 0, left: 0 },
  };
  return shape;
}

function addBullets(slide, items, x, y, w, h, opts = {}) {
  const shape = addText(slide, "", x, y, w, h, {
    fontSize: opts.fontSize ?? 23,
    color: opts.color ?? C.charcoal,
    lineSpacing: opts.lineSpacing ?? 1.05,
  });
  shape.text = makeNativeBulletParagraphs(items, {
    marginLeftPoints: opts.marginLeftPoints ?? 18,
    hangingPoints: opts.hangingPoints ?? 9,
    spaceAfterPoints: opts.spaceAfterPoints ?? 9,
  });
  shape.text.style = {
    typeface: FONT,
    fontSize: opts.fontSize ?? 23,
    color: opts.color ?? C.charcoal,
    autoFit: "none",
    wrap: "square",
    lineSpacing: opts.lineSpacing ?? 1.05,
    insets: { top: 0, right: 0, bottom: 0, left: 0 },
  };
  return shape;
}

function addHeader(slide, title, number, dark = false) {
  addText(slide, title, 70, 48, 1030, 62, {
    fontSize: 40,
    bold: true,
    color: dark ? C.cream : C.darkClay,
  });
  addRect(slide, 70, 118, 92, 5, C.ochre);
  addText(slide, `${number}/5`, 1160, 58, 56, 26, {
    fontSize: 16,
    color: dark ? C.paleClay : C.muted,
    alignment: "right",
  });
}

function addPicturePlaceholder(slide, label, x, y, w, h, dark = false) {
  const fill = dark ? "#6C3A2C" : "#F1E1D2";
  const line = dark ? C.paleClay : "#C98D6E";
  const text = dark ? C.cream : C.darkClay;
  addRect(slide, x, y, w, h, fill, line, 2, 18);
  addRect(slide, x + 26, y + 26, w - 52, 2, line);
  addRect(slide, x + 26, y + h - 28, w - 52, 2, line);
  addText(slide, label, x + 40, y + h / 2 - 24, w - 80, 48, {
    fontSize: 22,
    bold: true,
    color: text,
    alignment: "center",
    verticalAlignment: "middle",
  });
}

// Slide 1: Cover
{
  const slide = presentation.slides.add();
  slide.background.fill = C.cream;
  addRect(slide, 0, 0, 24, 720, C.clay);
  addRect(slide, 75, 72, 82, 7, C.ochre);
  addText(slide, "สยามศิลาดิน", 75, 105, 610, 86, {
    fontSize: 58,
    bold: true,
    color: C.darkClay,
  });
  addText(slide, "ธุรกิจเครื่องปั้นดินเผาที่สืบสานศิลปะ\nและวัฒนธรรมไทย", 78, 198, 590, 76, {
    fontSize: 28,
    color: C.clay,
    lineSpacing: 1.12,
  });
  addText(slide, "“ปั้นดินให้มีคุณค่า เพราะงานศิลป์ไทยควรอยู่คู่คนไทย”", 78, 293, 570, 78, {
    fontSize: 25,
    bold: true,
    color: C.sage,
    lineSpacing: 1.14,
  });
  addText(slide, "สมาชิก", 78, 420, 150, 36, {
    fontSize: 22,
    bold: true,
    color: C.darkClay,
  });
  addText(slide,
    "นาย นราวิชญ์ ทิดมนตรี  ชั้น 2/7  เลขที่ 14\n" +
    "นาย คณธัช หุ่นเมืองปัก  ชั้น 2/7  เลขที่ 15\n" +
    "นาย นวพรรต ลีนะกิตติ  ชั้น 2/7  เลขที่ 20\n" +
    "นาย ชลพล ศรีชาเยช  ชั้น 2/7  เลขที่ 21",
    78, 462, 560, 160,
    { fontSize: 19, color: C.charcoal, lineSpacing: 1.28 }
  );
  addPicturePlaceholder(slide, "พื้นที่สำหรับรูปหน้าปก", 735, 82, 455, 556);
}

// Slide 2: Why this business
{
  const slide = presentation.slides.add();
  slide.background.fill = C.paper;
  addHeader(slide, "แนวคิดและที่มาของธุรกิจ", 2);

  addText(slide, "แรงบันดาลใจ", 72, 160, 420, 44, {
    fontSize: 28,
    bold: true,
    color: C.clay,
  });
  addText(slide,
    "พวกเราชื่นชอบศิลปะและวัฒนธรรมไทย โดยเฉพาะเครื่องปั้นดินเผาที่มีความสวยงาม เรียบง่าย และสะท้อนภูมิปัญญาท้องถิ่น จึงต้องการพัฒนางานหัตถกรรมไทยให้เข้ากับความสนใจของผู้บริโภคยุคใหม่",
    72, 220, 522, 270,
    { fontSize: 25, color: C.charcoal, lineSpacing: 1.24 }
  );
  addRect(slide, 637, 157, 2, 455, C.paleClay);

  addText(slide, "ความหมายของชื่อ “สยามศิลาดิน”", 688, 160, 500, 44, {
    fontSize: 27,
    bold: true,
    color: C.darkClay,
  });
  addText(slide, "สยาม", 688, 229, 110, 34, { fontSize: 24, bold: true, color: C.clay });
  addText(slide, "สื่อถึงความเป็นไทย", 815, 229, 350, 34, { fontSize: 23, color: C.charcoal });
  addText(slide, "ศิลาและดิน", 688, 286, 150, 34, { fontSize: 24, bold: true, color: C.clay });
  addText(slide, "สื่อถึงวัสดุธรรมชาติและงานฝีมือ", 850, 286, 330, 62, { fontSize: 23, color: C.charcoal });

  addRect(slide, 685, 376, 485, 2, C.paleClay);
  addText(slide, "แนวคิดของธุรกิจ", 688, 410, 270, 40, {
    fontSize: 27,
    bold: true,
    color: C.sage,
  });
  addText(slide,
    "จำหน่ายเครื่องปั้นดินเผา ควบคู่ชุดระบายสีและคอร์สปั้นดิน ลูกค้าจึงได้ทั้งสินค้า ความรู้ และความสนุกจากการลงมือทำ",
    688, 462, 485, 132,
    { fontSize: 22, color: C.charcoal, lineSpacing: 1.18 }
  );
  addRect(slide, 72, 614, 1098, 3, C.ochre);
  addText(slide, "เป้าหมายของแบรนด์คือทำให้งานศิลป์จากดินเข้าถึงง่ายและอยู่ร่วมกับชีวิตประจำวัน", 72, 635, 1098, 36, {
    fontSize: 22,
    bold: true,
    color: C.darkClay,
    alignment: "center",
  });
}

// Slide 3: Audience and channels
{
  const slide = presentation.slides.add();
  slide.background.fill = C.cream;
  addHeader(slide, "กลุ่มเป้าหมายและช่องทางจัดจำหน่าย", 3);

  addText(slide, "กลุ่มเป้าหมายหลัก", 72, 158, 460, 45, {
    fontSize: 28,
    bold: true,
    color: C.clay,
  });
  addBullets(slide, [
    "ผู้ที่สนใจศิลปะและวัฒนธรรมไทย",
    "นักเรียน นักศึกษา และครอบครัวที่มองหากิจกรรมสร้างสรรค์",
    "นักท่องเที่ยวที่ต้องการของฝากที่มีเอกลักษณ์ไทย",
    "ผู้ที่ต้องการของตกแต่งบ้านหรือของขวัญทำมือ",
  ], 72, 220, 500, 280, { fontSize: 22, spaceAfterPoints: 12 });

  addRect(slide, 618, 160, 2, 360, C.paleClay);
  addText(slide, "ช่องทางจัดจำหน่าย", 670, 158, 480, 45, {
    fontSize: 28,
    bold: true,
    color: C.sage,
  });
  addText(slide, "หน้าร้าน", 670, 224, 150, 32, { fontSize: 22, bold: true, color: C.darkClay });
  addText(slide, "ร้านสยามศิลาดิน ตลาดงานฝีมือ และบูธตามเทศกาลหรือแหล่งท่องเที่ยวเชิงวัฒนธรรม", 670, 267, 490, 86, {
    fontSize: 21,
    color: C.charcoal,
    lineSpacing: 1.18,
  });
  addText(slide, "ออนไลน์", 670, 380, 150, 32, { fontSize: 22, bold: true, color: C.darkClay });
  addText(slide, "Facebook • Instagram • TikTok\nLINE Official Account • Shopee • Lazada", 670, 425, 490, 72, {
    fontSize: 21,
    color: C.charcoal,
    lineSpacing: 1.22,
  });

  addRect(slide, 72, 553, 1098, 104, C.darkClay, "none", 0, 14);
  addText(slide,
    "เครื่องปั้นดินเผาเป็นทั้งของใช้ ของตกแต่ง และผลงานศิลปะ ลูกค้าจึงเลือกซื้อเพื่อใช้งาน สะสม มอบเป็นของขวัญ หรือเรียนรู้การปั้นด้วยตนเองได้",
    105, 578, 1032, 62,
    { fontSize: 23, color: C.cream, alignment: "center", verticalAlignment: "middle", lineSpacing: 1.14 }
  );
}

// Slide 4: Products and services
{
  const slide = presentation.slides.add();
  slide.background.fill = C.paper;
  addHeader(slide, "สินค้าและบริการ", 4);

  const columns = [
    {
      x: 72,
      n: "01",
      title: "เครื่องใช้และของตกแต่ง",
      items: ["ถ้วย จาน และหม้อ", "แจกันและของตกแต่งบ้าน", "กำไลข้อมือจากดินเผา"],
      color: C.clay,
    },
    {
      x: 430,
      n: "02",
      title: "กิจกรรมสร้างสรรค์",
      items: ["ของเล่นจากดินเผา", "ชุดเครื่องปั้นดินเผาสำหรับระบายสี", "อุปกรณ์ขึ้นรูปและตกแต่ง"],
      color: C.ochre,
    },
    {
      x: 788,
      n: "03",
      title: "คอร์สและประสบการณ์",
      items: ["คอร์สปั้นดินสำหรับผู้เริ่มต้น", "กิจกรรมสำหรับคู่รัก ครอบครัว และกลุ่มเพื่อน", "กิจกรรมสำหรับโรงเรียน\nและองค์กร"],
      color: C.sage,
    },
  ];

  for (const col of columns) {
    addText(slide, col.n, col.x, 164, 100, 48, { fontSize: 31, bold: true, color: col.color });
    addRect(slide, col.x, 220, 292, 4, col.color);
    addText(slide, col.title, col.x, 250, 300, 72, { fontSize: 25, bold: true, color: C.darkClay, lineSpacing: 1.1 });
    addBullets(slide, col.items, col.x, 344, 300, 210, { fontSize: 21, spaceAfterPoints: 12, marginLeftPoints: 16, hangingPoints: 8 });
  }

  addRect(slide, 72, 586, 1098, 82, C.paleGold, "none", 0, 12);
  addText(slide,
    "จุดเด่นของร้าน: ลูกค้าสามารถซื้อสินค้าสำเร็จรูป ออกแบบผลงานของตนเอง หรือเรียนรู้กระบวนการปั้นดินได้ภายในร้านเดียว",
    102, 607, 1038, 42,
    { fontSize: 22, bold: true, color: C.darkClay, alignment: "center", verticalAlignment: "middle" }
  );
}

// Slide 5: Advertising poster
{
  const slide = presentation.slides.add();
  slide.background.fill = C.darkClay;
  addRect(slide, 0, 0, 34, 720, C.ochre);
  addText(slide, "สยามศิลาดิน", 76, 56, 630, 68, {
    fontSize: 45,
    bold: true,
    color: C.cream,
  });
  addText(slide, "“ปั้นดินให้มีคุณค่า เพราะงานศิลป์ไทยควรอยู่คู่คนไทย”", 78, 130, 600, 64, {
    fontSize: 23,
    bold: true,
    color: C.paleGold,
    lineSpacing: 1.12,
  });
  addText(slide, "สัมผัสเสน่ห์เครื่องปั้นดินเผาไทย", 78, 231, 600, 46, {
    fontSize: 29,
    bold: true,
    color: C.white,
  });
  addText(slide, "เลือกซื้อของใช้ ของตกแต่ง และสนุกกับกิจกรรมระบายสีหรือปั้นดินด้วยตนเอง", 78, 288, 575, 78, {
    fontSize: 22,
    color: C.cream,
    lineSpacing: 1.16,
  });

  addRect(slide, 78, 398, 556, 2, C.paleClay);
  addText(slide, "โปรโมชั่นพิเศษ", 78, 425, 240, 34, { fontSize: 22, bold: true, color: C.ochre });
  addText(slide, "ซื้อครบ 100 บาท", 78, 468, 420, 54, { fontSize: 36, bold: true, color: C.white });
  addText(slide, "รับส่วนลดสินค้าชิ้นถัดไป 20 บาท", 78, 528, 540, 38, { fontSize: 24, color: C.paleGold });
  addText(slide, "ซื้อชุดระบายสี 2 ชุดขึ้นไป รับส่วนลดคอร์สปั้นดิน 100 บาท", 78, 584, 558, 58, {
    fontSize: 20,
    color: C.cream,
    lineSpacing: 1.13,
  });
  addText(slide, "Facebook  •  TikTok  •  LINE  •  QR Code", 78, 665, 560, 28, { fontSize: 16, color: C.paleClay });
  addPicturePlaceholder(slide, "พื้นที่สำหรับรูปโปสเตอร์สินค้า", 715, 70, 485, 570, true);
  addText(slide, "ทุกชิ้นมีเรื่องราว ทุกการปั้นช่วยสืบสานงานศิลป์ไทย", 715, 661, 485, 26, {
    fontSize: 16,
    color: C.paleGold,
    alignment: "center",
  });
}

const requirements = {
  explicitTotalSlideCount: 5,
  requiredNativeTableOwnerSlides: [],
  requiredNativeChartOwnerSlides: [],
};
const fontPolicy = { basis: "design", families: [FONT] };
const expectedSlideSizeEmu = "12192000,6858000";
const stagingDir = path.join(workspaceDir, ".codex-finalizer");
await fs.mkdir(stagingDir, { recursive: true });
const candidatePath = path.join(stagingDir, "siam-siladin-candidate.pptx");
await (await PresentationFile.exportPptx(presentation)).save(candidatePath);

const result = await finalizePresentation({
  ...requirements,
  workspaceDir,
  candidatePath,
  finalPath: FINAL_PPTX,
  pythonExecutable: RUNTIME_PYTHON,
  integrityValidatorPath: path.join(SKILL_DIR, "container_tools", "inspect_presentation_package_integrity.py"),
  layoutValidatorPath: path.join(SKILL_DIR, "container_tools", "inspect_presentation_layout_geometry.py"),
  layoutArgs: [
    "--expected-slide-size-emu", expectedSlideSizeEmu,
    "--validate-bullet-geometry",
    "--validate-heading-fit",
  ],
  requiredNativeTableOwnerSlides: [],
  fontPolicy,
  verifyArtifactToolImport: true,
  receiptPath: path.join(stagingDir, "siam-siladin-v2.validation.json"),
});

console.log(JSON.stringify({ finalPath: FINAL_PPTX, result }, null, 2));
