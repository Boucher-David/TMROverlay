# SDK Field Availability Corpus

Compact redacted availability map for SDK variables observed in local raw captures.

- Sources: 5
- SDK fields: 367
- Raw telemetry frames and private session-info identity values are not included.
- `sdkDeclaredShape` records the largest SDK/storage shape observed in this corpus; source rows preserve each capture's dynamic `CarIdx*` maximum. Observed min/max values come from sampled captures.

## Sources

| Capture | Category | Frames | Schema Fields | CarIdx Slots | Sampled Frames | Identity Shape |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| capture-20260426-130334-932 | endurance-4h-team-race | 1036026 | 334 | 64 | 1730 | drivers 61; user names 61; team names 61; blank class names 1 |
| capture-20260502-143722-571 | endurance-24h-fragment | 277680 | 334 | 64 | 466 | drivers 60; user names 60; team names 60; blank class names 1 |
| capture-20260515-210810-124 | ai-nascar-limited-tire-race | 55944 | 325 | 64 | 936 | drivers 38; user names 38; team names 38; blank class names 38 |
| capture-20260516-204700-385 | pcup-open-practice-pit-service | 29363 | 334 | 64 | 493 | drivers 4; user names 4; team names 4; blank class names 0 |
| capture-20260714-193157-308 | acura-offline-testing-dynamic-caridx | 3750 | 344 | 72 | 66 | drivers 1; user names 1; team names 1; blank class names 1 |

## Category Counts

- `driver-change`: 2
- `engine`: 25
- `fuel`: 10
- `input`: 33
- `misc`: 81
- `per-car`: 30
- `pit-service`: 102
- `race-control`: 12
- `radio-camera`: 7
- `scoring`: 7
- `session`: 34
- `vehicle-dynamics`: 60
- `weather`: 13

## Field Index

