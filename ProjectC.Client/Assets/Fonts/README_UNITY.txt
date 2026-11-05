ProjectC / Unity TextMeshPro font pack

Recommended assignments:
  Title / card name Korean: NotoSerifKR-Bold.ttf
  Title / card name Japanese: NotoSerifJP-Bold.ttf
  Body / HUD Korean: Pretendard-Medium.ttf or Pretendard-SemiBold.ttf
  Body / HUD Japanese: PretendardJP-Medium.ttf or PretendardJP-SemiBold.ttf
  HP / stats / resource numbers: Cinzel-Bold.ttf

The *-VF.ttf files are original variable fonts. The *-Bold.ttf
files are weight-700 static TrueType instances generated from them
for easier use with TextMeshPro.

Use Create > TextMeshPro > Font Asset. For CJK, put the UI's known
strings in static atlases where practical; use a dynamic fallback
for strings not known ahead of time, and verify runtime device builds.

Sources:
  https://github.com/orioncactus/pretendard
  https://github.com/google/fonts/tree/main/ofl/notoserifkr
  https://github.com/google/fonts/tree/main/ofl/notoserifjp
  https://github.com/google/fonts/tree/main/ofl/cinzel

Licenses: SIL Open Font License 1.1 (see LICENSE_*.txt).
Retain relevant OFL copyright/license notices when redistributing
the font files with the game. Fonts are licensed separately from
your game code and assets.
