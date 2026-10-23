"""Source for the ten editable full-art Godot piece scenes.

Run from the repository root or any other directory:
    python art/full-art-v1/source/pieces-generate.py

Every output scene contains actual low-poly 3D meshes. Names are material slots
assigned per instance by PieceModels.cs; the common plinth is built there.
"""

from dataclasses import dataclass, field
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "src" / "godot" / "parts" / "pieces"

# Red-side preview swatches for editing the native scenes in Godot. PieceModels.cs
# replaces these with the selected faction's fresh materials during play.
PREVIEW_MATERIALS = {
    "Stone": ("Color(0.75, 0.65, 0.59, 1)", .88),
    "Shade": ("Color(0.36, 0.32, 0.36, 1)", .90),
    "Metal": ("Color(0.69, 0.57, 0.36, 1)", .70),
    "Enamel": ("Color(0.77, 0.23, 0.19, 1)", .68),
    "Cloth": ("Color(0.72, 0.28, 0.25, 1)", .90),
    "Crystal": ("Color(0.70, 0.73, 0.78, 1)", .58),
    "Ink": ("Color(0.95, 0.94, 0.91, 1)", .88),
}


def v(x: float, y: float, z: float) -> str:
    return f"Vector3({x:g}, {y:g}, {z:g})"


@dataclass
class Part:
    name: str
    kind: str
    properties: dict[str, str]
    at: tuple[float, float, float]
    parent: str = "."
    rotation: tuple[float, float, float] | None = None
    scale: tuple[float, float, float] | None = None


@dataclass
class Model:
    name: str
    parts: list[Part] = field(default_factory=list)
    groups: list[tuple[str, str, tuple[float, float, float], tuple[float, float, float]]] = field(default_factory=list)

    def group(self, name: str, at=(0, 0, 0), rotation=(0, 0, 0), parent="."):
        self.groups.append((name, parent, at, rotation))

    def box(self, name: str, size, at, parent=".", rotation=None):
        self.parts.append(Part(name, "BoxMesh", {"size": v(*size)}, at, parent, rotation))

    def cylinder(self, name: str, bottom: float, top: float, height: float, at,
                 parent=".", rotation=None, scale=None, segments: int = 8):
        self.parts.append(Part(name, "CylinderMesh", {
            "top_radius": f"{top:g}", "bottom_radius": f"{bottom:g}",
            "height": f"{height:g}", "radial_segments": str(segments),
        }, at, parent, rotation, scale))

    def sphere(self, name: str, radius: float, at, height=None, segments: int = 8, parent="."):
        self.parts.append(Part(name, "SphereMesh", {
            "radius": f"{radius:g}", "height": f"{(height or radius * 2):g}",
            "radial_segments": str(segments), "rings": "4",
        }, at, parent))

    def torus(self, name: str, inner: float, outer: float, at, rotation, scale=None):
        self.parts.append(Part(name, "TorusMesh", {
            "inner_radius": f"{inner:g}", "outer_radius": f"{outer:g}",
            "rings": "12", "ring_segments": "7",
        }, at, ".", rotation, scale))

    def render(self) -> str:
        slots = sorted({part.name.split("_", 1)[0] for part in self.parts})
        assert all(slot in PREVIEW_MATERIALS for slot in slots)
        lines = [f"[gd_scene load_steps={len(self.parts) + len(slots) + 1} format=3]", ""]
        for slot in slots:
            color, roughness = PREVIEW_MATERIALS[slot]
            lines.extend([f'[sub_resource type="StandardMaterial3D" id="mat_{slot}"]',
                          f"albedo_color = {color}", f"roughness = {roughness:g}", ""])
        for i, part in enumerate(self.parts, 1):
            lines.append(f'[sub_resource type="{part.kind}" id="mesh_{i}"]')
            lines.extend(f"{key} = {value}" for key, value in part.properties.items())
            lines.append("")
        lines.extend([f'[node name="{self.name}" type="Node3D"]', ""])
        for name, parent, at, rotation in self.groups:
            lines.extend([f'[node name="{name}" type="Node3D" parent="{parent}"]',
                          f"position = {v(*at)}", f"rotation_degrees = {v(*rotation)}", ""])
        for i, part in enumerate(self.parts, 1):
            lines.extend([f'[node name="{part.name}" type="MeshInstance3D" parent="{part.parent}"]',
                          f"position = {v(*part.at)}"])
            if part.rotation is not None:
                lines.append(f"rotation_degrees = {v(*part.rotation)}")
            if part.scale is not None:
                lines.append(f"scale = {v(*part.scale)}")
            slot = part.name.split("_", 1)[0]
            lines.extend([f'mesh = SubResource("mesh_{i}")',
                          f'material_override = SubResource("mat_{slot}")', ""])
        return "\n".join(lines)