| Field | Type | Count | Max Index | Bytes | Unit | Categories | Present In |
| --- | --- | ---: | ---: | ---: | --- | --- | --- |
| AirDensity | irFloat | 1 | 0 | 4 | kg/m^3 | misc | 5 |
| AirPressure | irFloat | 1 | 0 | 4 | Pa | misc | 5 |
| AirTemp | irFloat | 1 | 0 | 4 | C | weather | 5 |
| Brake | irFloat | 1 | 0 | 4 | % | input | 5 |
| BrakeABSactive | irBool | 1 | 0 | 1 |  | input | 5 |
| BrakeRaw | irFloat | 1 | 0 | 4 | % | input | 5 |
| CamCameraNumber | irInt | 1 | 0 | 4 |  | radio-camera | 5 |
| CamCameraState | irBitField | 1 | 0 | 4 | irsdk_CameraState | radio-camera | 5 |
| CamCarIdx | irInt | 1 | 0 | 4 |  | per-car, radio-camera | 5 |
| CamGroupNumber | irInt | 1 | 0 | 4 |  | radio-camera | 5 |
| CarDistAhead | irFloat | 1 | 0 | 4 | m | misc | 5 |
| CarDistBehind | irFloat | 1 | 0 | 4 | m | misc | 5 |
| CarIdxBestLapNum | irInt | 72 | 71 | 288 |  | per-car | 5 |
| CarIdxBestLapTime | irFloat | 72 | 71 | 288 | s | per-car | 5 |
| CarIdxClass | irInt | 72 | 71 | 288 |  | per-car | 5 |
| CarIdxClassPosition | irInt | 72 | 71 | 288 |  | scoring, per-car | 5 |
| CarIdxEstTime | irFloat | 72 | 71 | 288 | s | per-car | 5 |
| CarIdxF2Time | irFloat | 72 | 71 | 288 | s | scoring, per-car | 5 |
| CarIdxFastRepairsUsed | irInt | 72 | 71 | 288 |  | per-car, pit-service | 5 |
| CarIdxGear | irInt | 72 | 71 | 288 |  | per-car, input | 5 |
| CarIdxLap | irInt | 72 | 71 | 288 |  | per-car | 5 |
| CarIdxLapCompleted | irInt | 72 | 71 | 288 |  | per-car | 5 |
| CarIdxLapDistPct | irFloat | 72 | 71 | 288 | % | per-car | 5 |
| CarIdxLastLapTime | irFloat | 72 | 71 | 288 | s | scoring, per-car | 5 |
| CarIdxOnPitRoad | irBool | 72 | 71 | 72 |  | per-car, pit-service | 5 |
| CarIdxP2P_Count | irInt | 72 | 71 | 288 |  | per-car | 5 |
| CarIdxP2P_Status | irBool | 72 | 71 | 72 |  | per-car | 5 |
| CarIdxPaceFlags | irBitField | 72 | 71 | 288 | irsdk_PaceFlags | per-car, race-control | 5 |
| CarIdxPaceLine | irInt | 72 | 71 | 288 |  | per-car, race-control | 5 |
| CarIdxPaceRow | irInt | 72 | 71 | 288 |  | per-car, race-control | 5 |
| CarIdxPosition | irInt | 72 | 71 | 288 |  | scoring, per-car | 5 |
| CarIdxQualTireCompound | irInt | 72 | 71 | 288 |  | per-car, pit-service, vehicle-dynamics | 5 |
| CarIdxQualTireCompoundLocked | irBool | 72 | 71 | 72 |  | per-car, pit-service, vehicle-dynamics | 5 |
| CarIdxRPM | irFloat | 72 | 71 | 288 | revs/min | per-car, engine | 5 |
| CarIdxSessionFlags | irBitField | 72 | 71 | 288 | irsdk_Flags | session, per-car, race-control | 5 |
| CarIdxSteer | irFloat | 72 | 71 | 288 | rad | per-car, input | 5 |
| CarIdxTireCompound | irInt | 72 | 71 | 288 |  | per-car, pit-service | 5 |
| CarIdxTrackSurface | irInt | 72 | 71 | 288 | irsdk_TrkLoc | per-car | 5 |
| CarIdxTrackSurfaceMaterial | irInt | 72 | 71 | 288 | irsdk_TrkSurf | per-car | 5 |
| CarLeftRight | irInt | 1 | 0 | 4 | irsdk_CarLeftRight | misc | 5 |
| ChanAvgLatency | irFloat | 1 | 0 | 4 | s | vehicle-dynamics | 5 |
| ChanClockSkew | irFloat | 1 | 0 | 4 | s | misc | 5 |
| ChanLatency | irFloat | 1 | 0 | 4 | s | vehicle-dynamics | 5 |
| ChanPartnerQuality | irFloat | 1 | 0 | 4 | % | misc | 5 |
| ChanQuality | irFloat | 1 | 0 | 4 | % | misc | 5 |
| Clutch | irFloat | 1 | 0 | 4 | % | input | 5 |
| ClutchRaw | irFloat | 1 | 0 | 4 | % | input | 5 |
| CpuUsageBG | irFloat | 1 | 0 | 4 | % | misc | 5 |
| CpuUsageFG | irFloat | 1 | 0 | 4 | % | misc | 5 |
| DCDriversSoFar | irInt | 1 | 0 | 4 |  | driver-change | 5 |
| DCLapStatus | irInt | 1 | 0 | 4 |  | driver-change | 5 |
| DisplayUnits | irInt | 1 | 0 | 4 |  | misc | 5 |
| DriverMarker | irBool | 1 | 0 | 1 |  | race-control | 5 |
| EnergyBatteryToMGU_KLap | irFloat | 1 | 0 | 4 | J | misc | 1 |
| EnergyERSBattery | irFloat | 1 | 0 | 4 | J | engine | 1 |
| EnergyERSBatteryPct | irFloat | 1 | 0 | 4 | % | engine | 1 |
| EnergyMGU_KLapDeployPct | irFloat | 1 | 0 | 4 | % | misc | 1 |
| Engine0_RPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| Engine1_RPM | irFloat | 1 | 0 | 4 | revs/min | engine | 1 |
| EngineWarnings | irBitField | 1 | 0 | 4 | irsdk_EngineWarnings | engine | 5 |
| EnterExitReset | irInt | 1 | 0 | 4 |  | misc | 5 |
| FROLLshockDefl | irFloat | 1 | 0 | 4 | m | vehicle-dynamics | 1 |
| FROLLshockDefl_ST | irFloat | 6 | 5 | 24 | m | vehicle-dynamics | 1 |
| FROLLshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 1 |
| FROLLshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 1 |
| FastRepairAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| FastRepairUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| FogLevel | irFloat | 1 | 0 | 4 | % | misc | 5 |
| FrameRate | irFloat | 1 | 0 | 4 | fps | misc | 5 |
| FrontTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| FrontTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| FuelLevel | irFloat | 1 | 0 | 4 | l | fuel | 5 |
| FuelLevelPct | irFloat | 1 | 0 | 4 | % | fuel | 5 |
| FuelPress | irFloat | 1 | 0 | 4 | bar | fuel, engine | 5 |
| FuelUsePerHour | irFloat | 1 | 0 | 4 | kg/h | fuel, engine | 5 |
| Gear | irInt | 1 | 0 | 4 |  | input | 5 |
| GpuUsage | irFloat | 1 | 0 | 4 | % | misc | 5 |
| HFshockDefl | irFloat | 1 | 0 | 4 | m | misc | 1 |
| HFshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 1 |
| HFshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 1 |
| HFshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 1 |
| HandbrakeRaw | irFloat | 1 | 0 | 4 | % | input | 5 |
| IsDiskLoggingActive | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsDiskLoggingEnabled | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsGarageVisible | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsInGarage | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsOnTrack | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsOnTrackCar | irBool | 1 | 0 | 1 |  | misc | 5 |
| IsReplayPlaying | irBool | 1 | 0 | 1 |  | session | 5 |
| LFTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LFTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LFbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 5 |
| LFcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| LFodometer | irFloat | 1 | 0 | 4 | m | pit-service | 5 |
| LFshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| LFshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| LFshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| LFshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| LFtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LFtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LFtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LFwearL | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| LFwearM | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| LFwearR | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| LRTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LRTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LRbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 5 |
| LRcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| LRodometer | irFloat | 1 | 0 | 4 | m | pit-service | 5 |
| LRshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| LRshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| LRshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| LRshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| LRtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LRtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LRtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| LRwearL | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| LRwearM | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| LRwearR | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| Lap | irInt | 1 | 0 | 4 |  | misc | 5 |
| LapBestLap | irInt | 1 | 0 | 4 |  | misc | 5 |
| LapBestLapTime | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LapBestNLapLap | irInt | 1 | 0 | 4 |  | misc | 5 |
| LapBestNLapTime | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LapCompleted | irInt | 1 | 0 | 4 |  | misc | 5 |
| LapCurrentLapTime | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LapDeltaToBestLap | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LapDeltaToBestLap_DD | irFloat | 1 | 0 | 4 | s/s | misc | 5 |
| LapDeltaToBestLap_OK | irBool | 1 | 0 | 1 |  | misc | 5 |
| LapDeltaToOptimalLap | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LapDeltaToOptimalLap_DD | irFloat | 1 | 0 | 4 | s/s | misc | 5 |
| LapDeltaToOptimalLap_OK | irBool | 1 | 0 | 1 |  | misc | 5 |
| LapDeltaToSessionBestLap | irFloat | 1 | 0 | 4 | s | session | 5 |
| LapDeltaToSessionBestLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 5 |
| LapDeltaToSessionBestLap_OK | irBool | 1 | 0 | 1 |  | session | 5 |
| LapDeltaToSessionLastlLap | irFloat | 1 | 0 | 4 | s | session | 5 |
| LapDeltaToSessionLastlLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 5 |
| LapDeltaToSessionLastlLap_OK | irBool | 1 | 0 | 1 |  | session | 5 |
| LapDeltaToSessionOptimalLap | irFloat | 1 | 0 | 4 | s | session | 5 |
| LapDeltaToSessionOptimalLap_DD | irFloat | 1 | 0 | 4 | s/s | session | 5 |
| LapDeltaToSessionOptimalLap_OK | irBool | 1 | 0 | 1 |  | session | 5 |
| LapDist | irFloat | 1 | 0 | 4 | m | misc | 5 |
| LapDistPct | irFloat | 1 | 0 | 4 | % | misc | 5 |
| LapLasNLapSeq | irInt | 1 | 0 | 4 |  | misc | 5 |
| LapLastLapTime | irFloat | 1 | 0 | 4 | s | scoring | 5 |
| LapLastNLapTime | irFloat | 1 | 0 | 4 | s | misc | 5 |
| LatAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 5 |
| LatAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 5 |
| LeftTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LeftTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| LoadNumTextures | irBool | 1 | 0 | 1 |  | misc | 5 |
| LongAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 5 |
| LongAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 5 |
| ManifoldPress | irFloat | 1 | 0 | 4 | bar | engine | 5 |
| ManualBoost | irBool | 1 | 0 | 1 |  | misc | 5 |
| ManualNoBoost | irBool | 1 | 0 | 1 |  | misc | 5 |
| MemPageFaultSec | irFloat | 1 | 0 | 4 |  | misc | 5 |
| MemSoftPageFaultSec | irFloat | 1 | 0 | 4 |  | misc | 5 |
| OilLevel | irFloat | 1 | 0 | 4 | l | engine | 5 |
| OilPress | irFloat | 1 | 0 | 4 | bar | engine | 5 |
| OilTemp | irFloat | 1 | 0 | 4 | C | engine | 5 |
| OkToReloadTextures | irBool | 1 | 0 | 1 |  | misc | 5 |
| OnPitRoad | irBool | 1 | 0 | 1 |  | pit-service | 5 |
| P2P_Count | irInt | 1 | 0 | 4 |  | misc | 5 |
| P2P_Status | irBool | 1 | 0 | 1 |  | misc | 5 |
| PaceMode | irInt | 1 | 0 | 4 | irsdk_PaceMode | race-control | 5 |
| PitOptRepairLeft | irFloat | 1 | 0 | 4 | s | pit-service | 5 |
| PitRepairLeft | irFloat | 1 | 0 | 4 | s | pit-service | 5 |
| PitSvFlags | irBitField | 1 | 0 | 4 | irsdk_PitSvFlags | race-control, pit-service | 5 |
| PitSvFuel | irFloat | 1 | 0 | 4 | l or kWh | pit-service, fuel | 5 |
| PitSvLFP | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| PitSvLRP | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| PitSvRFP | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| PitSvRRP | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| PitSvTireCompound | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| Pitch | irFloat | 1 | 0 | 4 | rad | pit-service, vehicle-dynamics | 5 |
| PitchRate | irFloat | 1 | 0 | 4 | rad/s | pit-service, vehicle-dynamics | 5 |
| PitchRate_ST | irFloat | 6 | 5 | 24 | rad/s | pit-service, vehicle-dynamics | 5 |
| PitsOpen | irBool | 1 | 0 | 1 |  | pit-service | 5 |
| PitstopActive | irBool | 1 | 0 | 1 |  | pit-service | 5 |
| PlayerCarClass | irInt | 1 | 0 | 4 |  | misc | 5 |
| PlayerCarClassPosition | irInt | 1 | 0 | 4 |  | scoring | 5 |
| PlayerCarDriverIncidentCount | irInt | 1 | 0 | 4 |  | session | 5 |
| PlayerCarDryTireSetLimit | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| PlayerCarIdx | irInt | 1 | 0 | 4 |  | per-car | 5 |
| PlayerCarInPitStall | irBool | 1 | 0 | 1 |  | pit-service | 5 |
| PlayerCarMyIncidentCount | irInt | 1 | 0 | 4 |  | session | 5 |
| PlayerCarPitSvStatus | irInt | 1 | 0 | 4 | irsdk_PitSvStatus | pit-service | 5 |
| PlayerCarPosition | irInt | 1 | 0 | 4 |  | scoring | 5 |
| PlayerCarPowerAdjust | irFloat | 1 | 0 | 4 | % | misc | 5 |
| PlayerCarSLBlinkRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| PlayerCarSLFirstRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| PlayerCarSLLastRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| PlayerCarSLShiftRPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| PlayerCarTeamIncidentCount | irInt | 1 | 0 | 4 |  | session | 5 |
| PlayerCarTowTime | irFloat | 1 | 0 | 4 | s | misc | 5 |
| PlayerCarWeightPenalty | irFloat | 1 | 0 | 4 | kg | vehicle-dynamics | 5 |
| PlayerFastRepairsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| PlayerIncidents | irInt | 1 | 0 | 4 | irsdk_IncidentFlags | misc | 5 |
| PlayerTireCompound | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| PlayerTrackSurface | irInt | 1 | 0 | 4 | irsdk_TrkLoc | misc | 5 |
| PlayerTrackSurfaceMaterial | irInt | 1 | 0 | 4 | irsdk_TrkSurf | misc | 5 |
| PowerMGU_H | irFloat | 1 | 0 | 4 | W | engine | 1 |
| PowerMGU_K | irFloat | 1 | 0 | 4 | W | engine | 1 |
| Precipitation | irFloat | 1 | 0 | 4 | % | pit-service, weather | 5 |
| PushToPass | irBool | 1 | 0 | 1 |  | misc | 5 |
| PushToTalk | irBool | 1 | 0 | 1 |  | misc | 5 |
| RFTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RFTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RFbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 5 |
| RFcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| RFodometer | irFloat | 1 | 0 | 4 | m | pit-service | 5 |
| RFshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| RFshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| RFshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| RFshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| RFtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RFtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RFtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RFwearL | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RFwearM | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RFwearR | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RPM | irFloat | 1 | 0 | 4 | revs/min | engine | 5 |
| RROLLshockDefl | irFloat | 1 | 0 | 4 | m | vehicle-dynamics | 1 |
| RROLLshockDefl_ST | irFloat | 6 | 5 | 24 | m | vehicle-dynamics | 1 |
| RROLLshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 1 |
| RROLLshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 1 |
| RRTiresAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RRTiresUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RRbrakeLinePress | irFloat | 1 | 0 | 4 | bar | input | 5 |
| RRcoldPressure | irFloat | 1 | 0 | 4 | kPa | pit-service | 5 |
| RRodometer | irFloat | 1 | 0 | 4 | m | pit-service | 5 |
| RRshockDefl | irFloat | 1 | 0 | 4 | m | misc | 4 |
| RRshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 4 |
| RRshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 4 |
| RRshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 4 |
| RRtempCL | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RRtempCM | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RRtempCR | irFloat | 1 | 0 | 4 | C | pit-service | 5 |
| RRwearL | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RRwearM | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RRwearR | irFloat | 1 | 0 | 4 | % | pit-service | 5 |
| RaceLaps | irInt | 1 | 0 | 4 |  | misc | 5 |
| RadioTransmitCarIdx | irInt | 1 | 0 | 4 |  | per-car, radio-camera | 5 |
| RadioTransmitFrequencyIdx | irInt | 1 | 0 | 4 |  | radio-camera | 5 |
| RadioTransmitRadioIdx | irInt | 1 | 0 | 4 |  | radio-camera | 5 |
| RearTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RearTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RelativeHumidity | irFloat | 1 | 0 | 4 | % | vehicle-dynamics | 5 |
| ReplayFrameNum | irInt | 1 | 0 | 4 |  | session | 5 |
| ReplayFrameNumEnd | irInt | 1 | 0 | 4 |  | session | 5 |
| ReplayPlaySlowMotion | irBool | 1 | 0 | 1 |  | session | 5 |
| ReplayPlaySpeed | irInt | 1 | 0 | 4 |  | session, vehicle-dynamics | 5 |
| ReplaySessionNum | irInt | 1 | 0 | 4 |  | session | 5 |
| ReplaySessionTime | irDouble | 1 | 0 | 8 | s | session | 5 |
| RightTireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| RightTireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| Roll | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 5 |
| RollRate | irFloat | 1 | 0 | 4 | rad/s | vehicle-dynamics | 5 |
| RollRate_ST | irFloat | 6 | 5 | 24 | rad/s | vehicle-dynamics | 5 |
| SessionFlags | irBitField | 1 | 0 | 4 | irsdk_Flags | session, race-control | 5 |
| SessionJokerLapsRemain | irInt | 1 | 0 | 4 |  | session, race-control | 5 |
| SessionLapsRemain | irInt | 1 | 0 | 4 |  | session | 5 |
| SessionLapsRemainEx | irInt | 1 | 0 | 4 |  | session | 5 |
| SessionLapsTotal | irInt | 1 | 0 | 4 |  | session | 5 |
| SessionNum | irInt | 1 | 0 | 4 |  | session | 5 |
| SessionOnJokerLap | irBool | 1 | 0 | 1 |  | session, race-control | 5 |
| SessionState | irInt | 1 | 0 | 4 | irsdk_SessionState | session | 5 |
| SessionTick | irInt | 1 | 0 | 4 |  | session | 5 |
| SessionTime | irDouble | 1 | 0 | 8 | s | session | 5 |
| SessionTimeOfDay | irFloat | 1 | 0 | 4 | s | session | 5 |
| SessionTimeRemain | irDouble | 1 | 0 | 8 | s | session | 5 |
| SessionTimeTotal | irDouble | 1 | 0 | 8 | s | session | 5 |
| SessionUniqueID | irInt | 1 | 0 | 4 |  | session | 5 |
| ShiftGrindRPM | irFloat | 1 | 0 | 4 | RPM | engine | 5 |
| ShiftIndicatorPct | irFloat | 1 | 0 | 4 | % | engine | 5 |
| ShiftPowerPct | irFloat | 1 | 0 | 4 | % | input | 5 |
| Shifter | irInt | 1 | 0 | 4 |  | misc | 5 |
| Skies | irInt | 1 | 0 | 4 |  | misc | 5 |
| SolarAltitude | irFloat | 1 | 0 | 4 | rad | weather, vehicle-dynamics | 5 |
| SolarAzimuth | irFloat | 1 | 0 | 4 | rad | weather | 5 |
| Speed | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 5 |
| SteeringFFBEnabled | irBool | 1 | 0 | 1 |  | input | 5 |
| SteeringWheelAngle | irFloat | 1 | 0 | 4 | rad | input | 5 |
| SteeringWheelAngleMax | irFloat | 1 | 0 | 4 | rad | input | 5 |
| SteeringWheelLimiter | irFloat | 1 | 0 | 4 | % | input, vehicle-dynamics | 5 |
| SteeringWheelMaxForceNm | irFloat | 1 | 0 | 4 | N*m | input | 5 |
| SteeringWheelPctDamper | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPctIntensity | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPctSmoothing | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPctTorque | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPctTorqueSign | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPctTorqueSignStops | irFloat | 1 | 0 | 4 | % | input | 5 |
| SteeringWheelPeakForceNm | irFloat | 1 | 0 | 4 | N*m | input | 5 |
| SteeringWheelTorque | irFloat | 1 | 0 | 4 | N*m | input | 5 |
| SteeringWheelTorque_ST | irFloat | 6 | 5 | 24 | N*m | input | 5 |
| SteeringWheelUseLinear | irBool | 1 | 0 | 1 |  | input | 5 |
| TRshockDefl | irFloat | 1 | 0 | 4 | m | misc | 1 |
| TRshockDefl_ST | irFloat | 6 | 5 | 24 | m | misc | 1 |
| TRshockVel | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 1 |
| TRshockVel_ST | irFloat | 6 | 5 | 24 | m/s | vehicle-dynamics | 1 |
| Throttle | irFloat | 1 | 0 | 4 | % | input | 5 |
| ThrottleRaw | irFloat | 1 | 0 | 4 | % | input | 5 |
| TireLF_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 5 |
| TireLR_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 5 |
| TireRF_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 5 |
| TireRR_RumblePitch | irFloat | 1 | 0 | 4 | Hz | pit-service, vehicle-dynamics | 5 |
| TireSetsAvailable | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| TireSetsUsed | irInt | 1 | 0 | 4 |  | pit-service | 5 |
| TorqueMGU_K | irFloat | 1 | 0 | 4 | Nm | engine | 1 |
| TrackTemp | irFloat | 1 | 0 | 4 | C | weather | 5 |
| TrackTempCrew | irFloat | 1 | 0 | 4 | C | weather | 5 |
| TrackWetness | irInt | 1 | 0 | 4 | irsdk_TrackWetness | weather | 5 |
| VelocityX | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 5 |
| VelocityX_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 5 |
| VelocityY | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 5 |
| VelocityY_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 5 |
| VelocityZ | irFloat | 1 | 0 | 4 | m/s | vehicle-dynamics | 5 |
| VelocityZ_ST | irFloat | 6 | 5 | 24 | m/s at 360 Hz | vehicle-dynamics | 5 |
| VertAccel | irFloat | 1 | 0 | 4 | m/s^2 | vehicle-dynamics | 5 |
| VertAccel_ST | irFloat | 6 | 5 | 24 | m/s^2 | vehicle-dynamics | 5 |
| VidCapActive | irBool | 1 | 0 | 1 |  | misc | 5 |
| VidCapEnabled | irBool | 1 | 0 | 1 |  | misc | 5 |
| Voltage | irFloat | 1 | 0 | 4 | V | engine | 5 |
| WaterLevel | irFloat | 1 | 0 | 4 | l | engine | 5 |
| WaterTemp | irFloat | 1 | 0 | 4 | C | engine | 5 |
| WeatherDeclaredWet | irBool | 1 | 0 | 1 |  | pit-service, weather | 5 |
| WindDir | irFloat | 1 | 0 | 4 | rad | weather | 5 |
| WindVel | irFloat | 1 | 0 | 4 | m/s | weather, vehicle-dynamics | 5 |
| Yaw | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 5 |
| YawNorth | irFloat | 1 | 0 | 4 | rad | vehicle-dynamics | 5 |
| YawRate | irFloat | 1 | 0 | 4 | rad/s | vehicle-dynamics | 5 |
| YawRate_ST | irFloat | 6 | 5 | 24 | rad/s | vehicle-dynamics | 5 |
| dcABS | irFloat | 1 | 0 | 4 |  | misc | 4 |
| dcABSToggle | irBool | 1 | 0 | 1 |  | misc | 2 |
| dcAntiRollFront | irFloat | 1 | 0 | 4 |  | vehicle-dynamics | 1 |
| dcAntiRollRear | irFloat | 1 | 0 | 4 |  | vehicle-dynamics | 1 |
| dcBrakeBias | irFloat | 1 | 0 | 4 |  | input | 5 |
| dcDashPage | irFloat | 1 | 0 | 4 |  | misc | 3 |
| dcHeadlightFlash | irBool | 1 | 0 | 1 |  | misc | 4 |
| dcLowFuelAccept | irBool | 1 | 0 | 1 |  | fuel | 2 |
| dcPitSpeedLimiterToggle | irBool | 1 | 0 | 1 |  | pit-service, vehicle-dynamics | 4 |
| dcStarter | irBool | 1 | 0 | 1 |  | misc | 5 |
| dcThrottleShape | irFloat | 1 | 0 | 4 |  | input | 1 |
| dcToggleWindshieldWipers | irBool | 1 | 0 | 1 |  | weather | 4 |
| dcTractionControl | irFloat | 1 | 0 | 4 |  | misc | 4 |
| dcTractionControl2 | irFloat | 1 | 0 | 4 |  | misc | 1 |
| dcTractionControlToggle | irBool | 1 | 0 | 1 |  | misc | 3 |
| dcTriggerWindshieldWipers | irBool | 1 | 0 | 1 |  | weather | 4 |
| dpFastRepair | irFloat | 1 | 0 | 4 |  | pit-service | 5 |
| dpFuelAddKg | irFloat | 1 | 0 | 4 | kg | pit-service, fuel | 5 |
| dpFuelAutoFillActive | irFloat | 1 | 0 | 4 |  | race-control, pit-service, fuel | 5 |
| dpFuelAutoFillEnabled | irFloat | 1 | 0 | 4 |  | pit-service, fuel | 5 |
| dpFuelFill | irFloat | 1 | 0 | 4 |  | race-control, pit-service, fuel | 5 |
| dpLFTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 4 |
| dpLFTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 5 |
| dpLRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 4 |
| dpLRTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 5 |
| dpLTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpRFTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 4 |
| dpRFTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 5 |
| dpRRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 4 |
| dpRRTireColdPress | irFloat | 1 | 0 | 4 | Pa | pit-service | 5 |
| dpRTireChange | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWeightJackerLeft | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWeightJackerRight | irFloat | 1 | 0 | 4 |  | pit-service | 1 |
| dpWindshieldTearoff | irFloat | 1 | 0 | 4 |  | pit-service, weather | 5 |
