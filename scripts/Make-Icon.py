from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / 'assets'
with Image.open(assets / 'icon.png') as image:
    image.save(assets / 'BeniZip.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
