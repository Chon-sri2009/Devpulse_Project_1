from pathlib import Path
from PIL import Image, ImageOps, ImageDraw


def make_sheets(source: Path, destination: Path, prefix: str) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    pages = sorted(source.glob("*.png"))
    for sheet_number, start in enumerate(range(0, len(pages), 4), start=1):
        batch = pages[start : start + 4]
        thumbs = []
        for page in batch:
            image = Image.open(page).convert("RGB")
            image.thumbnail((750, 980), Image.Resampling.LANCZOS)
            framed = Image.new("RGB", (790, 1040), "white")
            framed.paste(image, ((790 - image.width) // 2, 35))
            draw = ImageDraw.Draw(framed)
            draw.rectangle((0, 0, framed.width - 1, framed.height - 1), outline="#9ca3af", width=2)
            draw.text((18, 10), page.stem, fill="#111827")
            thumbs.append(framed)

        canvas = Image.new("RGB", (1600, 2100), "#e5e7eb")
        positions = [(10, 10), (800, 10), (10, 1050), (800, 1050)]
        for thumb, position in zip(thumbs, positions):
            canvas.paste(thumb, position)
        canvas.save(destination / f"{prefix}-{sheet_number:02d}.png")


root = Path(__file__).resolve().parent
make_sheets(root / "doc-render-setup-credits", root / "doc-contact-credits", "setup")
make_sheets(root / "doc-render-description-credits", root / "doc-contact-credits", "description")
