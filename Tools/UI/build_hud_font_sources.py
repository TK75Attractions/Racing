"""Create the HUD font masters from the repository's M PLUS variable font.

Requires fonttools (pip install fonttools). Run from the project root after Git LFS checkout.
The Unity menu Racing > UI > Build HUD Fonts bakes the TMP atlases separately.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

source = Path("Assets/Fonts/MPLUS1-VariableFont_wght.ttf")
font = TTFont(source)
for weight, name in [(500, "Medium"), (700, "Bold")]:
    instance = instantiateVariableFont(font, {"wght": weight}, inplace=False)
    family = "MPLUS HUD " + name
    for record in instance["name"].names:
        if record.nameID in (1, 4, 6, 16):
            value = family.replace(" ", "-") if record.nameID == 6 else family
            record.string = value.encode(record.getEncoding())
        elif record.nameID in (2, 17):
            record.string = "Regular".encode(record.getEncoding())
    instance.save(source.with_name("MPLUS1-HUD-" + name + ".ttf"))
