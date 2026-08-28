# VR Research Data - ADB Pull Guide

## Quick Commands

### Pull all research data from Quest
```bash
adb pull /sdcard/Android/data/com.unity.xr.interaction.examples/files/VRResearch/ ./vr_research_data/
```

### Pull specific session
```bash
adb pull /sdcard/Android/data/com.unity.xr.interaction.examples/files/VRResearch/P001_20260826_143022/ ./vr_research_data/session1/
```

### List available sessions on device
```bash
adb shell ls /sdcard/Android/data/com.unity.xr.interaction.examples/files/VRResearch/
```

### View CSV content directly
```bash
adb shell cat /sdcard/Android/data/com.unity.xr.interaction.examples/files/VRResearch/*/movement_data.csv | head -20
```

## Using Meta Quest Developer Hub (MQDH)

1. Open MQDH → File Manager
2. Navigate to: `Android/data/com.unity.xr.interaction.examples/files/VRResearch/`
3. Select session folder → Download

## File Structure on Device

```
/sdcard/Android/data/com.unity.xr.interaction.examples/files/VRResearch/
├── P001_20260826_143022/
│   ├── movement_data.csv       (Frame-by-frame tracking)
│   ├── movement_data.json      (Same data in JSON)
│   └── interaction_events.json (Grab/Release/Locomotion events)
├── P001_20260826_150045/
│   └── ...
```

## Verifying Data Quality

### Check CSV has data (not empty)
```bash
wc -l ./vr_research_data/*/movement_data.csv
```

### Check for zero-values (indicates tracking failure)
```bash
grep ",0.0000,0.0000,0.0000," ./vr_research_data/*/movement_data.csv | head -5
```

### Check interaction events exist
```bash
cat ./vr_research_data/*/interaction_events.json | python3 -m json.tool | head -30
```
