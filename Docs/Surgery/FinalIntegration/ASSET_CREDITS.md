# Asset credits: integrated hospital

One list for everything shipped in the integrated build. Detailed source tables, with every licence checked on its source page, stay with each contribution:
- clinical v0.6: [`ASSET_CREDITS.md`](../../../ASSET_CREDITS.md) (project root);
- Person 2: [`Docs/Surgery/Anatomy/ASSET_LICENSES.md`](../Anatomy/ASSET_LICENSES.md);
- Person 1: [`Docs/Surgery/Environment/ASSET_LICENSES.md`](../Environment/ASSET_LICENSES.md).

**No asset was purchased.** None was downloaded with an account, and nothing was taken from Wraith VR or any commercial simulator.

## Added by this integration
| Asset | Source | Licence | Used for | Modifications |
|---|---|---|---|---|
| Avatar **`Medical_Male_04`** (FBX and 6 textures) | Microsoft Rocketbox Avatar Library, https://github.com/microsoft/Microsoft-Rocketbox | **MIT**, Copyright (c) 2020 Microsoft | Sami, surgical assistant (Room 02) | Textures converted to PNG; URP materials (`Sami_*`); humanoid import. No geometry changes. |
| Hospital lobby, corridor extension, directory, room signs, sliding doors | Generated in-project (`HospitalBuilder.cs`) | Project | Hospital | Original |

The MIT licences require the copyright notice and licence text to ship with any distribution. Until this integration they were only in the git-ignored `AssetStaging/` and the package cache, so no earlier build carried them (a gap since v0.2). They are now in [`Licenses/`](../../../Licenses/) in the repository and were copied into a `Licenses/` folder next to both integrated executables:
- `Microsoft-Rocketbox_MIT.txt`
- `uLipSync_MIT.txt`
- `Z-Anatomy_derivatives_CC-BY-SA-4.0.txt`

The build scripts do not copy them yet, so copy them by hand for a new build. An in-app credits screen does not exist yet.

## Credits screen text
> **Characters:** Microsoft Rocketbox Avatar Library (MIT, © 2020 Microsoft). Lip sync: uLipSync by hecomi (MIT).
>
> **Knee anatomy and the derived generic implant meshes:** adapted from *Z-Anatomy – The libre 3D atlas of anatomy* (CC BY-SA 4.0), based on *BodyParts3D © The Database Center for Life Science* (CC BY 4.0).
>
> **Surgical patient:** MakeHuman / MPFB2 system assets (CC0).
>
> **Hospital furniture models** (CC BY 4.0, via Sketchfab):
> - "Simple Hospital Bed" by Yvo Pors
> - "HospitalDoor" by azizcharfeddine1997
> - "Chair Hospital" by Khafesus
> - "Covid-19 Sanitizer Station" by Marco669966
> - "Pediatric Code Cart" by Mikpultro
> - "Adjustable Bedside Table" by Ethan Cragun
> - "IV Pole" by Mouch
> - "Rigged Stethoscope" by Simon Pasi
> - "Sphygmomanometer Low Poly" by TheGiwi
> - "Pulse Oximeter | Free Model" by Nofil.Khan
>
> **Textures, HDRI and models:** Poly Haven and ambientCG (CC0).
>
> **Sounds:** Kenney and BigSoundBank (CC0).
>
> **Fonts:** Noto Sans Arabic and Noto Kufi Arabic (SIL OFL 1.1).
>
> Operating room, instruments, tray, drapes and procedural textures: made for MedSim AI.

## Summary by contribution
| Contribution | Third-party content | Licence obligations |
|---|---|---|
| Clinical v0.6 (corridor, Room 01, Dr. Hassan, Mr. Reyes) | Rocketbox avatars and animations (MIT), uLipSync (MIT), 10 Sketchfab models (CC BY 4.0), Poly Haven, ambientCG, Kenney, BigSoundBank (CC0), Noto fonts (OFL) | MIT notices; CC BY attribution (above) |
| Person 1: operating room | None. All meshes, textures and screen graphics are original. | — |
| Person 2: patient, knee, implants, instruments | Z-Anatomy (CC BY-SA 4.0), BodyParts3D (CC BY 4.0), MakeHuman/MPFB2 (CC0), ambientCG (CC0) | Attribution. **Adapted anatomy and implant meshes stay CC BY-SA 4.0** (licence file in `Surgery/Anatomy/Models/Knee/`). |
| Person 3: mechanics | Code only | — |
| Person 4: AI, HUD, proxy | Code only. Characters reuse the project's Rocketbox avatars. | — |
| Integration | Rocketbox `Medical_Male_04` (MIT) | MIT notice |

## Packages and services
- XR Interaction Toolkit 3.7.0 and its Starter Assets (Unity Companion License).
- HTC VIVE OpenXR Plugin (HTC VIVE SDK licence; review before commercial distribution).
- AI Navigation.
- Runtime AI services through the MedSim proxy. These are as documented in the root `ASSET_CREDITS.md` and were not changed by this integration.

## Not shipped
- No pre-recorded character dialogue.
- No manufacturer implant CAD or logos.
- No paid store assets.
- Nothing from Wraith VR: it was used only as a public visual reference by Persons 1 and 2.
