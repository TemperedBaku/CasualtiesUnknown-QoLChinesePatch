# Changelog

## v0.10.14 legacy-base QoL 1.0.4.5 backport

- Based on v0.10.11 scene_reprime_fix.
- Backported QoL Unknown 1.0.4.5 / v7.0.1 text entries from the v0.11.x test line.
- Added QoLModuleManager registry localization for module names/descriptions.
- Extended Bootstrap IMGUI coverage from GUI.* to GUI/GUILayout text methods.
- Kept the older Bootstrap lifecycle and scene re-prime behavior.
- Did not include ironman / console-lock / fog-of-war-resolution changes.


## v0.10.14 copyright-safe UI final

- Reverted broad wiki-name import from v0.10.13.
- Kept only targeted QoL UI/dropdown internal IDs needed by QoL 1.0.4.5.
- Added remaining labels: `Seed` -> `种子`, `Custom settings` -> `自定义设置`.
- Does not include full wiki descriptions or wholesale wiki content.
