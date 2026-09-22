import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, PresentationFile } from "@oai/artifact-tool";

const source = path.resolve("deliverables/DevPulse_V1_Team_3_Devils_Bilingual_Presentation.pptx");
const output = path.resolve(".artifact-build/team-deck-render");
await fs.mkdir(output, { recursive: true });

const presentation = await PresentationFile.importPptx(await FileBlob.load(source));
const snapshot = await presentation.inspect({
  kind: "deck,slide,textbox,image,notes,layout",
  include: "id,slide,name,title,text,textPreview,bbox,bboxUnit,alt",
  maxChars: 100000,
});
await fs.writeFile(path.join(output, "inspection.ndjson"), snapshot.ndjson);

for (let index = 0; index < presentation.slides.items.length; index += 1) {
  const slide = presentation.slides.getItem(index);
  const preview = await presentation.export({ slide, format: "png", scale: 1 });
  await fs.writeFile(path.join(output, `slide-${index + 1}.png`), new Uint8Array(await preview.arrayBuffer()));
}

console.log(`Rendered ${presentation.slides.items.length} slides`);
console.log(snapshot.ndjson.split("\n").filter(line => line.includes('"kind":"notes"')).join("\n"));
