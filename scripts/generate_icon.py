from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets"
OUT.mkdir(parents=True, exist_ok=True)

size = 256
image = Image.new("RGBA", (size, size), "#1B1E24")
draw = ImageDraw.Draw(image)

# Compact script-frame: cool-white braces, prompt chevron, forge-orange cursor.
white = "#F1F4F6"
orange = "#FF982E"
width = 24

draw.line([(70, 42), (44, 42), (44, 214), (70, 214)], fill=white, width=width, joint="curve")
draw.line([(186, 42), (212, 42), (212, 214), (186, 214)], fill=white, width=width, joint="curve")
draw.line([(88, 84), (137, 128), (88, 172)], fill=white, width=width, joint="curve")
draw.rounded_rectangle((132, 168, 181, 191), radius=2, fill=orange)

image.save(OUT / "cmdletforge.png")
image.save(OUT / "cmdletforge.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
