#!/usr/bin/env python3
"""
cmt_quantification.py — Cartilage morphological quantification.

Usage:
    python cmt_quantification.py --seg <segmentation.nii.gz> --output-dir <output/>

Input:
    Segmentation mask with labels:
        0=背景, 1=股骨软骨, 2=内侧胫骨软骨, 3=外侧胫骨软骨, 4=股骨, 5=胫骨

Outputs:
    <output-dir>/thickness_map.nii.gz      — 3D cartilage thickness map
    <output-dir>/quantification_report.json — volume, surface area, thickness metrics

Progress reporting:
    Writes PROGRESS:<percent>:<message> lines to stdout.
"""

import argparse
import json
import os
import sys
import traceback


def report_progress(percent: int, message: str):
    sys.stdout.write(f"PROGRESS:{percent}:{message}\n")
    sys.stdout.flush()


def main():
    parser = argparse.ArgumentParser(description="Cartilage quantification")
    parser.add_argument("--seg", required=True, help="Segmentation mask NIfTI file")
    parser.add_argument("--output-dir", required=True, help="Directory for output files")
    args = parser.parse_args()

    os.makedirs(args.output_dir, exist_ok=True)

    try:
        import nibabel as nib
        import numpy as np
        from scipy import ndimage

        # ------------------------------------------------------------------
        # Step 1: Load segmentation
        # ------------------------------------------------------------------
        report_progress(5, "正在加载分割掩膜...")
        img = nib.load(args.seg)
        mask = img.get_fdata().astype(np.int32)
        affine = img.affine

        # Voxel dimensions from affine
        voxel_dims = np.sqrt(np.sum(affine[:3, :3] ** 2, axis=0))
        voxel_volume_mm3 = float(np.prod(voxel_dims))

        # Cartilage labels
        cartilage_labels = {
            1: "股骨软骨 (Femoral Cartilage)",
            2: "内侧胫骨软骨 (Medial Tibial Cartilage)",
            3: "外侧胫骨软骨 (Lateral Tibial Cartilage)",
        }

        # Bone labels (for reference)
        bone_labels = {
            4: "股骨 (Femur)",
            5: "胫骨 (Tibia)",
        }

        report_progress(10, f"分割尺寸: {mask.shape}, 体素体积: {voxel_volume_mm3:.4f}mm³")

        # ------------------------------------------------------------------
        # Step 2: Volume computation
        # ------------------------------------------------------------------
        report_progress(20, "正在计算各 ROI 体积...")
        volume_results = {}

        for label, name in {**cartilage_labels, **bone_labels}.items():
            voxel_count = int(np.sum(mask == label))
            volume_mm3 = voxel_count * voxel_volume_mm3
            volume_results[name] = {
                "label": label,
                "voxel_count": voxel_count,
                "volume_mm3": round(volume_mm3, 2),
                "volume_ml": round(volume_mm3 / 1000.0, 4),
            }

        report_progress(40, "体积计算完成")

        # ------------------------------------------------------------------
        # Step 3: Thickness map via 3D Euclidean distance transform
        # ------------------------------------------------------------------
        report_progress(45, "正在计算软骨厚度图 (3D 距离变换)...")

        thickness_map = np.zeros_like(mask, dtype=np.float32)

        for label, name in cartilage_labels.items():
            report_progress(45 + (label * 10), f"正在处理 {name}...")

            # Isolate this cartilage
            cartilage_binary = (mask == label).astype(np.uint8)

            if np.sum(cartilage_binary) == 0:
                continue

            # Distance to background within the cartilage
            # Compute distance from each cartilage voxel to the nearest non-cartilage voxel
            dist_internal = ndimage.distance_transform_edt(cartilage_binary, sampling=list(voxel_dims))

            # For thickness: skeleton-based approach
            # 1. Find the surface of the cartilage (boundary with background)
            # 2. For each cartilage voxel, find distance to nearest surface point
            # This gives half-thickness (distance to nearest boundary)
            # Double it for full-thickness estimate

            # Surface detection: cartilage voxels adjacent to non-cartilage
            eroded = ndimage.binary_erosion(cartilage_binary, iterations=1)
            surface = cartilage_binary.astype(bool) & ~eroded

            if np.sum(surface) == 0:
                continue

            # Distance from each cartilage voxel to nearest surface point
            dist_to_surface = ndimage.distance_transform_edt(
                ~surface.astype(bool),
                sampling=list(voxel_dims)
            )

            # Thickness ≈ 2 × distance to nearest surface (local thickness)
            thickness_map[cartilage_binary > 0] = np.maximum(
                thickness_map[cartilage_binary > 0],
                dist_to_surface[cartilage_binary > 0] * 2.0
            )

        report_progress(80, "厚度图计算完成")

        # ------------------------------------------------------------------
        # Step 4: Surface area estimation (marching cubes)
        # ------------------------------------------------------------------
        report_progress(85, "正在估算软骨表面积...")
        surface_results = {}

        try:
            from skimage import measure

            for label, name in cartilage_labels.items():
                cartilage_binary = (mask == label).astype(np.uint8)
                if np.sum(cartilage_binary) == 0:
                    surface_results[name] = {"surface_area_mm2": 0.0, "label": label}
                    continue

                # Marching cubes for surface mesh
                verts, faces, _, _ = measure.marching_cubes(
                    cartilage_binary.astype(float),
                    level=0.5,
                    spacing=tuple(float(v) for v in voxel_dims),
                    method='lewiner'
                )

                # Surface area from mesh
                area_mm2 = 0.0
                for face in faces:
                    v0, v1, v2 = verts[face[0]], verts[face[1]], verts[face[2]]
                    area_mm2 += 0.5 * float(np.linalg.norm(np.cross(v1 - v0, v2 - v0)))

                surface_results[name] = {
                    "label": label,
                    "surface_area_mm2": round(area_mm2, 2),
                }
        except ImportError:
            # scikit-image not available — fallback to voxel-based estimate
            for label, name in cartilage_labels.items():
                cartilage_binary = (mask == label).astype(np.uint8)
                if np.sum(cartilage_binary) == 0:
                    surface_results[name] = {"surface_area_mm2": 0.0, "label": label}
                    continue

                # Surface voxel count × average face area
                eroded = ndimage.binary_erosion(cartilage_binary, iterations=1)
                surface_voxels = int(np.sum(cartilage_binary - eroded))
                avg_face_area = np.mean([voxel_dims[0] * voxel_dims[1],
                                         voxel_dims[1] * voxel_dims[2],
                                         voxel_dims[0] * voxel_dims[2]])
                area_mm2 = surface_voxels * avg_face_area
                surface_results[name] = {
                    "label": label,
                    "surface_area_mm2": round(area_mm2, 2),
                    "method": "voxel_estimate",
                }

        report_progress(92, "表面积估算完成")

        # ------------------------------------------------------------------
        # Step 5: Thickness statistics per ROI
        # ------------------------------------------------------------------
        report_progress(94, "正在汇总厚度统计...")
        thickness_stats = {}

        for label, name in cartilage_labels.items():
            roi_thickness = thickness_map[mask == label]
            if len(roi_thickness) == 0:
                thickness_stats[name] = {"mean_mm": 0, "max_mm": 0, "std_mm": 0}
                continue

            thickness_stats[name] = {
                "label": label,
                "mean_mm": round(float(np.mean(roi_thickness)), 3),
                "std_mm": round(float(np.std(roi_thickness)), 3),
                "max_mm": round(float(np.max(roi_thickness)), 3),
                "median_mm": round(float(np.median(roi_thickness)), 3),
                "percentile_95_mm": round(float(np.percentile(roi_thickness, 95)), 3),
            }

        # ------------------------------------------------------------------
        # Step 6: Save results
        # ------------------------------------------------------------------
        report_progress(96, "正在保存结果...")

        # Save thickness map
        thickness_path = os.path.join(args.output_dir, "thickness_map.nii.gz")
        thickness_nii = nib.Nifti1Image(thickness_map, affine)
        nib.save(thickness_nii, thickness_path)

        # Assemble report
        report = {
            "voxel_dimensions_mm": [round(float(v), 4) for v in voxel_dims],
            "voxel_volume_mm3": round(voxel_volume_mm3, 4),
            "volume_analysis": volume_results,
            "surface_area_analysis": surface_results,
            "thickness_analysis": thickness_stats,
            "cartilage_total_volume_ml": round(
                sum(v["volume_ml"] for v in volume_results.values()
                    if v["label"] in cartilage_labels), 4
            ),
        }

        report_path = os.path.join(args.output_dir, "quantification_report.json")
        with open(report_path, 'w', encoding='utf-8') as f:
            json.dump(report, f, indent=2, ensure_ascii=False)

        report_progress(100, "量化分析完成")
        print(f"Thickness map: {thickness_path}")
        print(f"Report: {report_path}")

    except Exception as e:
        report_progress(-1, f"量化分析失败: {e}")
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    main()
