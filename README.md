# MatsPrefsUnifier

A Unity Editor tool for batch-copying selected lilToon material settings.

## Concept

MatsPrefsUnifier was created to simplify repetitive material configuration when working with avatars.

When an avatar uses multiple materials, manually adjusting shadow, rim shading, and distance fade settings across each material can be time-consuming.

This tool lets you select a template material and copy the settings you need to multiple target materials in one operation.

## Overview

- **Unity Version:** 2022.3.22f1
- **Menu:** `nur_ieren > MatsPrefsUnifier`

## Features

- **Multiple Target Selection Methods** — Find materials by specifying a folder or a GameObject hierarchy.
- **Recursive Folder Search** — Optionally include materials in subfolders.
- **GameObject Hierarchy Search** — Collect materials assigned to MeshRenderers and SkinnedMeshRenderers, with an option to include inactive objects.
- **lilToon Family Support** — Recognizes lilToon materials and supported derived shaders, including Uzumore Shader.
- **Shadow Settings** — Copies matching shadow-related shader properties.
- **Rim Shading Settings** — Copies matching rim-related shader properties.
- **Distance Fade Settings** — Copies matching distance fade shader properties.
- **Keyword Synchronization** — Optionally synchronizes related shader keywords while preserving unrelated keywords.
- **Preview** — Lists the materials found before applying changes.
- **Progress and Cancellation** — Displays a progress bar during batch processing and allows the operation to be canceled.
- **Detailed Logs** — Reports the materials processed and the results of the operation.
- **Undo Support** — Records material changes for Unity's Undo system.

## How to use

1. Open `nur_ieren > MatsPrefsUnifier`.
2. Choose either folder-based selection or GameObject-based selection.
3. Specify the target folder or GameObject.
4. If using a folder, choose whether to include subfolders.
5. If using a GameObject, choose whether to include inactive renderers.
6. Assign a template material that uses a supported lilToon-family shader.
7. Select the settings to copy:
   - Shadow
   - Rim
   - Distance Fade
   - Related ON/OFF shader keywords
8. Click `プレビュー（対象マテリアルを確認）` to review the materials that will be processed.
9. Click `一括上書きを実行` and confirm the operation.

### Important notes

- The template material is used as the source and is skipped during batch processing.
- Only the selected categories of shader properties are copied.
- Properties that do not exist on a target material are skipped.
- When keyword synchronization is enabled, unrelated shader keywords are preserved.
- In GameObject mode, only materials assigned to MeshRenderers and SkinnedMeshRenderers are collected.
- Unsaved material instances are excluded from GameObject-based processing.
- Canceling an operation stops further processing, but materials already updated remain changed.
- Back up your project before running a large batch operation.

## Development

This project was created to make repetitive material adjustments more efficient and consistent across avatar projects.

## Author

nur_ieren

[X](https://x.com/nur_ieren)

## Licence
This project is licensed under the MIT License. See the LICENSE file for details.

[MIT License](https://opensource.org/license/mit)  
