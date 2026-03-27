# DicomViewer

Windows desktop DICOM viewer scaffold inspired by the Python `dicom_prototype`.

## Current UI structure

- Left navigation panel
  - **Load DICOM files...** button
  - status summary for the loaded study
- Main viewer area with 4 panes
  - **Axial**
  - **Coronal**
  - **Sagittal**
  - **3D** placeholder

## Current implementation status

- Sidebar-driven navigation with a **Load DICOM Files...** action
- User selects one DICOM file, then the app scans that folder for the full DICOM series
- Builds an internal volume representation from the discovered slice sequence
- Renders:
  - axial slice
  - coronal reformatted preview
  - sagittal reformatted preview
- Keeps the fourth pane as a placeholder for future 3D reconstruction
- Each pane is independently scrollable

## Notes

- This is currently implemented as a Windows Forms Windows app structure.
- It is **MFC-like in layout and desktop behavior**, but it is **not literal native MFC/ATL C++**.
- If you want, the next step can be either:
  1. keep this in modern C#/.NET and improve it, or
  2. convert the structure into a true native **MFC C++** project.

## Planned next steps

- Load a full DICOM series from a folder instead of a single file
- Proper slice ordering and volume assembly
- Window/level controls
- Crosshair sync between views
- Zoom/pan tools
- Real 3D reconstruction pane
