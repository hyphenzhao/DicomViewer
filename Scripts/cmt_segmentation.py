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


def main():
    parser = argparse.ArgumentParser(description="Knee cartilage segmentation")
    parser.add_argument("--input", required=True, help="Input NIfTI file (.nii or .nii.gz)")
    parser.add_argument("--model-dir", required=True, help="Directory containing nnUNet model with fold_0/ structure")
    parser.add_argument("--output-dir", required=True, help="Directory for output files")
    args = parser.parse_args()

    os.makedirs(args.output_dir, exist_ok=True)

    try:
        import nibabel as nib
        import numpy as np

        # ------------------------------------------------------------------
        # Step 1: Load input image
        # ------------------------------------------------------------------
        report_progress(5, "Loading input image...")
        img = nib.load(args.input)
        data = img.get_fdata().astype(np.float32)
        affine = img.affine
        original_shape = data.shape

        # Voxel dimensions from affine
        voxel_dims = np.sqrt(np.sum(affine[:3, :3] ** 2, axis=0))
        report_progress(10, f"Image shape: {data.shape}, voxel: {[f'{v:.2f}mm' for v in voxel_dims]}")

        # ------------------------------------------------------------------
        # Step 2: Preprocess for nnUNet — reorient to RAS+
        # ------------------------------------------------------------------
        report_progress(15, "Preprocessing (reorientation, resampling)...")
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
        report_progress(25, "Preprocessing complete")

        # ------------------------------------------------------------------
        # Step 3: Set up CartiMorph-nnUNet environment
        # ------------------------------------------------------------------
        report_progress(30, "Setting up nnUNet environment...")

        # nnUNet requires these environment variables to be set
        os.environ.setdefault("RESULTS_FOLDER", os.path.join(args.output_dir, "_nnunet_results"))
        os.environ.setdefault("nnUNet_raw_data_base", os.path.join(args.output_dir, "_nnunet_raw"))
        os.environ.setdefault("nnUNet_preprocessed", os.path.join(args.output_dir, "_nnunet_preprocessed"))

        # ------------------------------------------------------------------
        # Step 4: Run nnUNet inference
        # ------------------------------------------------------------------
        report_progress(35, "Running CartiMorph-nnUNet segmentation...")

        # Prepare temp input folder for nnUNet predict_from_folder
        tmp_input = os.path.join(args.output_dir, "_tmp_nnunet_input")
        tmp_output = os.path.join(args.output_dir, "_tmp_nnunet_output")
        os.makedirs(tmp_input, exist_ok=True)
        os.makedirs(tmp_output, exist_ok=True)

        # Save preprocessed volume as NIfTI for nnUNet (expected name: CASENAME_0000.nii.gz)
        nii_input = nib.Nifti1Image(preprocessed.transpose(2, 1, 0), np.eye(4))
        nib.save(nii_input, os.path.join(tmp_input, "case_0000.nii.gz"))

        # Validate model directory structure
        model_dir = args.model_dir
        plans_path = os.path.join(model_dir, "plans.pkl")
        fold0_model = os.path.join(model_dir, "fold_0", "model_best.model")
        fold0_pkl = os.path.join(model_dir, "fold_0", "model_best.model.pkl")

        if not os.path.exists(plans_path):
            report_progress(-1, f"Model plans not found: {plans_path}")
            sys.exit(1)
        if not os.path.exists(fold0_model):
            report_progress(-1, f"Model checkpoint not found: {fold0_model}")
            sys.exit(1)

        try:
            from CartiMorph_nnUNet.inference.predict import predict_from_folder

            report_progress(40, "Running nnUNet inference (CPU, may take a while)...")
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
            report_progress(80, "Inference complete")
        except ImportError as e:
            report_progress(-1, f"CartiMorph-nnUNet import failed: {e}")
            sys.exit(1)
        except Exception as e:
            report_progress(-1, f"Inference failed: {e}\n{traceback.format_exc()}")
            sys.exit(1)

        # Read back result
        result_path = os.path.join(tmp_output, "case.nii.gz")
        if not os.path.exists(result_path):
            report_progress(-1, f"Result file not found: {result_path}")
            sys.exit(1)

        result_img = nib.load(result_path)
        mask = result_img.get_fdata().astype(np.uint8)
        report_progress(82, f"Mask shape: {mask.shape}, unique labels: {list(np.unique(mask))}")

        # ------------------------------------------------------------------
        # Step 5: Resample mask back to original space
        # ------------------------------------------------------------------
        report_progress(85, "Resampling mask back to original space...")

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
        report_progress(90, f"Resampled mask shape: {mask_original.shape}")

        # ------------------------------------------------------------------
        # Step 6: Save outputs
        # ------------------------------------------------------------------
        report_progress(92, "Saving segmentation results...")
        output_path = os.path.join(args.output_dir, "segmentation_mask.nii.gz")
        out_img = nib.Nifti1Image(mask_original.astype(np.uint8), affine)
        nib.save(out_img, output_path)

        # Save label definitions
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
        info_path = os.path.join(args.output_dir, "segmentation_info.json")
        with open(info_path, 'w', encoding='utf-8') as f:
            json.dump(label_info, f, indent=2, ensure_ascii=False)

        unique_labels = np.unique(mask_original)
        report_progress(100, f"Segmentation complete. Found labels: {list(unique_labels)}")
        print(f"Output: {output_path}")
        print(f"Labels: {list(unique_labels)}")
        print("SCRIPT_DONE")  # Marker for C# to confirm clean exit

    except Exception as e:
        report_progress(-1, f"Segmentation failed: {e}")
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
