"""Makes the game's surface detail textures: tileable greyscale detail (R = shade, G = roughness,
B = height) and a matching normal map for each surface kind. They multiply the colour the game
already paints, so tinting, uniforms and the Decorate finishes keep working.

    python3 tools/Textures/make_surfaces.py game/Art/Surfaces
"""
import os
import sys

import numpy as np
from PIL import Image

N = 256
rng = np.random.default_rng(7)


def noise(scale, aniso=(1.0, 1.0)):
    """Tileable band-limited noise: white noise filtered in frequency space (so it wraps)."""
    white = rng.standard_normal((N, N))
    fy = np.fft.fftfreq(N)[:, None] * aniso[1]
    fx = np.fft.fftfreq(N)[None, :] * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    filt = np.exp(-(f * scale) ** 2)
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
    out -= out.mean()
    return out / (out.std() + 1e-9)


def norm01(a):
    return (a - a.min()) / (a.max() - a.min() + 1e-9)


def save(folder, name, shade, rough, height, strength):
    """shade: multiplier around 1; rough 0-1; height any (normalised); strength of the bumps."""
    shade = np.clip((shade - 0.75) / 0.5, 0, 1)  # 0.75..1.25 -> 0..1
    h = norm01(height)
    detail = np.stack([shade, np.clip(rough, 0, 1), h], -1)
    Image.fromarray((detail * 255).astype(np.uint8)).save(os.path.join(folder, f"{name}_detail.png"))
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    n = np.stack([-gx, gy, nz], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(os.path.join(folder, f"{name}_normal.png"))


def main(folder):
    os.makedirs(folder, exist_ok=True)
    y, x = np.mgrid[0:N, 0:N] / N

    # 0 plain: nothing (people, signs)
    save(folder, "0_plain", np.ones((N, N)), np.full((N, N), 0.85), np.zeros((N, N)), 0)

    # 1 wall paint: soft orange peel, faint mottling
    peel = noise(0.75)
    mott = noise(60)
    save(folder, "1_paint", 1 + 0.02 * mott + 0.006 * peel, 0.72 + 0.06 * mott, peel, 0.5)

    # 2 wood: grain along x, rings, pores
    warp = noise(75) * 0.004
    rings = np.sin((y + warp + 0.002 * noise(10)) * 2 * np.pi * 16)
    fine = noise(1, aniso=(12.0, 1.0))
    grain = 0.6 * rings + 0.4 * fine
    save(folder, "2_wood", 1 + 0.07 * grain + 0.03 * noise(60), 0.55 + 0.08 * grain, grain, 3)

    # 3 fabric: woven twill
    weave = np.sin(x * 2 * np.pi * 64) * np.sin(y * 2 * np.pi * 64)
    twill = np.sin((x + y) * 2 * np.pi * 48)
    fuzz = noise(1)
    fab = 0.5 * weave + 0.3 * twill + 0.3 * fuzz
    save(folder, "3_fabric", 1 + 0.06 * fab + 0.03 * noise(50), 0.95 + 0.03 * fuzz, fab, 5)

    # 4 brushed metal: long streaks along x
    streak = noise(0.75, aniso=(60.0, 1.0))
    save(folder, "4_metal", 1 + 0.05 * streak, 0.32 + 0.06 * streak, streak, 1.5)

    # 5 vinyl floor: tiny flecks, soft wear, very glossy
    fleck = (noise(0.6) > 2.3).astype(float) - (noise(0.6) > 2.5).astype(float)
    wear = noise(70)
    save(folder, "5_vinyl", 1 + 0.06 * fleck + 0.025 * wear, 0.16 + 0.1 * norm01(wear) + 0.04 * noise(15), 0.5 * noise(1) + 0.5 * noise(12.5), 2)

    # 6 ceiling tile: mineral fibre fissures
    fis = noise(2, aniso=(1.0, 3.0))
    pits = (noise(0.75) < -2.2).astype(float)
    save(folder, "6_ceiling", 1 - 0.05 * pits + 0.02 * fis, np.full((N, N), 0.95), fis - 2 * pits, 4)

    # 7 laminate / plastic: fine stipple, satin
    stip = noise(0.75)
    save(folder, "7_laminate", 1 + 0.006 * stip + 0.012 * noise(60), 0.45 + 0.05 * noise(60), stip, 0.12)

    # 8 rubber / dark: matt, fine grain
    g = noise(1)
    save(folder, "8_rubber", 1 + 0.03 * g, 0.88 + 0.04 * g, g, 2)

    # 9 tiles (wall tiles, splashbacks): glazed, faint ripple
    rip = noise(30)
    save(folder, "9_glaze", 1 + 0.015 * rip, 0.15 + 0.05 * norm01(rip), rip, 1)

    # 10 carpet tiles: fine loop pile, matt, mottled
    pile = noise(0.5)
    save(folder, "10_carpet", 1 + 0.08 * pile + 0.04 * noise(20), 0.97 + 0.02 * pile, pile, 3)

    # 11 terrazzo: stone chips set in resin, polished
    chips = np.zeros((N, N))
    for scale, cut, weight in ((3, 1.6, 0.18), (2, 1.9, -0.14), (4, 2.0, 0.1)):
        chips += weight * (noise(scale) > cut)
    save(folder, "11_terrazzo", 1 + chips + 0.02 * noise(40), 0.22 + 0.06 * norm01(noise(60)), chips, 1.5)

    # 12 safety vinyl: grippy grit flecks, satin
    grit = (noise(0.4) > 2.0).astype(float)
    save(folder, "12_safety", 1 + 0.05 * grit - 0.03 * (noise(0.4) < -2.2) + 0.02 * noise(70), 0.5 + 0.15 * grit, grit + 0.3 * noise(1), 3)

    # 13 ceramic floor tiles: glazed, slightly worn
    save(folder, "13_ceramic", 1 + 0.015 * noise(30) + 0.01 * noise(1.5), 0.3 + 0.08 * norm01(noise(50)), noise(20), 0.8)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "game/Art/Surfaces")
