"""Draws the ResearchFlow launcher icon (Assets/VRMaintenanceResearch/ResearchFlow/UI/AppIcon): a PC card with a wrench, Blue/Ink/white.

usage: python Tools/make_app_icon.py <out_1024.png> <preview_192.png>
"""
import sys
from PIL import Image, ImageDraw

S = 1024 * 4  # supersample, then downscale for clean edges
BLUE, INK, WHITE = (31, 110, 219), (20, 36, 59), (255, 255, 255)
im = Image.new("RGB", (S, S), BLUE)
d = ImageDraw.Draw(im)
u = S / 100

# Card body with gold-free contact strip and a chip, drawn in white/ink only.
d.rounded_rectangle([18*u, 30*u, 82*u, 66*u], radius=5*u, fill=WHITE)
for i in range(9):
    x = 24*u + i * 5.6*u
    d.rectangle([x, 66*u, x + 3*u, 72*u], fill=WHITE)
d.rounded_rectangle([26*u, 38*u, 48*u, 58*u], radius=2.5*u, fill=INK)
for row in range(3):
    d.rectangle([54*u, (40 + row*6)*u, 74*u, (42.5 + row*6)*u], fill=INK)

# Wrench across the upper right corner.
w = Image.new("RGBA", (S, S), (0, 0, 0, 0)); wd = ImageDraw.Draw(w)
wd.rounded_rectangle([44*u, 47*u, 96*u, 55*u], radius=4*u, fill=INK)
wd.ellipse([82*u, 39*u, 102*u, 63*u], fill=INK)
wd.rectangle([94*u, 47.5*u, 104*u, 54.5*u], fill=(0, 0, 0, 0))
w = w.rotate(40, center=(70*u, 51*u), resample=Image.BICUBIC)
w = w.transform(w.size, Image.AFFINE, (1, 0, 4*u, 0, 1, 26*u), resample=Image.BICUBIC)
im.paste(w, (0, 0), w)

im = im.resize((1024, 1024), Image.LANCZOS)
im.save(sys.argv[1])
im.resize((192, 192), Image.LANCZOS).save(sys.argv[2])
