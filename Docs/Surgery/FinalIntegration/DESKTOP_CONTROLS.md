# Desktop controls: integrated hospital

**Run:** `D:/VRBuilds/MedSimAI_Integrated_Desktop/MedSimAI.exe`. It starts in the reception lobby.

Start the AI proxy first for live voices (see the root README). Without it the app runs in a clearly labelled text-only mode.

## Everywhere
| Key / mouse | Action |
|---|---|
| W A S D + mouse | Walk and look (Shift: faster) |
| Hold **T** | Talk to the person you are looking at (push-to-talk) |
| **M** | Microphone mode: push-to-talk / hands-free / muted |
| **E** | Interact (doors, buttons; in Room 01, instruments and the ECG cart) |
| **Esc** | Pause menu (language, subtitles, restart; in Room 02 also "Back to the corridor" and "Finish and see debrief") |
| **F1** / **H** (outside Room 02) | Show or hide the controls help |
| **F10** | Back to the corridor (in the hospital; in the standalone scenes it opens the scenario menu) |

## Hospital navigation
| Key | Action |
|---|---|
| **1** | Ask the doctor to take you to Room 01 (clinical examination) |
| **2** | Ask the doctor to take you to Room 02 (surgical training) |
| **3** | Ask the doctor to take you back to the corridor |
| **F2** | Switch between guided training and independent assessment |
| Click the directory buttons | Same as 1 / 2 / mode / language |

You can also just say it (hold T):
- «خديني على غرفة العمليات», «بدي أروح على الغرفة الأولى», «رجعني عالممر», «وين لازم أروح؟», «استني», «امشي معي»
- "take me to the operating room", "room one", "back to the corridor", "wait", "walk with me", "what rooms are there?"

## Room 01: clinical examination
| Key / mouse | Action |
|---|---|
| Left click | Grab or apply an instrument (cuff → upper arm, oximeter → fingertip, stethoscope → chest) |
| Right click | Put the instrument back |
| E (or left click) on the NIBP monitor | START the blood-pressure measurement once the cuff is on the upper arm (new; VR: poke or select the monitor) |
| E on the ECG cart | Record ECG |

## Room 02: surgical training
| Key / mouse | Action |
|---|---|
| **Left click** (empty hand) | Pick up the instrument or implant under the crosshair |
| **Left click** (holding) | Apply the instrument to the target under the crosshair, or place the implant |
| **E** (holding saw or drill) | Switch the powered instrument on or off |
| **Q** | Drop or put down what you are holding |
| Mouse wheel | Move the held item nearer or farther |
| Hold **right mouse** | Rotate the held item |
| **H** | Ask the supervisor for help (counted as a hint) |
| **J** | Ask the supervisor to explain the current step |
| **R** | Restart the surgical scenario (tissue, implants and instruments reset) |
| Hold **T** | Talk to Dr. Hassan or Sami, for example «سامي، أعطيني دليل القطع», "pass me the saw", "what's next?" |

## Command-line options
| Option | Effect |
|---|---|
| `-clinical-only` | Start the standalone v0.6 clinical scene instead of the hospital |
| `-desktop` | Force desktop mode (useful for the PCVR build without a headset) |
| `-hospitalSelfTest <dir>` | Run the scripted journey, writing screenshots, GIF frames and a log to `<dir>` |
| `-uncapped` | With the self-test: try to disable vSync for timing |
