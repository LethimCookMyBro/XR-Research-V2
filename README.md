# XR Research V2 — Meta Quest 3

Unity 6 VR research application for the TMU/TMU study. This repository started from Unity XR Interaction Toolkit Examples, but the active project is the ResearchFlow implementation below; the upstream XRI sample README was archived during project cleanup.

## Active project

- Main scene: `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`
- ResearchFlow code/UI/tests: `Assets/VRMaintenanceResearch/ResearchFlow/`
- Reused hand/controller/logger runtime: `Assets/VRMaintenanceResearch/HandsLabQuest3/`
- UI/font dependency: `Assets/VRMaintenanceResearch/TrainingV2/`
- Quest helper scripts: `Tools/`
- Current integration status: `Logs/QuestFinalIntegration/FINAL_INTEGRATION_STATUS.md`

## Important

The working tree is intentionally dirty and contains active research work. Do not use broad Git cleanup/reset operations.

Real-study Task A/B production content is not approved yet. Technical/demo fixtures are not study content.

For AI agents, follow `AGENTS.md`. Historical evidence under `Logs/` should not be recursively scanned.

## Upstream base

Unity XRI Examples 3.4.0 remains partially present because current scenes/runtime still depend on XRI assets and packages. `LICENSE.md` is retained; the upstream README/changelog and unused example documentation were archived outside the project during cleanup.