def basic() -> Model:
    m = Model("RoundSentinel")
    m.cylinder("Shade_RobeHem", .205, .18, .055, (0, .145, 0), segments=10)
    m.cylinder("Stone_TaperedRobe", .185, .095, .265, (0, .285, 0), segments=8)
    m.cylinder("Stone_CarvedShoulders", .165, .123, .075, (0, .405, 0), segments=8)
    m.cylinder("Metal_Collar", .13, .13, .042, (0, .438, 0), segments=10)
    m.sphere("Stone_RoundHead", .164, (0, .575, 0), segments=10)
    m.box("Metal_HelmetBrow", (.235, .033, .042), (0, .555, .152))
    m.box("Enamel_ChestSeal", (.095, .12, .02), (0, .295, .167))
    m.box("Shade_LeftRobeCut", (.026, .095, .014), (-.075, .278, .166))
    m.box("Shade_RightRobeCut", (.026, .095, .014), (.075, .278, .166))
    m.sphere("Metal_CrownStud", .031, (0, .75, 0), segments=6)
    return m


def fortress() -> Model:
    m = Model("CarvedKeep")
    m.cylinder("Shade_WideFoot", .255, .235, .075, (0, .15, 0), segments=10)
    m.cylinder("Stone_Keep", .215, .185, .45, (0, .395, 0), segments=8)
    m.cylinder("Metal_LowerHoop", .225, .225, .035, (0, .245, 0), segments=10)
    m.cylinder("Enamel_UpperHoop", .235, .235, .045, (0, .53, 0), segments=10)
    m.cylinder("Stone_Parapet", .265, .265, .075, (0, .65, 0), segments=10)
    m.box("Shade_ArchedGate", (.135, .215, .025), (0, .36, .218))
    for x, z, suffix in [(-.18, -.18, "NW"), (.18, -.18, "NE"),
                         (-.18, .18, "SW"), (.18, .18, "SE")]:
        m.box(f"Stone_Merlon{suffix}", (.115, .145, .115), (x, .762, z))
    return m


def line() -> Model:
    m = Model("TwinOrbs")
    m.cylinder("Shade_CentralFoot", .10, .075, .18, (0, .205, 0), segments=8)
    m.box("Metal_JoiningBeam", (.49, .075, .075), (0, .42, 0))
    m.box("Enamel_BeamInlay", (.38, .026, .079), (0, .448, .003))
    for sign, suffix in [(-1, "Left"), (1, "Right")]:
        m.sphere(f"Stone_Orb{suffix}", .145, (sign * .215, .42, 0), segments=9)
        m.sphere(f"Metal_Pin{suffix}", .037, (sign * .215, .57, 0), segments=6)
    return m


def multiplier() -> Model:
    m = Model("RadialPyramid")
    m.cylinder("Shade_SteppedFoot", .225, .19, .085, (0, .155, 0), segments=4)
    m.cylinder("Stone_FourSidedSpire", .24, .002, .47, (0, .415, 0),
               rotation=(0, 45, 0), segments=4)
    m.cylinder("Metal_SpireCollar", .13, .13, .055, (0, .28, 0), segments=4)
    for axis, sign, suffix in [("X", -1, "West"), ("X", 1, "East"),
                               ("Z", -1, "North"), ("Z", 1, "South")]:
        x, z = (sign * .245, 0) if axis == "X" else (0, sign * .245)
        size = (.205, .042, .065) if axis == "X" else (.065, .042, .205)
        m.box(f"Enamel_Ray{suffix}", size, (x, .235, z))
        m.sphere(f"Metal_RayTip{suffix}", .035, (x * 1.30, .235, z * 1.30), segments=6)
    return m


def synergy() -> Model:
    m = Model("FivefoldCrystal")
    m.cylinder("Metal_CrownSocket", .205, .195, .065, (0, .16, 0), segments=10)
    m.torus("Metal_InterlockRing", .155, .22, (0, .275, 0), (0, 0, 0))
    m.sphere("Crystal_CentralHeart", .14, (0, .43, 0), segments=8)
    for x, z, tilt_x, tilt_z, height, suffix in [
        (-.19, 0, 0, -17, .38, "West"), (.19, 0, 0, 17, .38, "East"),
        (0, -.19, -17, 0, .36, "North"), (0, .19, 17, 0, .36, "South"),
    ]:
        m.cylinder(f"Crystal_Petal{suffix}", .092, .002, height,
                   (x, .22 + height * .5, z), rotation=(tilt_x, 0, tilt_z), segments=5)
        m.sphere(f"Metal_PetalSocket{suffix}", .043, (x, .255, z), segments=6)
    return m


