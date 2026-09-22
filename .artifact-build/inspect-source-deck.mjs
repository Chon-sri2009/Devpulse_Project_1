import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, PresentationFile } from "@oai/artifact-tool";

const source = "C:/Users/Admin/Downloads/DevPulse_V1_Presentation_Final.pptx";
const output = path.resolve(".artifact-build/source-deck-inspection");
await fs.mkdir(output, { recursive: true });

const presentation = await PresentationFile.importPptx(await FileBlob.load(source));
const snapshot = await presentation.inspect({
  kind: "deck,slide,textbox,shape,image,table,chart,notes,layout",
  include: "id,slide,name,title,text,textPreview,bbox,bboxUnit,alt,placeholders",
  maxChars: 60000,
});
await fs.writeFile(path.join(output, "inspection.ndjson"), snapshot.ndjson);

for (let index = 0; index < presentation.slides.items.length; index += 1) {
  const slide = presentation.slides.getItem(index);
  const preview = await presentation.export({ slide, format: "png", scale: 1 });
  await fs.writeFile(path.join(output, `slide-${index + 1}.png`), new Uint8Array(await preview.arrayBuffer()));
}

console.log(snapshot.ndjson);
