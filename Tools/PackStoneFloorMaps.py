#!/usr/bin/env python3
"""Pack supplied floor_pattern_01 maps for URP Lit. Requires OpenEXR, numpy, Pillow."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import OpenEXR
from PIL import Image


def load_exr(path, channel):
    channels = OpenEXR.File(str(path)).channels()
    if channel not in channels:
        raise ValueError(f"{path}: missing EXR channel {channel}")
    pixels = channels[channel].pixels.astype(np.float32)
    if not np.isfinite(pixels).all():
        raise ValueError(f"{path}: non-finite EXR values")
    return pixels


def pack(source, output):
    arm_path = source / "floor_pattern_01_arm_4k.jpg"
    rough_path = source / "floor_pattern_01_rough_4k.exr"
    normal_path = source / "floor_pattern_01_nor_gl_4k.exr"
    bump_path = source / "floor_pattern_01_bump_4k.exr"
    arm = np.array(Image.open(arm_path).convert("RGB"))
    # These grayscale EXRs store luminance in Y, not R. Unity's RGB import
    # does not expose that channel correctly; decode Y explicitly offline.
    rough = np.clip(load_exr(rough_path, "Y"), 0, 1)
    if rough.shape != arm.shape[:2]:
        raise ValueError("ARM and roughness dimensions must match")
    packed = np.zeros((*rough.shape, 4), dtype=np.uint8)
    packed[:, :, 0] = arm[:, :, 2]  # URP metallic R <- ARM metallic B
    packed[:, :, 1] = arm[:, :, 0]  # URP occlusion G <- ARM AO R
    packed[:, :, 3] = np.rint((1 - rough) * 255).astype(np.uint8)
    output.mkdir(parents=True, exist_ok=True)
    Image.fromarray(packed).save(output / "FloorMetallicOcclusionSmoothness.png")

    normal = load_exr(normal_path, "RGBA")
    bump = load_exr(bump_path, "Y")
    # EXR rows run down while tangent-space V runs up. A GL normal's Y
    # therefore correlates with +d(height)/d(image row). Check broad stone
    # relief at 256px; the bump map also contains finer independent detail.
    check_size = (256, 256)
    ny = np.asarray(Image.fromarray(normal[:, :, 1]).resize(check_size, Image.Resampling.BOX)) * 2 - 1
    height = np.asarray(Image.fromarray(bump).resize(check_size, Image.Resampling.BOX))
    derivative = height[2:, 1:-1] - height[:-2, 1:-1]
    agreement = float(np.corrcoef(derivative.ravel(), ny[1:-1, 1:-1].ravel())[0, 1])
    if not np.isfinite(agreement) or agreement <= 0:
        raise ValueError(f"Normal Y does not agree with GL height gradient: {agreement}")
    report = {
        "dimensions": [int(arm.shape[1]), int(arm.shape[0])],
        "packing": {"R": "ARM.B metallic", "G": "ARM.R ambient occlusion", "B": "0 (unused)", "A": "1 - clamp(Roughness EXR.Y, 0, 1)"},
        "roughness_mean": float(rough.mean()),
        "normal_y_height_gradient_correlation_256": agreement,
        "normal_flip_green_channel": False,
        "source_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [arm_path, rough_path, normal_path, bump_path]},
    }
    (output / "PackingReport.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    root = Path(__file__).resolve().parent.parent / "ProjectC.Client/Assets/Art/Environment"
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=root / "Textures/floor_pattern_01_4k")
    parser.add_argument("--output", type=Path, default=root / "BattleField/SurroundingStone")
    arguments = parser.parse_args()
    pack(arguments.source, arguments.output)
