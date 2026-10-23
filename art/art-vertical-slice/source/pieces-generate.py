"""Editable low-poly model source for the first three Siege piece bodies.

Run from any directory:
    python art/art-vertical-slice/source/pieces-generate.py

The generated Godot scenes contain real primitive meshes and remain editable in the
Godot scene editor. Their root is the body only; PieceModels.cs keeps the existing
shared plinth and emblem. Dimensions use one board cell as roughly one world unit.
"""

from dataclasses import dataclass, field
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "src" / "godot" / "parts" / "pieces"


def vec(x: float, y: float, z: float) -> str:
    return f"Vector3({x:g}, {y:g}, {z:g})"


@dataclass
class Part:
    name: str
    mesh_type: str
    properties: dict[str, str]
    position: tuple[float, float, float]
    parent: str = "."
    rotation: tuple[float, float, float] | None = None
    scale: tuple[float, float, float] | None = None


@dataclass
class Model:
    name: str
    parts: list[Part] = field(default_factory=list)
    groups: list[tuple[str, str, tuple[float, float, float], tuple[float, float, float]]] = field(default_factory=list)

    def box(self, name: str, size: tuple[float, float, float], at: tuple[float, float, float],
            parent: str = ".", rotation: tuple[float, float, float] | None = None):
        self.parts.append(Part(name, "BoxMesh", {"size": vec(*size)}, at, parent, rotation))

    def cylinder(self, name: str, bottom: float, top: float, height: float,
                 at: tuple[float, float, float], parent: str = ".",
                 rotation: tuple[float, float, float] | None = None,
                 scale: tuple[float, float, float] | None = None,
                 segments: int = 8):
        self.parts.append(Part(name, "CylinderMesh", {
            "top_radius": f"{top:g}", "bottom_radius": f"{bottom:g}",
            "height": f"{height:g}", "radial_segments": str(segments),
        }, at, parent, rotation, scale))

    def sphere(self, name: str, radius: float, height: float,
               at: tuple[float, float, float], segments: int = 8):
        self.parts.append(Part(name, "SphereMesh", {
            "radius": f"{radius:g}", "height": f"{height:g}",
            "radial_segments": str(segments), "rings": "4",
        }, at))

    def render(self) -> str:
        lines = [f"[gd_scene load_steps={len(self.parts) + 1} format=3]", ""]
        for index, part in enumerate(self.parts, 1):
            lines.append(f'[sub_resource type="{part.mesh_type}" id="mesh_{index}"]')
            lines.extend(f"{key} = {value}" for key, value in part.properties.items())
            lines.append("")

        lines.extend([f'[node name="{self.name}" type="Node3D"]', ""])
        for name, parent, position, rotation in self.groups:
            lines.extend([f'[node name="{name}" type="Node3D" parent="{parent}"]',
                          f"position = {vec(*position)}", f"rotation_degrees = {vec(*rotation)}", ""])

        for index, part in enumerate(self.parts, 1):
            lines.append(f'[node name="{part.name}" type="MeshInstance3D" parent="{part.parent}"]')
            lines.append(f"position = {vec(*part.position)}")
            if part.rotation is not None:
                lines.append(f"rotation_degrees = {vec(*part.rotation)}")
            if part.scale is not None:
                lines.append(f"scale = {vec(*part.scale)}")
            lines.extend([f'mesh = SubResource("mesh_{index}")', ""])
        return "\n".join(lines)


def basic() -> Model:
    """Compact round head, clear narrow neck, and a gently tapering stone coat."""
    m = Model("BasicBody")
    m.cylinder("Shade_FootRing", 0.19, 0.18, 0.05, (0, 0.095, 0))
    m.cylinder("Stone_Coat", 0.165, 0.105, 0.22, (0, 0.215, 0))
    m.cylinder("Faction_Collar", 0.115, 0.115, 0.045, (0, 0.345, 0))
    m.sphere("Stone_RoundHead", 0.17, 0.34, (0, 0.52, 0))
    m.cylinder("Metal_CrownPin", 0.028, 0.018, 0.04, (0, 0.705, 0), segments=6)
    return m


def fortress() -> Model:
    """Tall octagonal keep with an overhanging ring and four separate battlements."""
    m = Model("FortressBody")
    m.cylinder("Shade_Footing", 0.255, 0.225, 0.075, (0, 0.1075, 0))
    m.cylinder("Stone_Keep", 0.218, 0.187, 0.43, (0, 0.355, 0))
    m.cylinder("Faction_WaistBand", 0.225, 0.225, 0.047, (0, 0.435, 0))
    m.cylinder("Stone_Parapet", 0.255, 0.255, 0.07, (0, 0.604, 0))
    m.box("Shade_Gate", (0.13, 0.19, 0.025), (0, 0.24, 0.216))
    for x, z, suffix in [(-0.163, -0.163, "NW"), (0.163, -0.163, "NE"),
                         (-0.163, 0.163, "SW"), (0.163, 0.163, "SE")]:
        m.box(f"Stone_Merlon{suffix}", (0.115, 0.14, 0.115), (x, 0.705, z))
    return m


def boundary() -> Model:
    """Low, almost plinth-wide tablet: twice the round pawn's head width.

    The slab leans back to show its broad face from the game's 60-degree camera.
    Its two faction grooves remain visible when color is reduced to gray.
    """
    m = Model("BoundaryBody")
    m.groups.append(("TabletTilt", ".", (0, 0.112, 0), (-14, 0, 0)))
    m.box("Shade_WideFoot", (0.69, 0.055, 0.22), (0, 0.0975, 0))
    m.box("Stone_BroadSlab", (0.63, 0.245, 0.115), (0, 0.1225, 0), parent="TabletTilt")
    m.cylinder("Stone_RoundShoulder", 0.315, 0.315, 0.115, (0, 0.248, 0),
               parent="TabletTilt", rotation=(90, 0, 0), scale=(1, 1, 0.50), segments=12)
    m.box("Faction_LowerGroove", (0.43, 0.035, 0.014), (0, 0.125, 0.066), parent="TabletTilt")
    m.box("Faction_UpperGroove", (0.43, 0.035, 0.014), (0, 0.230, 0.066), parent="TabletTilt")
    return m


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for file_name, model in [
        ("basic.tscn", basic()),
        ("fortress.tscn", fortress()),
        ("boundary.tscn", boundary()),
    ]:
        with (OUT / file_name).open("w", encoding="utf-8", newline="\n") as output:
            output.write(model.render())
        print(OUT / file_name)


if __name__ == "__main__":
    main()
