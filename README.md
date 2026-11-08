# ksPlugPoseForce

English / [日本語](README_ja.md)

BepInEx plugins that adjust the body pose during intercourse in H scenes, using a table of corrections per position and state (raise and arch the hips, open the legs, move the man's hips, attach the hands, and so on). The correction strength (0 to 1) is set per position and remembered.
It is the Koikatsu Sunshine counterpart of the pose correction that **kPlug** (by Katarsys) offers in Koikatu.

Two plugins, one per game:

| Folder | Game | Processes | Role |
|---|---|---|---|
| [ksPlugPoseForce](ksPlugPoseForce) | Koikatsu Sunshine | `KoikatsuSunshine` | Applies the corrections, in-game editor, import |
| [kPlugPoseForceExport](kPlugPoseForceExport) | Koikatu / Koikatsu Party | `Koikatu`, `Koikatsu Party` | Writes the corrections of the running kPlug to a TSV file |

**No correction values are included.** Before first use, either export the values from kPlug with kPlugPoseForceExport and import the file in ksPlugPoseForce, or enter your own values in the in-game editor.

- ksPlugPoseForce works with or without ksPlug. Normal mode only (no VR).
- Supported: 2-person intercourse and the standard 3-person mode (two girls and a man), including anal states that are in the table. Not supported: MMF, invited characters added by ksPlug in 2-person mode, service, massage, VR.
- kPlugPoseForceExport needs kPlug in normal mode. It reads kPlug at run time and does not reference kPlug's files.
- Tested in game: ksPlugPoseForce in Koikatsu Sunshine normal mode with ksPlug 1.7.3; kPlugPoseForceExport in Koikatu normal mode with kPlug 3.6.4 (3.6.3 gives the same table). Not tested: Koikatsu Party.
- Versions: ksPlugPoseForce 0.1.3, kPlugPoseForceExport 0.1.1

## How it works

- The corrections are added to the bones and IK targets right before the game solves the body IK, so hands and feet stay attached.
- Each row of the table is the maximum offset (position in m, rotation in degrees) for one position (animation asset), person, state and body part. The strength scales it.
- Hands can be attached to another bone (for example the man's hands to the girl's thighs) with an offset.
- The table is a plain TSV file (`BepInEx\config\ksPlugPoseForce\PoseForce.tsv`). Rows you edit are kept when you import a newer export.
- **Sharing**: write your own values to a file (`export\PoseForceShare_<name>_<date>.tsv`) and give it to others; they put it in their `import` folder and import it. Only rows you edited are written (rows equal to kPlug's export are left out). When the same item has several values, the order is: your own > shared data > kPlug > built-in.

## Install

1. Install BepInEx 5 and ConfigurationManager.
2. Koikatsu Sunshine: copy `ksPlugPoseForce.dll` from the [Releases](../../releases) ZIP to `BepInEx\plugins\ksPlugPoseForce\`.
3. To use kPlug's values: in Koikatu, copy `kPlugPoseForceExport.dll` to `BepInEx\plugins\kPlugPoseForceExport\`, start an H scene in normal mode and press **「kPlug の姿勢補正の値を書き出す」** (Export kPlug's pose correction values; ConfigurationManager → `kPlug Pose Force Export`). Copy the written `BepInEx\config\kPlugPose_kPlug<version>.tsv` to Koikatsu Sunshine's `BepInEx\config\ksPlugPoseForce\import\` and import it with **「書き出しの取り込み画面を開く…」** (open the import dialog) in ksPlugPoseForce's settings.
4. In Koikatsu Sunshine, turn on `EnablePoseForce` (ConfigurationManager → `ksPlug Pose Force` → `PoseForce`), press **「調整の画面を開く」** (open the adjustment window) and raise the strength.

Settings: `BepInEx\config\kkttspj.kks.ksplugposeforce.cfg`. All keys (`WindowKey`, `EditorWindowKey`, `ForceUpKey`, `ForceDownKey`) are unassigned by default.
The button labels and the guides are in Japanese: see [README_ja.md](README_ja.md) and the guides in [docs](docs).

## Build

Place a plugin folder directly under the game folder and run its `build_and_deploy.ps1` (needs the .NET SDK).
The projects reference `..\..\BepInEx\core\` and `..\..\KoikatsuSunshine_Data\Managed\` (ksPlugPoseForce) or `..\..\Koikatu_Data\Managed\` (kPlugPoseForceExport). They do not reference kPlug or ksPlug. The DLL is copied to `..\Deploy\<plugin name>\`.

## License

MIT ([LICENSE](LICENSE)). No warranty.
kPlug and ksPlug are separate works by their author and are not included here, and neither are kPlug's correction values. Game files, BepInEx and other plugins are not included either.
