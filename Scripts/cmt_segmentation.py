#!/usr/bin/env python3
"""
cmt_segmentation.py — Knee cartilage segmentation via CartiMorph-nnUNet.

Usage:
    python cmt_segmentation.py --input <input.nii.gz> --model-dir <models/> --output-dir <output/>

Outputs:
    <output-dir>/segmentation_mask.nii.gz   — 5-class label map
    <output-dir>/segmentation_info.json     — label definitions

Progress reporting:
    Writes PROGRESS:<percent>:<message> lines to stdout.
"""

import argparse
import json
import os
import sys
import time
import traceback

# ---------------------------------------------------------------------------
# Monkey-patch: PyTorch >= 2.6 defaults to weights_only=True for torch.load.
# The CartiMorph-nnUNet checkpoint was saved with an older PyTorch and
# contains numpy scalars that are blocked by the safe loader.
# ---------------------------------------------------------------------------
import torch as _torch
_original_load = _torch.load
def _patched_load(*args, **kwargs):
    kwargs.setdefault("weights_only", False)
    return _original_load(*args, **kwargs)
_torch.load = _patched_load
# ---------------------------------------------------------------------------


def report_progress(percent: int, message: str):
    """Emit a progress line that ExternalProcessRunner can parse."""
    sys.stdout.write(f"PROGRESS:{percent}:{message}\n")
    sys.stdout.flush()


