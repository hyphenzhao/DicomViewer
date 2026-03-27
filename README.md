# DicomViewer

Windows desktop medical volume viewer for DICOM series and NIfTI volumes.

## Current UI structure

- Left navigation panel
  - **Load DICOM files...** button
  - status summary for the loaded study or volume
- Main viewer area with 4 panes
  - **Axial**
  - **Coronal**
  - **Sagittal**
  - **3D** placeholder

## Current implementation status

- Sidebar-driven loading workflow for volumetric medical image data
- Loads a DICOM series by scanning the selected folder for readable slices
- Filters slices to a consistent series and image size before assembling the volume
- Orders DICOM slices using spatial metadata when available, with tag-based fallbacks
- Loads NIfTI-1 volumes, including `.nii` and gzip-compressed `.nii.gz` files
- Builds an internal 3D voxel volume representation
- Renders:
  - axial slice
  - coronal reformatted preview
  - sagittal reformatted preview
- Applies DICOM rescale and `MONOCHROME1` inversion handling during import
- Keeps the fourth pane as a placeholder for future 3D reconstruction
- Each pane is independently scrollable

## Notes

- Implemented as a Windows Forms desktop application on .NET 8.
- The layout is desktop-style and viewer-oriented, but it is not a native MFC/ATL C++ application.

## Planned next steps

- Window/level controls
- Crosshair sync between views
- Zoom/pan tools
- Real 3D reconstruction pane
