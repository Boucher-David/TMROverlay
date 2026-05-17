# SDK Field Availability Corpus

Compact redacted availability map for SDK variables observed in local raw captures.

- Sources: 4
- SDK fields: 340
- Raw telemetry frames and private session-info identity values are not included.
- `sdkDeclaredShape` records SDK/storage shape maximums; observed min/max values come from sampled captures.

## Sources

| Capture | Category | Frames | Schema Fields | Sampled Frames | Identity Shape |
| --- | --- | ---: | ---: | ---: | --- |
| capture-20260426-130334-932 | endurance-4h-team-race | 1036026 | 334 | 1730 | drivers 61; user names 61; team names 61; blank class names 1 |
| capture-20260502-143722-571 | endurance-24h-fragment | 277680 | 334 | 466 | drivers 60; user names 60; team names 60; blank class names 1 |
| capture-20260515-210810-124 | ai-nascar-limited-tire-race | 55944 | 325 | 936 | drivers 38; user names 38; team names 38; blank class names 38 |
| capture-20260516-204700-385 | pcup-open-practice-pit-service | 29363 | 334 | 493 | drivers 4; user names 4; team names 4; blank class names 0 |

## Category Counts

- `driver-change`: 2
- `engine`: 19
- `fuel`: 10
- `input`: 33
- `misc`: 74
- `per-car`: 30
- `pit-service`: 102
- `race-control`: 12
- `radio-camera`: 7
- `scoring`: 7
- `session`: 34
- `vehicle-dynamics`: 46
- `weather`: 13

## Field Index

