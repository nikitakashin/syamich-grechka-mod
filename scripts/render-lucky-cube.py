#!/usr/bin/env python3
"""Наложить квадратную текстуру на куб и сохранить PNG с прозрачным фоном.

Зависимость: python3 -m pip install Pillow
Пример: python3 scripts/render-lucky-cube.py face.png -o cube.png
Несколько файлов: python3 scripts/render-lucky-cube.py *-face.png --output-dir cubes
"""

import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance


def extend_edges(image, border):
    """Продлить крайние пиксели для выборки на стыках, как clamp-to-edge у GPU."""
    w, h = image.size
    padded = Image.new("RGBA", (w + 2 * border, h + 2 * border))
    padded.paste(image, (border, border))
    for box, target, dimensions in [
        ((0, 0, w, 1), (border, 0), (w, border)),
        ((0, h - 1, w, h), (border, h + border), (w, border)),
        ((0, 0, 1, h), (0, border), (border, h)),
        ((w - 1, 0, w, h), (w + border, border), (border, h)),
    ]:
        padded.paste(image.crop(box).resize(dimensions, Image.Resampling.NEAREST), target)
    for x, source_x in ((0, 0), (w + border, w - 1)):
        for y, source_y in ((0, 0), (h + border, h - 1)):
            padded.paste(image.getpixel((source_x, source_y)), (x, y, x + border, y + border))
    return padded


def render_cube(texture, size=512, yaw=25.0, pitch=20.0, padding=0.04,
                side_shade=0.68, top_shade=1.08):
    """Ортографическая проекция настоящего единичного куба, без зеркальных граней.

    Текстура остаётся неизменной; каждый пиксель результата берётся из исходника
    методом nearest neighbour. Освещение меняет только RGB, сохраняя альфа-канал.
    """
    if texture.width != texture.height:
        raise ValueError("Текстура грани должна быть квадратной")
    if size < 16 or not 0 < yaw < 90 or not 0 < pitch < 90:
        raise ValueError("Размер должен быть >= 16, углы — строго между 0 и 90")
    if not 0 <= padding < 0.45 or side_shade < 0 or top_shade < 0:
        raise ValueError("Отступ должен быть 0..0.45, яркость — неотрицательной")
    texture = texture.convert("RGBA")
    yaw, pitch = math.radians(yaw), math.radians(pitch)

    def project(vertex):
        x, y, z = vertex
        return (math.cos(yaw) * x + math.sin(yaw) * z,
                math.sin(pitch) * math.sin(yaw) * x - math.cos(pitch) * y
                - math.sin(pitch) * math.cos(yaw) * z)

    vertices = [project((x, y, z)) for x in (0, 1) for y in (0, 1) for z in (0, 1)]
    min_x, max_x = min(p[0] for p in vertices), max(p[0] for p in vertices)
    min_y, max_y = min(p[1] for p in vertices), max(p[1] for p in vertices)
    scale = (size - 1) * (1 - 2 * padding) / max(max_x - min_x, max_y - min_y)
    offset_x = (size - 1 - scale * (max_x - min_x)) / 2
    offset_y = (size - 1 - scale * (max_y - min_y)) / 2

    def screen(vertex):
        x, y = project(vertex)
        return offset_x + (x - min_x) * scale, offset_y + (y - min_y) * scale

    # Для каждой грани: верхний левый, верхний правый, нижний левый углы UV.
    # Четвёртый угол следует из параллелограмма ортографической проекции.
    faces = [
        (((0, 1, 1), (1, 1, 1), (0, 1, 0)), top_shade),
        (((1, 1, 0), (1, 1, 1), (1, 0, 0)), side_shade),
        (((0, 1, 0), (1, 1, 0), (0, 0, 0)), 1.0),
    ]
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    for corners, brightness in faces:
        origin, right, down = [screen(p) for p in corners]
        ux, uy = right[0] - origin[0], right[1] - origin[1]
        vx, vy = down[0] - origin[0], down[1] - origin[1]
        determinant = ux * vy - uy * vx
        # Pillow принимает обратное преобразование: координаты PNG -> UV текстуры.
        a, b = texture.width * vy / determinant, -texture.width * vx / determinant
        d, e = -texture.height * uy / determinant, texture.height * ux / determinant
        border = math.ceil(max(abs(a) + abs(b), abs(d) + abs(e))) + 2
        affine = (a, b, -a * origin[0] - b * origin[1] + border,
                  d, e, -d * origin[0] - e * origin[1] + border)
        face = extend_edges(ImageEnhance.Brightness(texture).enhance(brightness), border)
        warped = face.transform((size, size), Image.Transform.AFFINE, affine,
                                resample=Image.Resampling.NEAREST,
                                fillcolor=(0, 0, 0, 0))
        mask = Image.new("L", (size, size))
        fourth = (right[0] + down[0] - origin[0], right[1] + down[1] - origin[1])
        ImageDraw.Draw(mask).polygon([origin, right, fourth, down], fill=255)
        warped.putalpha(Image.composite(warped.getchannel("A"),
                                       Image.new("L", (size, size)), mask))
        canvas.alpha_composite(warped)
    return canvas


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("textures", type=Path, nargs="+", help="Квадратные PNG граней")
    outputs = parser.add_mutually_exclusive_group()
    outputs.add_argument("-o", "--output", type=Path, help="PNG для одного исходника")
    outputs.add_argument("--output-dir", type=Path, help="Папка для результатов")
    parser.add_argument("--size", type=int, default=512, help="Размер PNG, по умолчанию 512")
    parser.add_argument("--yaw", type=float, default=25, help="Поворот куба, градусы")
    parser.add_argument("--pitch", type=float, default=20, help="Наклон камеры, градусы")
    parser.add_argument("--padding", type=float, default=0.04, help="Отступ, доля холста")
    parser.add_argument("--side-shade", type=float, default=0.68, help="Яркость правой грани")
    parser.add_argument("--top-shade", type=float, default=1.08, help="Яркость верхней грани")
    parser.add_argument("--overwrite", action="store_true", help="Заменять готовые результаты")
    args = parser.parse_args()
    if args.output and len(args.textures) != 1:
        parser.error("--output принимает только один исходник; используйте --output-dir")

    jobs = []
    for source in args.textures:
        target = args.output or (args.output_dir or source.parent) / (source.stem + "-cube.png")
        if target.suffix.lower() != ".png":
            parser.error("Результат должен иметь расширение .png")
        if target.resolve() in {item.resolve() for item in args.textures}:
            parser.error("Результат не должен перезаписывать исходную текстуру")
        if any(target.resolve() == previous.resolve() for _, previous in jobs):
            parser.error("Несколько исходников имеют одинаковое имя результата")
        if target.exists() and not args.overwrite:
            parser.error(f"Уже существует: {target}. Для замены используйте --overwrite")
        jobs.append((source, target))
    for source, target in jobs:
        try:
            with Image.open(source) as texture:
                cube = render_cube(texture, args.size, args.yaw, args.pitch, args.padding,
                                   args.side_shade, args.top_shade)
            target.parent.mkdir(parents=True, exist_ok=True)
            cube.save(target, format="PNG")
        except (OSError, ValueError) as error:
            parser.error(f"{source}: {error}")
        print(target.resolve())


if __name__ == "__main__":
    main()
