# Performance report

## Measured: integrated Desktop executable
- **Machine:** Windows 11, **NVIDIA GeForce RTX 3050 Laptop GPU (4 GB)**, 1920 × 1080, URP.
- **Build:** `D:/VRBuilds/MedSimAI_Integrated_Desktop/MedSimAI.exe`, run with `-hospitalSelfTest`.
- **Method:** about 1,000–1,200 frames per area, after a settle period. GPU times come from Unity's `FrameTimingManager`.

| Area | Frames | Frame p50 | Frame p95 | Frame max | **GPU p50** | **GPU p95** |
|---|---|---|---|---|---|---|
| Reception lobby | 1198 | 4.16 ms | 4.19 ms | 8.3 ms | **1.49 ms** | **2.00 ms** |
| Corridor | 961 | 4.16 ms | 4.18 ms | 95.9 ms (one hitch) | **1.68 ms** | **2.34 ms** |
| Operating room, surgeon view | 1201 | 4.16 ms | 4.19 ms | 4.8 ms | **3.79 ms** | **4.08 ms** |
| Room 01 (clinical) | 1198 | 4.16 ms | 4.18 ms | 8.4 ms | **2.01 ms** | **2.39 ms** |

### How to read these numbers
- **The frame times are capped by vSync** at the laptop panel's 240 Hz (4.16 ms). The `-uncapped` option did not lift the cap on this machine. So the CPU/frame column shows "at least 240 fps on this desktop" and says nothing about the uncapped CPU cost.
  - The **GPU column is the useful figure**. The operating room is the heaviest area at about 3.8 ms of GPU time per frame at 1080p.
- **Corridor hitch:** a single 96–108 ms frame while the guided walk starts, seen in two runs. Likely causes are the first NavMesh path request plus the first speech/subtitle layout. It was not profiled further. In VR a hitch like this would be visible as a dropped frame.
- **This is Desktop FPS, not headset FPS.**
  - VIVE Focus 3 via streaming renders two eyes at a much higher resolution (about 2448 × 2448 per eye at 100 %), plus encoding.
  - A rough scale-up of the OR GPU time suggests the RTX 3050 Laptop is **at or beyond its budget** for 90 Hz in the operating room without lowering render scale.
  - **This was not measured.** It needs a headset test (see [VR_MANUAL_TEST_CHECKLIST](VR_MANUAL_TEST_CHECKLIST.md)).

## Earlier measurements by contributors (not re-run by this integration)
- **Person 1:** operating room alone on an **Apple M3** (macOS development player, Metal). See `Docs/Surgery/Environment/TEST_REPORT.md` and `Performance/mac-m3-1080p.csv`. Different hardware and graphics API, so not comparable with the table above.
- **Person 2:** knee and instrument triangle budgets, in `Docs/Surgery/Anatomy/TEST_REPORT.md`.
- **v0.6 clinical exe:** see `EMBODIED_AI.md` (project root).

## Build sizes
| Build | Size |
|---|---|
| Desktop | 405 MB |
| PCVR | 408 MB |

The surgical additions (anatomy, instruments, OR room, assistant textures) account for most of the growth over v0.6. The assistant's six PNG textures are about 24 MB of source assets. They were not compressed further.

## Not done
- Uncapped CPU profiling.
- Memory profiling.
- Profiling in a headset.
- Testing on the minimum-spec PC.
- LODs for the knee anatomy (one LOD only).
- Occlusion culling between the rooms. Both rooms render when they are in view, and the doors carve the NavMesh but do not cull.