def artisan() -> Model:
    m = Model("LeaningHammer")
    m.group("HammerLean", (-.055, .13, 0), (0, 0, -17))
    m.box("Shade_AnvilFoot", (.245, .075, .205), (.06, .15, 0))
    m.box("Metal_ToolShaft", (.072, .56, .072), (0, .285, 0), parent="HammerLean")
    m.box("Stone_HammerHead", (.38, .13, .15), (0, .59, 0), parent="HammerLean")
    m.box("Enamel_HeadBinding", (.085, .135, .155), (0, .59, 0), parent="HammerLean")
    m.box("Metal_SlantBrace", (.055, .39, .055), (.145, .285, .025), rotation=(0, 0, 34))
    m.box("Shade_BraceFoot", (.17, .055, .13), (.18, .16, .025))
    return m


def bannerman() -> Model:
    m = Model("HighPennant")
    m.cylinder("Shade_PoleSocket", .095, .065, .105, (0, .17, 0), segments=8)
    m.cylinder("Metal_TrueStaff", .032, .027, .78, (0, .51, 0), segments=7)
    m.sphere("Metal_Finial", .052, (0, .922, 0), segments=7)
    m.box("Cloth_BannerField", (.26, .27, .024), (.16, .68, 0))
    m.box("Cloth_LeftTail", (.09, .095, .024), (.075, .51, 0))
    m.box("Cloth_RightTail", (.09, .095, .024), (.245, .51, 0))
    m.box("Enamel_BannerHeading", (.27, .032, .03), (.16, .83, 0))
    return m


def chain() -> Model:
    m = Model("InterlockedRings")
    m.cylinder("Shade_LinkFoot", .105, .095, .055, (0, .145, 0), segments=8)
    m.torus("Metal_LowerOpenLink", .105, .18, (0, .337, 0), (90, 0, 0), (1, 1, 1.32))
    m.torus("Enamel_UpperOpenLink", .105, .18, (0, .517, 0), (0, 0, 90), (1.32, 1, 1))
    return m


def sentry() -> Model:
    m = Model("CrossedSpears")
    m.cylinder("Shade_SpearSocket", .13, .11, .07, (0, .16, 0), segments=8)
    for sign, suffix in [(-1, "Left"), (1, "Right")]:
        angle = sign * 29
        tip_x = -sign * .214
        m.box(f"Metal_Shaft{suffix}", (.045, .64, .045), (0, .455, 0), rotation=(0, 0, angle))
        m.cylinder(f"Stone_Point{suffix}", .061, .002, .16, (tip_x, .785, 0),
                   rotation=(0, 0, angle), segments=4)
    m.box("Enamel_CrossBinding", (.14, .095, .095), (0, .455, 0))
    return m


def boundary() -> Model:
    m = Model("BroadBoundaryStone")
    m.group("TabletTilt", (0, .135, 0), (-14, 0, 0))
    m.box("Shade_WideStep", (.70, .055, .22), (0, .147, 0))
    m.box("Stone_BroadTablet", (.66, .245, .13), (0, .1225, 0), parent="TabletTilt")
    m.box("Stone_LeftPier", (.09, .275, .16), (-.307, .137, 0), parent="TabletTilt")
    m.box("Stone_RightPier", (.09, .275, .16), (.307, .137, 0), parent="TabletTilt")
    m.cylinder("Stone_RoundedCrown", .33, .33, .13, (0, .245, 0), parent="TabletTilt",
               rotation=(90, 0, 0), scale=(1, 1, .45), segments=12)
    m.box("Metal_CrownCap", (.64, .035, .14), (0, .34, 0), parent="TabletTilt")
    m.box("Metal_LeftSealPin", (.047, .065, .019), (-.29, .205, .088), parent="TabletTilt")
    m.box("Metal_RightSealPin", (.047, .065, .019), (.29, .205, .088), parent="TabletTilt")
    m.box("Enamel_LowerInscription", (.48, .042, .016), (0, .125, .073), parent="TabletTilt")
    m.box("Enamel_UpperInscription", (.48, .042, .016), (0, .225, .073), parent="TabletTilt")
    return m


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    models = {
        "basic": basic(), "fortress": fortress(), "line": line(),
        "multiplier": multiplier(), "synergy": synergy(), "artisan": artisan(),
        "bannerman": bannerman(), "chain": chain(), "sentry": sentry(),
        "boundary": boundary(),
    }
    for name, model in models.items():
        with (OUT / f"{name}.tscn").open("w", encoding="utf-8", newline="\n") as output:
            output.write(model.render())
        print(OUT / f"{name}.tscn")


if __name__ == "__main__":
    main()