| Field | Type | Count | Max Index | Bytes | Unit | Categories | Present In |
| --- | --- | ---: | ---: | ---: | --- | --- | --- |
| AirDensity | irFloat | 1 | 0 | 4 | kg/m^3 | misc | 4 |
| AirPressure | irFloat | 1 | 0 | 4 | Pa | misc | 4 |
| AirTemp | irFloat | 1 | 0 | 4 | C | weather | 4 |
| Brake | irFloat | 1 | 0 | 4 | % | input | 4 |
| BrakeABSactive | irBool | 1 | 0 | 1 |  | input | 4 |
| BrakeRaw | irFloat | 1 | 0 | 4 | % | input | 4 |
| CamCameraNumber | irInt | 1 | 0 | 4 |  | radio-camera | 4 |
| CamCameraState | irBitField | 1 | 0 | 4 | irsdk_CameraState | radio-camera | 4 |
| CamCarIdx | irInt | 1 | 0 | 4 |  | per-car, radio-camera | 4 |
| CamGroupNumber | irInt | 1 | 0 | 4 |  | radio-camera | 4 |
| CarDistAhead | irFloat | 1 | 0 | 4 | m | misc | 4 |
| CarDistBehind | irFloat | 1 | 0 | 4 | m | misc | 4 |
| CarIdxBestLapNum | irInt | 64 | 63 | 256 |  | per-car | 4 |
| CarIdxBestLapTime | irFloat | 64 | 63 | 256 | s | per-car | 4 |
| CarIdxClass | irInt | 64 | 63 | 256 |  | per-car | 4 |
| CarIdxClassPosition | irInt | 64 | 63 | 256 |  | scoring, per-car | 4 |
| CarIdxEstTime | irFloat | 64 | 63 | 256 | s | per-car | 4 |
| CarIdxF2Time | irFloat | 64 | 63 | 256 | s | scoring, per-car | 4 |
| CarIdxFastRepairsUsed | irInt | 64 | 63 | 256 |  | per-car, pit-service | 4 |
| CarIdxGear | irInt | 64 | 63 | 256 |  | per-car, input | 4 |
| CarIdxLap | irInt | 64 | 63 | 256 |  | per-car | 4 |
| CarIdxLapCompleted | irInt | 64 | 63 | 256 |  | per-car | 4 |
| CarIdxLapDistPct | irFloat | 64 | 63 | 256 | % | per-car | 4 |
| CarIdxLastLapTime | irFloat | 64 | 63 | 256 | s | scoring, per-car | 4 |
| CarIdxOnPitRoad | irBool | 64 | 63 | 64 |  | per-car, pit-service | 4 |
| CarIdxP2P_Count | irInt | 64 | 63 | 256 |  | per-car | 4 |
| CarIdxP2P_Status | irBool | 64 | 63 | 64 |  | per-car | 4 |
| CarIdxPaceFlags | irBitField | 64 | 63 | 256 | irsdk_PaceFlags | per-car, race-control | 4 |
| CarIdxPaceLine | irInt | 64 | 63 | 256 |  | per-car, race-control | 4 |
| CarIdxPaceRow | irInt | 64 | 63 | 256 |  | per-car, race-control | 4 |
| CarIdxPosition | irInt | 64 | 63 | 256 |  | scoring, per-car | 4 |
| CarIdxQualTireCompound | irInt | 64 | 63 | 256 |  | per-car, pit-service, vehicle-dynamics | 4 |
| CarIdxQualTireCompoundLocked | irBool | 64 | 63 | 64 |  | per-car, pit-service, vehicle-dynamics | 4 |
| CarIdxRPM | irFloat | 64 | 63 | 256 | revs/min | per-car, engine | 4 |
| CarIdxSessionFlags | irBitField | 64 | 63 | 256 | irsdk_Flags | session, per-car, race-control | 4 |
| CarIdxSteer | irFloat | 64 | 63 | 256 | rad | per-car, input | 4 |
| CarIdxTireCompound | irInt | 64 | 63 | 256 |  | per-car, pit-service | 4 |
| CarIdxTrackSurface | irInt | 64 | 63 | 256 | irsdk_TrkLoc | per-car | 4 |
| CarIdxTrackSurfaceMaterial | irInt | 64 | 63 | 256 | irsdk_TrkSurf | per-car | 4 |
| CarLeftRight | irInt | 1 | 0 | 4 | irsdk_CarLeftRight | misc | 4 |
| ChanAvgLatency | irFloat | 1 | 0 | 4 | s | vehicle-dynamics | 4 |
| ChanClockSkew | irFloat | 1 | 0 | 4 | s | misc | 4 |
| ChanLatency | irFloat | 1 | 0 | 4 | s | vehicle-dynamics | 4 |
| ChanPartnerQuality | irFloat | 1 | 0 | 4 | % | misc | 4 |
| ChanQuality | irFloat | 1 | 0 | 4 | % | misc | 4 |
| Clutch | irFloat | 1 | 0 | 4 | % | input | 4 |
| ClutchRaw | irFloat | 1 | 0 | 4 | % | input | 4 |
| CpuUsageBG | irFloat | 1 | 0 | 4 | % | misc | 4 |
| CpuUsageFG | irFloat | 1 | 0 | 4 | % | misc | 4 |
| DCDriversSoFar | irInt | 1 | 0 | 4 |  | driver-change | 4 |
| DCLapStatus | irInt | 1 | 0 | 4 |  | driver-change | 4 |
| DisplayUnits | irInt | 1 | 0 | 4 |  | misc | 4 |
| DriverMarker | irBool | 1 | 0 | 1 |  | race-control | 4 |
| Engine0_RPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| EngineWarnings | irBitField | 1 | 0 | 4 | irsdk_EngineWarnings | engine | 4 |
| EnterExitReset | irInt | 1 | 0 | 4 |  | misc | 4 |
| FastRepairAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| FastRepairUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| FogLevel | irFloat | 1 | 0 | 4 | % | misc | 4 |
| FrameRate | irFloat | 1 | 0 | 4 | fps | misc | 4 |
| FrontTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| FrontTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| FuelLevel | irFloat | 1 | 0 | 4 | l | fuel | 4 |
| FuelLevelPct | irFloat | 1 | 0 | 4 | % | fuel | 4 |
| FuelPress | irFloat | 1 | 0 | 4 | bar | fuel, engine | 4 |
| FuelUsePerHour | irFloat | 1 | 0 | 4 | kg/h | fuel, engine | 4 |
| Gear | irInt | 1 | 0 | 4 |  | input | 4 |
| GpuUsage | irFloat | 1 | 0 | 4 | % | misc | 4 |
| HandbrakeRaw | irFloat | 1 | 0 | 4 | % | input | 4 |
| IsDiskLoggingActive | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsDiskLoggingEnabled | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsGarageVisible | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsInGarage | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsOnTrack | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsOnTrackCar | irBool | 1 | 0 | 1 |  | misc | 4 |
| IsReplayPlaying | irBool | 1 | 0 | 1 |  | session | 4 |
| LFTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LFTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LFbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 4 |
| LFcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| LFodometer | irFloat | 1 | 0 | 4 | m | pit-service | 4 |
| LFshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| LFshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| LFshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| LFshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| LFtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LFtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LFtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LFwearL | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| LFwearM | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| LFwearR | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| LRTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LRTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LRbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 4 |
| LRcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| LRodometer | irFloat | 1 | 0 | 4 | m | pit-service | 4 |
| LRshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| LRshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| LRshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| LRshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| LRtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LRtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LRtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| LRwearL | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| LRwearM | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| LRwearR | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| Lap | irInt | 1 | 0 | 4 |  | misc | 4 |
| LapBestLap | irInt | 1 | 0 | 4 |  | misc | 4 |
| LapBestLapTime | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LapBestNLapLap | irInt | 1 | 0 | 4 |  | misc | 4 |
| LapBestNLapTime | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LapCompleted | irInt | 1 | 0 | 4 |  | misc | 4 |
| LapCurrentLapTime | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LapDeltaToBestLap | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LapDeltaToBestLap_DD | irFloat | 1 | 0 | 4 | s/s | misc | 4 |
| LapDeltaToBestLap_OK | irBool | 1 | 0 | 1 |  | misc | 4 |
| LapDeltaToOptimalLap | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LapDeltaToOptimalLap_DD | irFloat | 1 | 0 | 4 | s/s | misc | 4 |
| LapDeltaToOptimalLap_OK | irBool | 1 | 0 | 1 |  | misc | 4 |
| LapDeltaToSessionBestLap | irFloat | 1 | 0 | 4 | s | session | 4 |
| LapDeltaToSessionBestLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 4 |
| LapDeltaToSessionBestLap_OK | irBool | 1 | 0 | 1 |  | session | 4 |
| LapDeltaToSessionLastlLap | irFloat | 1 | 0 | 4 | s | session | 4 |
| LapDeltaToSessionLastlLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 4 |
| LapDeltaToSessionLastlLap_OK | irBool | 1 | 0 | 1 |  | session | 4 |
| LapDeltaToSessionOptimalLap | irFloat | 1 | 0 | 4 | s | session | 4 |
| LapDeltaToSessionOptimalLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 4 |
| LapDeltaToSessionOptimalLap_OK | irBool | 1 | 0 | 1 |  | session | 4 |
| LapDist | irFloat | 1 | 0 | 4 | m | misc | 4 |
| LapDistPct | irFloat | 1 | 0 | 4 | % | misc | 4 |
| LapLasNLapSeq | irInt | 1 | 0 | 4 |  | misc | 4 |
| LapLastLapTime | irFloat | 1 | 0 | 4 | s | scoring | 4 |
| LapLastNLapTime | irFloat | 1 | 0 | 4 | s | misc | 4 |
| LatAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 4 |
| LatAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 4 |
| LeftTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LeftTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| LoadNumTextures | irBool | 1 | 0 | 1 |  | misc | 4 |
| LongAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 4 |
| LongAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 4 |
| ManifoldPress | irFloat | 1 | 0 | 4 | bar | engine | 4 |
| ManualBoost | irBool | 1 | 0 | 1 |  | misc | 4 |
| ManualNoBoost | irBool | 1 | 0 | 1 |  | misc | 4 |
| MemPageFaultSec | irFloat | 1 | 0 | 4 |  | misc | 4 |
| MemSoftPageFaultSec | irFloat | 1 | 0 | 4 |  | misc | 4 |
| OilLevel | irFloat | 1 | 0 | 4 | l | engine | 4 |
| OilPress | irFloat | 1 | 0 | 4 | bar | engine | 4 |
| OilTemp | irFloat | 1 | 0 | 4 | C | engine | 4 |
| OkToReloadTextures | irBool | 1 | 0 | 1 |  | misc | 4 |
| OnPitRoad | irBool | 1 | 0 | 1 |  | pit-service | 4 |
| P2P_Count | irInt | 1 | 0 | 4 |  | misc | 4 |
| P2P_Status | irBool | 1 | 0 | 1 |  | misc | 4 |
| PaceMode | irInt | 1 | 0 | 4 | irsdk_PaceMode | race-control | 4 |
| PitOptRepairLeft | irFloat | 1 | 0 | 4 | s | pit-service | 4 |
| PitRepairLeft | irFloat | 1 | 0 | 4 | s | pit-service | 4 |
| PitSvFlags | irBitField | 1 | 0 | 4 | irsdk_PitSvFlags | race-control, pit-service | 4 |
| PitSvFuel | irFloat | 1 | 0 | 4 | l or kWh | pit-service, fuel | 4 |
| PitSvLFP | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| PitSvLRP | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| PitSvRFP | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| PitSvRRP | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| PitSvTireCompound | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| Pitch | irFloat | 1 | 0 | 4 | rad | pit-service, vehicle-dynamics | 4 |
| PitchRate | irFloat | 1 | 0 | 4 | rad/s | pit-service, vehicle-dynamics | 4 |
| PitchRate_ST | irFloat | 6 | 5 | 24 | rad/s | pit-service, vehicle-dynamics | 4 |
| PitsOpen | irBool | 1 | 0 | 1 |  | pit-service | 4 |
| PitstopActive | irBool | 1 | 0 | 1 |  | pit-service | 4 |
| PlayerCarClass | irInt | 1 | 0 | 4 |  | misc | 4 |
| PlayerCarClassPosition | irInt | 1 | 0 | 4 |  | scoring | 4 |
| PlayerCarDriverIncidentCount | irInt | 1 | 0 | 4 |  | session | 4 |
| PlayerCarDryTireSetLimit | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| PlayerCarIdx | irInt | 1 | 0 | 4 |  | per-car | 4 |
| PlayerCarInPitStall | irBool | 1 | 0 | 1 |  | pit-service | 4 |
| PlayerCarMyIncidentCount | irInt | 1 | 0 | 4 |  | session | 4 |
| PlayerCarPitSvStatus | irInt | 1 | 0 | 4 | irsdk_PitSvStatus | pit-service | 4 |
| PlayerCarPosition | irInt | 1 | 0 | 4 |  | scoring | 4 |
| PlayerCarPowerAdjust | irFloat | 1 | 0 | 4 | % | misc | 4 |
| PlayerCarSLBlinkRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| PlayerCarSLFirstRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| PlayerCarSLLastRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| PlayerCarSLShiftRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| PlayerCarTeamIncidentCount | irInt | 1 | 0 | 4 |  | session | 4 |
| PlayerCarTowTime | irFloat | 1 | 0 | 4 | s | misc | 4 |
| PlayerCarWeightPenalty | irFloat | 1 | 0 | 4 | kg | vehicle-dynamics | 4 |
| PlayerFastRepairsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| PlayerIncidents | irInt | 1 | 0 | 4 | irsdk_IncidentFlags | misc | 4 |
| PlayerTireCompound | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| PlayerTrackSurface | irInt | 1 | 0 | 4 | irsdk_TrkLoc | misc | 4 |
| PlayerTrackSurfaceMaterial | irInt | 1 | 0 | 4 | irsdk_TrkSurf | misc | 4 |
| Precipitation | irFloat | 1 | 0 | 4 | % | pit-service, weather | 4 |
| PushToPass | irBool | 1 | 0 | 1 |  | misc | 4 |
| PushToTalk | irBool | 1 | 0 | 1 |  | misc | 4 |
| RFTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RFTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RFbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 4 |
| RFcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| RFodometer | irFloat | 1 | 0 | 4 | m | pit-service | 4 |
| RFshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| RFshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| RFshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| RFshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| RFtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RFtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RFtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RFwearL | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RFwearM | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RFwearR | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RPM | irFloat | 1 | 0 | 4 | revs/min | engine | 4 |
| RRTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RRTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RRbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 4 |
| RRcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 4 |
| RRodometer | irFloat | 1 | 0 | 4 | m | pit-service | 4 |
| RRshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| RRshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| RRshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| RRshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| RRtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RRtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RRtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 4 |
| RRwearL | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RRwearM | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RRwearR | irFloat | 1 | 0 | 4 | % | pit-service | 4 |
| RaceLaps | irInt | 1 | 0 | 4 |  | misc | 4 |
| RadioTransmitCarIdx | irInt | 1 | 0 | 4 |  | per-car, radio-camera | 4 |
| RadioTransmitFrequencyIdx | irInt | 1 | 0 | 4 |  | radio-camera | 4 |
| RadioTransmitRadioIdx | irInt | 1 | 0 | 4 |  | radio-camera | 4 |
| RearTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RearTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RelativeHumidity | irFloat | 1 | 0 | 4 | % | vehicle-dynamics | 4 |
| ReplayFrameNum | irInt | 1 | 0 | 4 |  | session | 4 |
| ReplayFrameNumEnd | irInt | 1 | 0 | 4 |  | session | 4 |
| ReplayPlaySlowMotion | irBool | 1 | 0 | 1 |  | session | 4 |
| ReplayPlaySpeed | irInt | 1 | 0 | 4 |  | session, vehicle-dynamics | 4 |
| ReplaySessionNum | irInt | 1 | 0 | 4 |  | session | 4 |
| ReplaySessionTime | irDouble | 1 | 0 | 8 | s | session | 4 |
| RightTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| RightTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| Roll | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 4 |
| RollRate | irFloat | 1 | 0 | 4 | rad/s | vehicle-dynamics | 4 |
| RollRate_ST | irFloat | 6 | 5 | 24 | rad/s | vehicle-dynamics | 4 |
| SessionFlags | irBitField | 1 | 0 | 4 | irsdk_Flags | session, race-control | 4 |
| SessionJokerLapsRemain | irInt | 1 | 0 | 4 |  | session, race-control | 4 |
| SessionLapsRemain | irInt | 1 | 0 | 4 |  | session | 4 |
| SessionLapsRemainEx | irInt | 1 | 0 | 4 |  | session | 4 |
| SessionLapsTotal | irInt | 1 | 0 | 4 |  | session | 4 |
| SessionNum | irInt | 1 | 0 | 4 |  | session | 4 |
| SessionOnJokerLap | irBool | 1 | 0 | 1 |  | session, race-control | 4 |
| SessionState | irInt | 1 | 0 | 4 | irsdk_SessionState | session | 4 |
| SessionTick | irInt | 1 | 0 | 4 |  | session | 4 |
| SessionTime | irDouble | 1 | 0 | 8 | s | session | 4 |
| SessionTimeOfDay | irFloat | 1 | 0 | 4 | s | session | 4 |
| SessionTimeRemain | irDouble | 1 | 0 | 8 | s | session | 4 |
| SessionTimeTotal | irDouble | 1 | 0 | 8 | s | session | 4 |
| SessionUniqueID | irInt | 1 | 0 | 4 |  | session | 4 |
| ShiftGrindRPM | irFloat | 1 | 0 | 4 | RPM | engine | 4 |
| ShiftIndicatorPct | irFloat | 1 | 0 | 4 | % | engine | 4 |
| ShiftPowerPct | irFloat | 1 | 0 | 4 | % | input | 4 |
| Shifter | irInt | 1 | 0 | 4 |  | misc | 4 |
| Skies | irInt | 1 | 0 | 4 |  | misc | 4 |
| SolarAltitude | irFloat | 1 | 0 | 4 | rad | weather, vehicle-dynamics | 4 |
| SolarAzimuth | irFloat | 1 | 0 | 4 | rad | weather | 4 |
| Speed | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| SteeringFFBEnabled | irBool | 1 | 0 | 1 |  | input | 4 |
| SteeringWheelAngle | irFloat | 1 | 0 | 4 | rad | input | 4 |
| SteeringWheelAngleMax | irFloat | 1 | 0 | 4 | rad | input | 4 |
| SteeringWheelLimiter | irFloat | 1 | 0 | 4 | % | input, vehicle-dynamics | 4 |
| SteeringWheelMaxForceNm | irFloat | 1 | 0 | 4 | N*m | input | 4 |
| SteeringWheelPctDamper | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPctIntensity | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPctSmoothing | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPctTorque | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPctTorqueSign | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPctTorqueSignStops | irFloat | 1 | 0 | 4 | % | input | 4 |
| SteeringWheelPeakForceNm | irFloat | 1 | 0 | 4 | N*m | input | 4 |
| SteeringWheelTorque | irFloat | 1 | 0 | 4 | N*m | input | 4 |
| SteeringWheelTorque_ST | irFloat | 6 | 5 | 24 | N*m | input | 4 |
| SteeringWheelUseLinear | irBool | 1 | 0 | 1 |  | input | 4 |
| Throttle | irFloat | 1 | 0 | 4 | % | input | 4 |
| ThrottleRaw | irFloat | 1 | 0 | 4 | % | input | 4 |
| TireLF_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 4 |
| TireLR_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 4 |
| TireRF_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 4 |
| TireRR_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 4 |
| TireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| TireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 4 |
| TrackTemp | irFloat | 1 | 0 | 4 | C | weather | 4 |
| TrackTempCrew | irFloat | 1 | 0 | 4 | C | weather | 4 |
| TrackWetness | irInt | 1 | 0 | 4 | irsdk_TrackWetness | weather | 4 |
| VelocityX | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| VelocityX_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 4 |
| VelocityY | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| VelocityY_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 4 |
| VelocityZ | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| VelocityZ_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 4 |
| VertAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 4 |
| VertAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 4 |
| VidCapActive | irBool | 1 | 0 | 1 |  | misc | 4 |
| VidCapEnabled | irBool | 1 | 0 | 1 |  | misc | 4 |
| Voltage | irFloat | 1 | 0 | 4 | V | engine | 4 |
| WaterLevel | irFloat | 1 | 0 | 4 | l | engine | 4 |
| WaterTemp | irFloat | 1 | 0 | 4 | C | engine | 4 |
| WeatherDeclaredWet | irBool | 1 | 0 | 1 |  | pit-service, weather | 4 |
| WindDir | irFloat | 1 | 0 | 4 | rad | weather | 4 |
| WindVel | irFloat | 1 | 0 | 4 | m/s | weather, vehicle-dynamics | 4 |
| Yaw | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 4 |
| YawNorth | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 4 |
| YawRate | irFloat | 1 | 0 | 4 | rad/s | vehicle-dynamics | 4 |
| YawRate_ST | irFloat | 6 | 5 | 24 | rad/s | vehicle-dynamics | 4 |
| dcABS | irFloat | 1 | 0 | 4 |  | misc | 3 |
| dcABSToggle | irBool | 1 | 0 | 1 |  | misc | 2 |
| dcBrakeBias | irFloat | 1 | 0 | 4 |  | input | 4 |
| dcDashPage | irFloat | 1 | 0 | 4 |  | misc | 3 |
| dcHeadlightFlash | irBool | 1 | 0 | 1 |  | misc | 3 |
| dcLowFuelAccept | irBool | 1 | 0 | 1 |  | fuel | 1 |
| dcPitSpeedLimiterToggle | irBool | 1 | 0 | 1 |  | pit-service, vehicle-dynamics | 3 |
| dcStarter | irBool | 1 | 0 | 1 |  | misc | 4 |
| dcThrottleShape | irFloat | 1 | 0 | 4 |  | input | 1 |
| dcToggleWindshieldWipers | irBool | 1 | 0 | 1 |  | weather | 3 |
| dcTractionControl | irFloat | 1 | 0 | 4 |  | misc | 3 |
| dcTractionControlToggle | irBool | 1 | 0 | 1 |  | misc | 2 |
| dcTriggerWindshieldWipers | irBool | 1 | 0 | 1 |  | weather | 3 |
| dpFastRepair | irFloat | 1 | 0 | 4 |  | pit-service | 4 |
| dpFuelAddKg | irFloat | 1 | 0 | 4 | kg | pit-service, fuel | 4 |
| dpFuelAutoFillActive | irFloat | 1 | 0 | 4 |  | race-control, pit-service, fuel | 4 |
| dpFuelAutoFillEnabled | irFloat | 1 | 0 | 4 |  | pit-service, fuel | 4 |
| dpFuelFill | irFloat | 1 | 0 | 4 |  | race-control, pit-service, fuel | 4 |
| dpLFTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 3 |
| dpLFTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 4 |
| dpLRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 3 |
| dpLRTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 4 |
| dpLTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpRFTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 3 |
| dpRFTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 4 |
| dpRRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 3 |
| dpRRTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 4 |
| dpRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWeightJackerLeft | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWeightJackerRight | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWindshieldTearoff | irFloat | 1 | 0 | 4 |  | pit-service, weather | 4 |