def run_segmentation(
    input_path: str,
    model_dir: str,
    output_dir: str,
    progress_callback=None,
) -> tuple:
    """
    Run knee cartilage segmentation via CartiMorph-nnUNet.

    Args:
        input_path: Path to input NIfTI file (.nii or .nii.gz).
        model_dir: Directory containing nnUNet model with fold_0/ structure.
        output_dir: Directory for output files.
        progress_callback: Optional callable(percent: int, message: str)
            for progress reporting.

    Returns:
        (mask_path, info_dict) where:
            mask_path: Path to segmentation_mask.nii.gz
            info_dict: Label definitions and metadata.

    Raises:
        FileNotFoundError: If model files are missing.
        ImportError: If CartiMorph-nnUNet cannot be imported.
        RuntimeError: On inference failure.
    """
    import nibabel as nib
    import numpy as np

    def _progress(percent: int, message: str):
        if progress_callback:
            progress_callback(percent, message)

    os.makedirs(output_dir, exist_ok=True)

    # ------------------------------------------------------------------
    # Step 1: Load input image
    # ------------------------------------------------------------------
    _progress(5, "Loading input image...")
    img = nib.load(input_path)
    data = img.get_fdata().astype(np.float32)
    affine = img.affine
    original_shape = data.shape

    # Voxel dimensions from affine
    voxel_dims = np.sqrt(np.sum(affine[:3, :3] ** 2, axis=0))
    _progress(10, f"Image shape: {data.shape}, voxel: {[f'{v:.2f}mm' for v in voxel_dims]}")

    # ------------------------------------------------------------------
    # Step 2: Preprocess for nnUNet — reorient to RAS+
    # ------------------------------------------------------------------
    _progress(15, "Preprocessing (reorientation, resampling)...")
    import SimpleITK as sitk

    # Convert to SimpleITK: numpy is (z,y,x), SITK expects (x,y,z)
    sitk_img = sitk.GetImageFromArray(data.transpose(2, 1, 0))
    sitk_img.SetSpacing([float(v) for v in voxel_dims])

    # Reorient to RAS+
    ras_filter = sitk.DICOMOrientImageFilter()
    ras_filter.SetDesiredCoordinateOrientation("RAS")
    sitk_img = ras_filter.Execute(sitk_img)

    # Record original space for later resampling
    original_sitk = sitk_img

    # Resample to model's expected input size (160, 384, 384)
    target_size = (160, 384, 384)
    original_size = sitk_img.GetSize()
    original_spacing = sitk_img.GetSpacing()

    target_spacing = tuple(
        (sz_old * sp) / sz_new
        for sz_old, sp, sz_new in zip(original_size, original_spacing, target_size)
    )

    resampler = sitk.ResampleImageFilter()
    resampler.SetSize(target_size)
    resampler.SetOutputSpacing(target_spacing)
    resampler.SetInterpolator(sitk.sitkLinear)
    resampler.SetOutputDirection(sitk_img.GetDirection())
    resampler.SetOutputOrigin(sitk_img.GetOrigin())
    sitk_img = resampler.Execute(sitk_img)

    # Back to numpy (z,y,x)
    preprocessed = sitk.GetArrayFromImage(sitk_img).transpose(2, 1, 0).astype(np.float32)
    _progress(25, "Preprocessing complete")

    # ------------------------------------------------------------------
    # Step 3: Set up CartiMorph-nnUNet environment
    # ------------------------------------------------------------------
    _progress(30, "Setting up nnUNet environment...")

    # nnUNet requires these environment variables to be set
    os.environ.setdefault("RESULTS_FOLDER", os.path.join(output_dir, "_nnunet_results"))
    os.environ.setdefault("nnUNet_raw_data_base", os.path.join(output_dir, "_nnunet_raw"))
    os.environ.setdefault("nnUNet_preprocessed", os.path.join(output_dir, "_nnunet_preprocessed"))

    # ------------------------------------------------------------------
    # Step 4: Run nnUNet inference
    # ------------------------------------------------------------------
    _progress(35, "Running CartiMorph-nnUNet segmentation...")

    # Prepare temp input folder for nnUNet predict_from_folder
    tmp_input = os.path.join(output_dir, "_tmp_nnunet_input")
    tmp_output = os.path.join(output_dir, "_tmp_nnunet_output")
    os.makedirs(tmp_input, exist_ok=True)
    os.makedirs(tmp_output, exist_ok=True)

    # Save preprocessed volume as NIfTI for nnUNet (expected name: CASENAME_0000.nii.gz)
    nii_input = nib.Nifti1Image(preprocessed.transpose(2, 1, 0), np.eye(4))
    nib.save(nii_input, os.path.join(tmp_input, "case_0000.nii.gz"))

    # Validate model directory structure
    plans_path = os.path.join(model_dir, "plans.pkl")
    fold0_model = os.path.join(model_dir, "fold_0", "model_best.model")

    if not os.path.exists(plans_path):
        raise FileNotFoundError(f"Model plans not found: {plans_path}")
    if not os.path.exists(fold0_model):
        raise FileNotFoundError(f"Model checkpoint not found: {fold0_model}")

    try:
        from CartiMorph_nnUNet.inference.predict import predict_from_folder

        _progress(40, "Running nnUNet inference...")
        sys.stdout.flush()

        predict_from_folder(
            model=model_dir,
            input_folder=tmp_input,
            output_folder=tmp_output,
            folds=(0,),
            save_npz=False,
            num_threads_preprocessing=2,
            num_threads_nifti_save=2,
            lowres_segmentations=None,
            part_id=0,
            num_parts=1,
            tta=False,  # Disable TTA for CPU speed (~8x faster)
            mixed_precision=True,
            overwrite_existing=True,
            step_size=0.5,
            checkpoint_name="model_best",
        )
        _progress(80, "Inference complete")
    except ImportError as e:
        raise ImportError(f"CartiMorph-nnUNet import failed: {e}") from e

    # Read back result
    result_path = os.path.join(tmp_output, "case.nii.gz")
    if not os.path.exists(result_path):
        raise RuntimeError(f"Result file not found: {result_path}")

    result_img = nib.load(result_path)
    mask = result_img.get_fdata().astype(np.uint8)
    _progress(82, f"Mask shape: {mask.shape}, unique labels: {list(np.unique(mask))}")

    # ------------------------------------------------------------------
    # Step 5: Resample mask back to original space
    # ------------------------------------------------------------------
    _progress(85, "Resampling mask back to original space...")

    # mask is numpy (z,y,x), convert to SITK (x,y,z)
    mask_sitk = sitk.GetImageFromArray(mask.transpose(2, 1, 0).astype(np.float32))

    # Copy spatial info from the preprocessed (resampled) image
    mask_sitk.SetSpacing(sitk_img.GetSpacing())
    mask_sitk.SetDirection(sitk_img.GetDirection())
    mask_sitk.SetOrigin(sitk_img.GetOrigin())

    # Resample back to original space using nearest-neighbor
    resampler_back = sitk.ResampleImageFilter()
    resampler_back.SetSize(original_size)
    resampler_back.SetOutputSpacing(original_spacing)
    resampler_back.SetOutputDirection(original_sitk.GetDirection())
    resampler_back.SetOutputOrigin(original_sitk.GetOrigin())
    resampler_back.SetInterpolator(sitk.sitkNearestNeighbor)
    mask_original_sitk = resampler_back.Execute(mask_sitk)

    mask_original = sitk.GetArrayFromImage(mask_original_sitk).transpose(2, 1, 0).astype(np.uint8)
    _progress(90, f"Resampled mask shape: {mask_original.shape}")

    # ------------------------------------------------------------------
    # Step 6: Save outputs
    # ------------------------------------------------------------------
    _progress(92, "Saving segmentation results...")
    mask_path = os.path.join(output_dir, "segmentation_mask.nii.gz")
    out_img = nib.Nifti1Image(mask_original.astype(np.uint8), affine)
    nib.save(out_img, mask_path)

    # Build label definitions
    label_info = {
        "labels": {
            "0": "Background",
            "1": "Femoral Cartilage (FC)",
            "2": "Medial Tibial Cartilage (MTC)",
            "3": "Lateral Tibial Cartilage (LTC)",
            "4": "Femur",
            "5": "Tibia"
        },
        "voxel_size_mm": [float(v) for v in voxel_dims],
        "original_shape": list(original_shape),
        "mask_shape": list(mask_original.shape)
    }

    # Also write info JSON to output_dir
    info_path = os.path.join(output_dir, "segmentation_info.json")
    with open(info_path, 'w', encoding='utf-8') as f:
        json.dump(label_info, f, indent=2, ensure_ascii=False)

    _progress(100, f"Segmentation complete. Found labels: {list(np.unique(mask_original))}")
    return mask_path, label_info


def main():
    parser = argparse.ArgumentParser(description="Knee cartilage segmentation")
    parser.add_argument("--input", required=True, help="Input NIfTI file (.nii or .nii.gz)")
    parser.add_argument("--model-dir", required=True, help="Directory containing nnUNet model with fold_0/ structure")
    parser.add_argument("--output-dir", required=True, help="Directory for output files")
    args = parser.parse_args()

    try:
        mask_path, label_info = run_segmentation(
            input_path=args.input,
            model_dir=args.model_dir,
            output_dir=args.output_dir,
            progress_callback=report_progress,
        )
        unique_labels = list(set(str(k) for k in label_info.get("labels", {}).keys()))
        print(f"Output: {mask_path}")
        print(f"Labels: {unique_labels}")
        print("SCRIPT_DONE")  # Marker for C# to confirm clean exit
    except Exception as e:
        report_progress(-1, f"Segmentation failed: {e}")
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
