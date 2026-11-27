# Operating room asset sources

## Included assets

| Files | Origin and redistribution |
| --- | --- |
| `Environment/Models/*.asset` | Original operating-room meshes built from authored assembly geometry. No external meshes are embedded. |
| `Environment/Textures/{Floor,Fabric,Steel}{Color,Normal}.png` | Original tileable raster textures with material-specific color and normal detail. |
| `Environment/Textures/{Vitals,Anesthesia,Robot,WallStatus}.png` | Original fictional environmental screen graphics. The baked lettering is part of the image. Font files are absent from this contribution. |
| `Environment/Textures/{RoomSign,SupplySign,UtilitySign,WasteSign}.png` | Original raster signs for this operating room. |
| `Environment/Materials/*.mat` | Scene-specific material settings using the project's Unity URP shaders. The contribution contains no copies of the package shaders. |
| `OperatingRoomReflection.asset` and `OperatingRoomNavigation.asset` | An authored neutral cubemap and a Unity bake of the prototype's own collision geometry. |
| Demo scene, prefabs, and documentation screenshots | Original assemblies and captures of this contribution, using the project editor and render pipeline. |

All listed source assets are included for use and distribution with the authorized MedSim AI contribution. There are no purchased assets, manufacturer logos, imported model libraries, or Wraith VR assets in the contribution.

## References and candidate models

- Ghost Medical's project description and its published demonstration show the importance of a readable room, an identifiable robotic station, and clear views around the table. The supplied YouTube link could not be loaded during research. [Steam listing](https://store.steampowered.com/app/1136210/) · [Ghost Medical demonstration](https://www.ghostmedical.com/video/vr-surgery-total-knee)
- VA's orthopedic operating-room templates and FGI's application guidance informed the room size and procedure-space layout. [VA space design standards](https://cfm.va.gov/cfm/til/dGuide/dgSurgAppendixC.pdf) · [FGI application guidance](https://fgicodes.org/application-guidance/)
- The STERIS table dimensions, Getinge suspension and optics documents, and Dräger anesthesia workstation literature informed the size and functional forms. The geometry is original. [STERIS table](https://www.steris.com/healthcare/products/surgical-tables/steris-5085-srt-surgical-table) · [Getinge light range](https://www.getinge.com/dam/hospital/documents/english/surgical-light-range-product-brochure-anz-en-anz.pdf) · [Dräger workstation](https://www.draeger.com/Content/Documents/Products/PI/en_neeur/Atlan-A350-A350-XL-Product-Information-en-neeur.pdf)
- Candidate asset research covered a Sketchfab operating table marked CC Attribution, a CC0 Innerscene table, and Poly Haven's CC0 materials. The delivered table uses original geometry so the supports, collision, and mattress plane can be maintained together. No candidate files were included. [Sketchfab table](https://sketchfab.com/3d-models/operating-table-e3a4512227d34b098e2ef0d11c8a80fd) · [Innerscene table](https://www.innerscene.com/tools/library/3d-parts/operating-table-59014fec) · [Poly Haven license](https://polyhaven.com/license)
