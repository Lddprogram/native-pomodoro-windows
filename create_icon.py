import argparse
from pathlib import Path

from PIL import Image, ImageFilter


ICON_SIZES = tuple((size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256))
MASTER_SIZE = 1024
SUBJECT_SIZE = 880


def create_icon(source_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGBA")
    alpha = source.getchannel("A")
    bbox = alpha.point(lambda value: 255 if value >= 3 else 0).getbbox()
    if bbox is None:
        raise ValueError("The source image has no visible pixels.")

    source = source.crop(bbox)
    scale = min(SUBJECT_SIZE / source.width, SUBJECT_SIZE / source.height)
    size = (
        max(1, round(source.width * scale)),
        max(1, round(source.height * scale)),
    )
    source = source.resize(size, Image.Resampling.LANCZOS)

    master = Image.new("RGBA", (MASTER_SIZE, MASTER_SIZE), (0, 0, 0, 0))
    x = (MASTER_SIZE - source.width) // 2
    y = (MASTER_SIZE - source.height) // 2

    shadow_alpha = source.getchannel("A").filter(ImageFilter.GaussianBlur(28))
    shadow_alpha = shadow_alpha.point(lambda value: round(value * 0.22))
    shadow = Image.new("RGBA", source.size, (24, 24, 24, 0))
    shadow.putalpha(shadow_alpha)
    master.alpha_composite(shadow, (x, y + 18))
    master.alpha_composite(source, (x, y))

    icon = master.resize((256, 256), Image.Resampling.LANCZOS)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    icon.save(output_path, format="ICO", sizes=ICON_SIZES, bitmap_format="png")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Create a multi-resolution Windows icon from a transparent image."
    )
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    create_icon(args.source, args.output)
    print(args.output.resolve())


if __name__ == "__main__":
    main()
