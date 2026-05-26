# Thetis Midi2Cat Command Reference — Zeus Cross-Match

**Source:** [`ramdor/Thetis` — `Midi2Cat.Data/CatCmdDb.cs`](https://github.com/ramdor/Thetis/blob/3759d096067b7574550b963c1e7a22003da2ab00/Project%20Files/Source/Midi2Cat/Midi2Cat.Data/CatCmdDb.cs)
**Date:** 2026-05-25
**Purpose:** Inventory of every Midi2Cat command in Thetis, cross-referenced against Zeus's existing API surface. This is the input for the MIDI plugin PRD.

## Legend

- **Control type:** `B` = Button (toggle/momentary), `W` = Wheel (relative encoder), `K` = Knob/Slider (absolute 0–127)
- **Zeus status:**
  - `READY` — Zeus API endpoint exists and is functional
  - `PARTIAL` — Zeus has the underlying capability but the exact command shape differs
  - `MISSING` — no Zeus API surface for this command
  - `N/A` — not applicable to Zeus (e.g. WinForms UI, RX2 on single-RX radios)

## 1. VFO Control

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 1 | `ChangeFreqVfoA` | Change Freq Vfo A | W | `POST /api/vfo` | READY | |
| 2 | `ChangeFreqVfoB` | Change Freq Vfo B | W | — | MISSING | Zeus has no VFO B (`VfoSetRequest` is VFO A only) |
| 3 | `MultiStepVfoA` | Multi Step Vfo A | W | `POST /api/vfo` | READY | Larger step increments |
| 4 | `VfoAtoB` | VfoA To B | B | — | MISSING | |
| 5 | `VfoBtoA` | VfoB To A | B | — | MISSING | |
| 6 | `VfoSwap` | Vfo Swap | B | — | MISSING | |
| 7 | `VfoSyncOnOff` | Vfo Sync On Off | B | — | MISSING | |
| 8 | `LockVFOOnOff` | Toggle VFO Lock | B | — | MISSING | |
| 9 | `LockVFOAOnOff` | Lock VFO A | B | — | MISSING | |
| 10 | `LockVFOBOnOff` | Lock VFO B | B | — | MISSING | |
| 11 | `MoveVFOADown100Khz` | Move VFOA Down 100Khz | B | `POST /api/vfo` | READY | Compute freq offset client-side |
| 12 | `MoveVFOAUp100Khz` | Move VFOA Up 100Khz | B | `POST /api/vfo` | READY | Compute freq offset client-side |
| 13 | `MoveVFOBDown100Khz` | Move VFOB Down 100Khz | B | — | MISSING | Zeus has no VFO B |
| 14 | `MoveVFOBUp100Khz` | Move VFOB Up 100Khz | B | — | MISSING | Zeus has no VFO B |
| 15 | `TuningStepUp` | Tuning Step Up | B | — | MISSING | No tuning-step API |
| 16 | `TuningStepDown` | Tuning Step Down | B | — | MISSING | |
| 17 | `ZeroBeatPress` | Zero Beat | B | — | MISSING | |
| 18 | `CTunOnOff` | Click Tune On Off | B | — | MISSING | |
| 19 | `SwapVFOWheels` | Swap VFO Wheels | B | — | N/A | MIDI-internal preference |
| 20 | `ToggleVFOWheel` | Toggle Wheel to VFOA/VFOB | B | — | N/A | MIDI-internal preference |

## 2. Band Selection

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 21 | `BandUp` | Band Up | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | No direct band-switch API; plugin reads band memory, computes next band freq, posts VFO |
| 22 | `BandDown` | Band Down | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | Same as BandUp but previous band |
| 23 | `Band160m` | Band 160m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | Plugin reads band memory for 160m, posts freq to VFO |
| 24 | `Band80m` | Band 80m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 25 | `Band60m` | Band 60m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 26 | `Band40m` | Band 40m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 27 | `Band30m` | Band 30m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 28 | `Band20m` | Band 20m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 29 | `Band17m` | Band 17m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 30 | `Band15m` | Band 15m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 31 | `Band12m` | Band 12m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 32 | `Band10m` | Band 10m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 33 | `Band6m` | Band 6m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 34 | `Band2m` | Band 2m | B | `GET /api/bands/memory` + `POST /api/vfo` | PARTIAL | |
| 35 | `Rx2BandUp` | Rx2 Band Up | B | — | N/A | RX2 not implemented |
| 36 | `Rx2BandDown` | Rx2 Band Down | B | — | N/A | |
| 37–46 | `Band160mRX2`..`Band2mRX2` | RX2 Band *x* | B | — | N/A | RX2 not implemented |

## 3. Mode Selection

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 47 | `Rx1ModeNext` | Rx1 Mode Next | B | `GET /api/state` + `POST /api/mode` | PARTIAL | Endpoint takes absolute `RxMode`, not next/prev; plugin must read current mode and compute next |
| 48 | `Rx1ModePrev` | Rx1 Mode Prev | B | `GET /api/state` + `POST /api/mode` | PARTIAL | Same, compute previous mode |
| 49 | `ModeSSB` | Mode SSB | B | `POST /api/mode` | READY | Direct mode select |
| 50 | `ModeLSB` | Mode LSB | B | `POST /api/mode` | READY | |
| 51 | `ModeUSB` | Mode USB | B | `POST /api/mode` | READY | |
| 52 | `ModeDSB` | Mode DSB | B | `POST /api/mode` | READY | |
| 53 | `ModeCW` | Mode CW | B | `POST /api/mode` | READY | |
| 54 | `ModeCWL` | Mode CWL | B | `POST /api/mode` | READY | |
| 55 | `ModeCWU` | Mode CWU | B | `POST /api/mode` | READY | |
| 56 | `ModeFM` | Mode FM | B | `POST /api/mode` | READY | |
| 57 | `ModeAM` | Mode AM | B | `POST /api/mode` | READY | |
| 58 | `ModeDIGU` | Mode DIGU | B | `POST /api/mode` | READY | |
| 59 | `ModeSPEC` | Mode SPEC | B | `POST /api/mode` | READY | |
| 60 | `ModeDIGL` | Mode DIGL | B | `POST /api/mode` | READY | |
| 61 | `ModeSAM` | Mode SAM | B | `POST /api/mode` | READY | |
| 62 | `ModeDRM` | Mode DRM | B | `POST /api/mode` | READY | |
| 63–76 | `RX2Mode*` | RX2 Mode * | B | — | N/A | RX2 not implemented |
| 77 | `Rx2ModeNext` | RX2 Mode Next | B | — | N/A | |
| 78 | `Rx2ModePrev` | RX2 Mode Prev | B | — | N/A | |

## 4. Filter Control

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 79 | `Rx1FilterWider` | Rx1 Filter Wider | B | `GET /api/state` + `POST /api/bandwidth` | PARTIAL | Endpoint takes absolute `(Low, High)`, not relative; plugin must read current and widen |
| 80 | `Rx1FilterNarrower` | Rx1 Filter Narrower | B | `GET /api/state` + `POST /api/bandwidth` | PARTIAL | Same, narrow |
| 81 | `FilterBandwidth` | FilterBandwidth | W | `POST /api/bandwidth` | READY | |
| 82 | `FilterHigh` | Filter High | W | `POST /api/filter` | READY | |
| 83 | `FilterLow` | Filter Low | W | `POST /api/filter` | READY | |
| 84 | `FilterShift` | Filter Shift | K | `POST /api/filter` | PARTIAL | Shift = move both edges |
| 85 | `TXFilterHigh` | TX Filter high | W | `POST /api/tx-filter` | READY | |
| 86 | `TXFilterLow` | TX Filter low | W | `POST /api/tx-filter` | READY | |
| 87 | `Rx2FilterWider` | RX2 Filter Wider | B | — | N/A | |
| 88 | `Rx2FilterNarrower` | RX2 Filter Narrower | B | — | N/A | |

## 5. RX Audio & Gain

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 89 | `SetAFGain` | AF Gain | K | `POST /api/rx/afGain` | READY | |
| 90 | `VolumeVfoA` | Volume VfoA | K | `POST /api/rx/afGain` | READY | |
| 91 | `VolumeVfoB` | Volume VfoB | K | — | MISSING | No separate VFO B volume |
| 92 | `VolumeVfoA_inc` | Volume VfoA Incr | W | `POST /api/rx/afGain` | READY | |
| 93 | `VolumeVfoB_inc` | Volume VfoB Incr | W | — | MISSING | |
| 94 | `MuteOnOff` | Mute On Off | B | `POST /api/audio/native/mute` | READY | |
| 95 | `RatioMainSubRx` | Ratio Main Sub Rx | K | — | MISSING | |
| 96 | `RX2Volume` | RX2 Volume | K | — | N/A | |
| 97 | `MuteRX2OnOff` | RX2 Mute On Off | B | — | N/A | |
| 98 | `AudioAmpOnOff` | Audio Amp On Off | B | — | MISSING | |

## 6. AGC

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 99 | `AGCLevel` | RX1 AGC Level | K | `POST /api/agcGain` | READY | |
| 100 | `AGCLevel_inc` | RX1 AGC Level Incr | W | `POST /api/agcGain` | READY | |
| 101 | `AGCModeUp` | AGC Mode Up | B | — | MISSING | No AGC mode cycle API |
| 102 | `AGCModeDown` | AGC Mode Down | B | — | MISSING | |
| 103 | `AGCModeKnob` | AGC Mode | K | — | MISSING | |
| 104 | `RX1AutoAGC` | RX1 Auto AGC compensation | B | `POST /api/auto-agc` | READY | |
| 105 | `RX2AGCLevel` | RX2 AGC Level | K | — | N/A | |
| 106 | `RX2AGCLevel_inc` | RX2 AGC Level Incr | W | — | N/A | |
| 107 | `RX2AGCModeUp` | RX2 AGC Mode Up | B | — | N/A | |
| 108 | `RX2AGCModeDown` | RX2 AGC Mode Down | B | — | N/A | |
| 109 | `RX2AGCModeKnob` | RX2 AGC Mode | K | — | N/A | |
| 110 | `RX2AutoAGC` | RX2 Auto AGC | B | — | N/A | |

## 7. Preamp & Attenuator

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 111 | `PreAmpSettingsKnob` | PreAmp Setting | K | `POST /api/preamp` | READY | |
| 112 | — (no Thetis MIDI cmd) | Attenuator | — | `POST /api/attenuator` | READY | Zeus has it, Thetis Midi2Cat doesn't |
| 113 | — | Auto Attenuator | — | `POST /api/auto-att` | READY | Zeus has it, Thetis Midi2Cat doesn't |

## 8. Noise Reduction & Blanker

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 114 | `NoiseReductionOnOff` | NR1 On Off | B | `POST /api/rx/nr` | PARTIAL | Endpoint takes full `NrConfig` (mode, ANF, SNB, NB…); plugin must read-modify-write to toggle NR mode |
| 115 | `NoiseReduction2OnOff` | NR2 On Off | B | `POST /api/rx/nr` | PARTIAL | Same read-modify-write pattern |
| 116 | `NoiseReduction3OnOff` | NR3 On Off | B | `POST /api/rx/nr` | PARTIAL | Same read-modify-write pattern |
| 117 | `NoiseReduction4OnOff` | NR4 On Off | B | `POST /api/rx/nr4` | READY | |
| 118 | `NoiseReduction4Amount` | NR4 Amount | K | `POST /api/rx/nr4` | READY | |
| 119 | `Rx1NoiseBlanker1OnOff` | Rx1 NB1 On Off | B | — | MISSING | No NB toggle API |
| 120 | `Rx1Noiseblanker2OnOff` | Rx1 NB2 On Off | B | — | MISSING | |
| 121 | `AutoNotchOnOff` | Auto Notch On Off | B | — | MISSING | |
| 122 | `SpectralNoiseBlankerOnOff` | SNB On Off | B | — | MISSING | |
| 123 | `Rx2NoiseReductionOnOff` | Rx2 NR1 On Off | B | — | N/A | |
| 124 | `Rx2NoiseReduction2OnOff` | Rx2 NR2 On Off | B | — | N/A | |
| 125 | `Rx2NoiseReduction3OnOff` | Rx2 NR3 On Off | B | — | N/A | |
| 126 | `Rx2NoiseReduction4OnOff` | Rx2 NR4 On Off | B | — | N/A | |
| 127 | `Rx2NoiseReduction4Amount` | Rx2 NR4 Amount | K | — | N/A | |
| 128 | `Rx2NoiseBlanker1OnOff` | Rx2 NB1 On Off | B | — | N/A | |
| 129 | `Rx2Noiseblanker2OnOff` | Rx2 NB2 On Off | B | — | N/A | |
| 130 | `SpectralNoiseBlankerRx2OnOff` | Rx2 SNB On Off | B | — | N/A | |
| 131 | `RX2AutoNotchOnOff` | RX2 Auto Notch On Off | B | — | N/A | |

## 9. TX Control

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 132 | `MOXOnOff` | MOX On Off | B | `POST /api/tx/mox` | READY | |
| 133 | `TunOnOff` | Tune On Off | B | `POST /api/tx/tun` | READY | |
| 134 | `DriveLevel` | DriveLevel | K | `POST /api/tx/drive` | READY | |
| 135 | `DriveLevel_inc` | Drive Level Increment | W | `POST /api/tx/drive` | READY | |
| 136 | `TUNPowerLevel` | TUN Power Level | K | `POST /api/tx/tune-drive` | READY | |
| 137 | `MicGain` | MicGain | K | `POST /api/mic-gain` | READY | |
| 138 | `TwoToneOnOff` | 2Tone On Off | B | `POST /api/tx/twotone` | READY | |
| 139 | `PSOnOff` | PS-A On Off | B | `POST /api/tx/ps` | READY | |
| 140 | `TXAFMonitor` | TX AF Monitor | K | `POST /api/tx/monitor` | READY | |
| 141 | `MONOnOff` | MON On Off | B | `POST /api/tx/monitor` | PARTIAL | Monitor toggle via same endpoint |
| 142 | `ToggleTX` | Toggle TX VFOA VFOB | B | — | MISSING | |
| 143 | `ExternalPAOnOff` | External PA On Off | B | — | MISSING | |

## 10. VOX & Squelch

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 144 | `VOXOnOff` | VOX On Off | B | — | MISSING | |
| 145 | `VOXGain` | VOXGain | K | — | MISSING | |
| 146 | `SquelchOnOff` | Squelch On Off | B | — | MISSING | |
| 147 | `SquelchControl` | Squelch | K | — | MISSING | |
| 148 | `RX2SquelchOnOff` | RX2 Squelch On Off | B | — | N/A | |
| 149 | `RX2SquelchControl` | RX2 Squelch Level | K | — | N/A | |

## 11. RIT / XIT / Split

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 150 | `RitOnOff` | Rit On Off | B | — | MISSING | |
| 151 | `XitOnOff` | Xit On Off | B | — | MISSING | |
| 152 | `RIT_inc` | RIT | W | — | MISSING | |
| 153 | `XIT_inc` | XIT | W | — | MISSING | |
| 154 | `RIT` | RIT | K | — | MISSING | |
| 155 | `XIT` | XIT | K | — | MISSING | |
| 156 | `RIT_clear` | RIT Clear | B | — | MISSING | |
| 157 | `XIT_clear` | XIT Clear | B | — | MISSING | |
| 158 | `SplitOnOff` | Split On Off | B | — | MISSING | |
| 159 | `QuickSplitOnOff` | Quick Split On Off | B | — | MISSING | |
| 160 | `QuickSplitOnOffandSplitOnOff` | Quick Split + Split | B | — | MISSING | |

## 12. Display & Panadapter

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 161 | `ZoomSliderInc` | Zoom | W | `POST /api/rx/zoom` | READY | |
| 162 | `ZoomSliderFix` | Zoom | K | `POST /api/rx/zoom` | READY | |
| 163 | `ZoomInc` | Zoom Inc | B | `POST /api/rx/zoom` | READY | |
| 164 | `ZoomDec` | Zoom Dec | B | `POST /api/rx/zoom` | READY | |
| 165 | `PanSliderInc` | Pan | W | — | MISSING | No pan API |
| 166 | `PanSlider` | Pan | K | — | MISSING | |
| 167 | `PanCenter` | Pan Center | B | — | MISSING | |
| 168 | `DisplayAverage` | Display Average | B | `PUT /api/display-settings` | PARTIAL | |
| 169 | `DisplayPeak` | Display Peak | B | `PUT /api/display-settings` | PARTIAL | |
| 170 | `DisplayTxFilter` | Display Tx Filter | B | `PUT /api/display-settings` | PARTIAL | |
| 171 | `DisplayModeNext` | Display Mode Next | B | — | MISSING | |
| 172 | `DisplayModePrev` | Display Mode Prev | B | — | MISSING | |
| 173 | `WaterfallLowLimit` | Waterfall Low Limit | K | `PUT /api/display-settings` | PARTIAL | |
| 174 | `WaterfallHighLimit` | Waterfall High Limit | K | `PUT /api/display-settings` | PARTIAL | |
| 175 | `ZoomToBandRecall` | Zoom To Band Recall | B | — | MISSING | |
| 176 | `ZoomToBandStore` | Zoom To Band Store | B | — | MISSING | |

## 13. CW

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 177 | `CWSpeed` | CW Speed | K | — | MISSING | |
| 178 | `CWSpeed_inc` | CW Speed Incr | W | — | MISSING | |
| 179 | `CWBreakIn` | Manual or Semi Break-In | B | — | MISSING | |
| 180 | `CWQSK` | Semi or QSK Break-In | B | — | MISSING | |
| 181–189 | `CWXMacro1`..`CWXMacro9` | CWX Macro 1–9 | B | — | MISSING | |
| 190 | `CWXStop` | CWX Stop | B | — | MISSING | |

## 14. Audio Peak Filter (APF)

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 191 | `APF_OnOff` | APF On Off | B | — | MISSING | |
| 192 | `APFFreq` | APF Tune | K | — | MISSING | |
| 193 | `APFBandwidth` | APF Bandwidth | K | — | MISSING | |
| 194 | `APFGain` | APF Gain | K | — | MISSING | |
| 195–198 | `APFType_*` | APF Type variants | B | — | MISSING | |

## 15. Compander / DEXP / EQ / Diversity

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 199 | `CompanderOnOff` | Compander On Off | B | — | MISSING | |
| 200 | `CPDRLevel` | CPDRLevel | K | — | MISSING | |
| 201 | `DEXPOnOff` | DEXP On Off | B | — | MISSING | |
| 202 | `DEXPThreshold` | DEXP Threshold | K | — | MISSING | |
| 203 | `DXLevel` | DXLevel | K | — | MISSING | |
| 204 | `RXEQOnOff` | RX EQ On Off | B | — | MISSING | |
| 205 | `TXEQOnOff` | TX EQ On Off | B | — | MISSING | |
| 206 | `BinauralOnOff` | Binaural On Off | B | — | MISSING | |
| 207 | `StereoDiversityOnOff` | Stereo Diversity On Off | B | — | MISSING | |
| 208 | `DiversityFormOpen` | Diversity Form Open | B | — | N/A | WinForms-specific |
| 209 | `DiversityEnable` | Diversity Enable | B | — | MISSING | |
| 210 | `DiversityPhase` | Diversity Phase | W | — | MISSING | |
| 211 | `DiversityGain` | Diversity Gain | W | — | MISSING | |
| 212 | `DiversityReference` | Diversity RX Reference | B | — | MISSING | |
| 213 | `DiversitySource` | Diversity RX Source | B | — | MISSING | |

## 16. VAC (Virtual Audio Cable)

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 214 | `VACOnOff` | VAC On Off | B | — | MISSING | |
| 215 | `VACGainRX` | VAC Gain RX | K | — | MISSING | |
| 216 | `VACGainTX` | VAC Gain TX | K | — | MISSING | |
| 217 | `VAC2OnOff` | VAC 2 On Off | B | — | MISSING | |
| 218 | `VAC2GainRX` | VAC2 Gain RX | K | — | MISSING | |
| 219 | `VAC2GainTX` | VAC2 Gain TX | K | — | MISSING | |
| 220 | `IQtoVAC` | Rx1 IQ To VAC | B | — | MISSING | |
| 221 | `IQtoVACRX2` | Rx2 IQ to VAC | B | — | N/A | |

## 17. RX2 Miscellaneous

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 222 | `RX2OnOff` | RX2 On Off | B | — | N/A | RX2 not implemented in Zeus |
| 223 | `Rx2PreAmpOnOff` | Rx2 Pre Amp On Off | B | — | N/A | |
| 224 | `RX2Pan` | Stereo Balance RX2 | K | — | N/A | |
| 225 | `RX2CTunOnOff` | RX2 CTUN On Off | B | — | N/A | |

## 18. Misc / System

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 226 | `StartOnOff` | Start On Off | B | `POST /api/connect` | PARTIAL | Connect/disconnect, not toggle |
| 227 | `TunerOnOff` | Tuner On Off | B | — | MISSING | ATU control |
| 228 | `TunerBypassOnOff` | Tuner Bypass | B | — | MISSING | |
| 229 | `MultiRxOnOff` | Multi Rx On Off | B | — | MISSING | |
| 230 | `QuickModeSave` | Quick Mode Save | B | — | MISSING | |
| 231 | `QuickModeRestore` | Quick Mode Restore | B | — | MISSING | |
| 232 | `QuickPlayOnOff` | Quick Play Wave File | B | — | MISSING | |
| 233 | `QuickRecOnOff` | Quick Rec Wave File | B | — | MISSING | |
| 234 | `ESCFormOnOff` | ESC Form On Off | B | — | N/A | WinForms-specific |
| 235 | `CloseConsole` | Close Thetis | B | — | N/A | WinForms-specific |

## 19. MIDI-Internal (meta-commands)

| # | Thetis CatCmd | Display Name | Type | Zeus API | Zeus Status | Notes |
|---|---|---|---|---|---|---|
| 236 | `MidiMessagesPerTuneStepUp` | Increase wheel sensitivity | B | — | N/A | Plugin-internal setting |
| 237 | `MidiMessagesPerTuneStepDown` | Decrease wheel sensitivity | B | — | N/A | Plugin-internal setting |
| 238 | `MidiMessagesPerTuneStepToggle` | Sensitivity High/Low Toggle | B | — | N/A | Plugin-internal setting |

---

## Summary

| Status | Count | Notes |
|---|---|---|
| **READY** | 35 | Direct 1:1 mapping to existing Zeus endpoint |
| **PARTIAL** | 27 | Zeus has the capability but plugin must add sequencing logic (read-modify-write, band memory lookup, mode cycling) |
| **MISSING** (Zeus could add) | ~98 | No Zeus API surface for this command |
| **N/A** (RX2 / WinForms / MIDI-internal) | ~80 | Not applicable to Zeus |

### READY commands (35) — direct 1:1 mapping

These map directly to existing Zeus API endpoints with no extra logic:

1. **VFO A** — tuning (wheel), ±100 kHz jumps (buttons), multi-step (wheel) — 4 commands
2. **Mode direct select** — LSB, USB, DSB, CW, CWL, CWU, FM, AM, DIGU, SPEC, DIGL, SAM, DRM, SSB — 14 commands
3. **Filter** — bandwidth wheel, high/low edges (wheel), TX filter high/low (wheel) — 5 commands
4. **RX Audio** — AF gain (knob + wheel), mute — 3 commands
5. **AGC** — level (knob + wheel), auto AGC — 3 commands
6. **Preamp** — setting knob — 1 command
7. **TX** — MOX, TUN, drive (knob + wheel), tune drive, mic gain, two-tone, PureSignal, TX AF monitor — 10 commands
8. **Display** — zoom (knob + wheel + inc/dec) — 4 commands

*Note: NR4 on/off and NR4 amount are counted as READY (dedicated `/api/rx/nr4` endpoint). NR1–3 are PARTIAL (compound `NrConfig` endpoint).*

### PARTIAL commands (27) — implementable with plugin-side logic

These require the plugin to read current state and compute the target value:

1. **Band select** — 12 direct + up/down = 14 commands (read band memory → post VFO freq)
2. **Mode next/prev** — 2 commands (read current mode → compute next → post)
3. **Filter wider/narrower** — 2 commands (read current bandwidth → adjust → post)
4. **NR1/NR2/NR3 toggle** — 3 commands (read full `NrConfig` → flip one field → post)
5. **Filter shift** — 1 command (move both edges)
6. **Display** — average, peak, TX filter, waterfall limits — 5 commands (read-modify-write on `DisplaySettings`)
7. **MON on/off** — 1 command (toggle via monitor endpoint)
8. **Start/Stop** — 1 command (connect/disconnect, not toggle)

### Key gaps for a complete Thetis-parity MIDI experience

- RIT/XIT/Split — entire subsystem missing from Zeus API
- VFO B — Zeus has no VFO B support
- VOX control — no API surface
- CW speed / break-in / macros — no API surface
- Noise blanker toggles (NB1/NB2) — no API surface
- AGC mode cycle — no API surface
- Squelch — no API surface
- Pan (horizontal scroll) — no API surface
- VFO lock/swap/sync — no API surface
