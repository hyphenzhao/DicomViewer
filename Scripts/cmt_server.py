#!/usr/bin/env python3
"""
cmt_server.py — REST API server for remote CartiMorph-nnUNet segmentation.

Usage:
    export CMT_MODEL_DIR=/path/to/Models/segModel-OAIZIB-19Mar2024
    export CMT_API_KEY=your-secret-key
    export CMT_PORT=8000       # optional, default 8000
    uvicorn cmt_server:app --host 0.0.0.0 --port 8000

Endpoints:
    GET  /api/v1/health                         — Health check (no auth)
    POST /api/v1/segment                        — Upload NIfTI, start segmentation
    GET  /api/v1/segment/{task_id}              — Query task status / progress
    GET  /api/v1/segment/{task_id}/download     — Download result mask .nii.gz
"""

import hashlib
import json
import os
import sys
import threading
import time
import traceback
import uuid
from pathlib import Path
from typing import Optional

# ---------------------------------------------------------------------------
# Monkey-patch: PyTorch >= 2.6 defaults to weights_only=True for torch.load.
# ---------------------------------------------------------------------------
import torch as _torch

_original_load = _torch.load


def _patched_load(*args, **kwargs):
    kwargs.setdefault("weights_only", False)
    return _original_load(*args, **kwargs)


_torch.load = _patched_load
# ---------------------------------------------------------------------------

from contextlib import asynccontextmanager

from fastapi import FastAPI, File, Header, HTTPException, UploadFile
from fastapi.responses import FileResponse, JSONResponse

# Import the shared segmentation function from the CLI script
from cmt_segmentation import run_segmentation

# ---------------------------------------------------------------------------
# Configuration from environment variables
# ---------------------------------------------------------------------------
MODEL_DIR = os.environ.get("CMT_MODEL_DIR", "")
API_KEY = os.environ.get("CMT_API_KEY", "")
PORT = int(os.environ.get("CMT_PORT", "8000"))
TEMP_ROOT = os.environ.get("CMT_TEMP_DIR", os.path.join(os.path.dirname(__file__), "_cmt_server_temp"))
TASK_TTL_SECONDS = int(os.environ.get("CMT_TASK_TTL", "3600"))  # 1 hour

if not MODEL_DIR:
    print("WARNING: CMT_MODEL_DIR environment variable is not set. Server will fail on segmentation requests.",
          file=sys.stderr)

if not API_KEY:
    print("WARNING: CMT_API_KEY environment variable is not set. API key authentication is disabled.",
          file=sys.stderr)

# ---------------------------------------------------------------------------
# In-memory task store
# ---------------------------------------------------------------------------
_tasks: dict[str, dict] = {}
_tasks_lock = threading.Lock()

# ---------------------------------------------------------------------------
# Application lifespan — periodic cleanup of expired tasks
# ---------------------------------------------------------------------------


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Start background cleanup thread on startup, cancel on shutdown."""
    stop_event = threading.Event()

    def _cleanup_loop():
        while not stop_event.wait(timeout=300):  # every 5 minutes
            _cleanup_expired_tasks()

    cleanup_thread = threading.Thread(target=_cleanup_loop, daemon=True)
    cleanup_thread.start()
    yield
    stop_event.set()
    cleanup_thread.join(timeout=5)


app = FastAPI(title="CartiMorph Segmentation Server", version="1.0.0", lifespan=lifespan)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _verify_api_key(x_api_key: Optional[str]) -> None:
    """Raise 401 if API key is configured and doesn't match."""
    if not API_KEY:
        return  # No key configured — allow all
    if not x_api_key:
        raise HTTPException(status_code=401, detail="Missing X-API-Key header")
    # Constant-time comparison to avoid timing attacks
    if not hashlib.sha256(x_api_key.encode()).hexdigest() == hashlib.sha256(API_KEY.encode()).hexdigest():
        raise HTTPException(status_code=401, detail="Invalid API key")


def _set_task(task_id: str, **kwargs):
    with _tasks_lock:
        task = _tasks.get(task_id, {})
        task.update(kwargs)
        task.setdefault("created_at", time.time())
        _tasks[task_id] = task


def _get_task(task_id: str) -> Optional[dict]:
    with _tasks_lock:
        return _tasks.get(task_id)


def _cleanup_expired_tasks():
    """Remove tasks older than TASK_TTL_SECONDS."""
    now = time.time()
    with _tasks_lock:
        expired = [
            tid for tid, t in _tasks.items()
            if now - t.get("created_at", now) > TASK_TTL_SECONDS
        ]
        for tid in expired:
            # Clean up task temp directory
            task_dir = _tasks[tid].get("task_dir", "")
            if task_dir and os.path.isdir(task_dir):
                try:
                    import shutil
                    shutil.rmtree(task_dir, ignore_errors=True)
                except Exception:
                    pass
            del _tasks[tid]


def _run_segmentation_task(task_id: str, input_path: str, output_dir: str):
    """Run segmentation in a background thread, updating the task store."""
    try:
        _set_task(task_id, status="running", progress_percent=0, progress_message="Starting...")

        def progress_callback(percent: int, message: str):
            _set_task(
                task_id,
                progress_percent=percent,
                progress_message=message,
            )

        mask_path, info = run_segmentation(
            input_path=input_path,
            model_dir=MODEL_DIR,
            output_dir=output_dir,
            progress_callback=progress_callback,
        )

        _set_task(
            task_id,
            status="completed",
            progress_percent=100,
            progress_message="Segmentation complete",
            mask_path=mask_path,
            info=info,
        )
    except Exception as e:
        _set_task(
            task_id,
            status="failed",
            progress_percent=-1,
            progress_message=f"Segmentation failed: {e}",
            error=str(e),
            error_traceback=traceback.format_exc(),
        )


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------


@app.get("/api/v1/health")
async def health_check():
    """Health check — returns server status and model availability."""
    model_loaded = bool(MODEL_DIR) and os.path.isdir(MODEL_DIR)
    plans_ok = model_loaded and os.path.isfile(os.path.join(MODEL_DIR, "plans.pkl"))
    fold0_ok = model_loaded and os.path.isfile(os.path.join(MODEL_DIR, "fold_0", "model_best.model"))

    return {
        "status": "ok",
        "version": "1.0.0",
        "model_loaded": model_loaded and plans_ok and fold0_ok,
        "model_dir": MODEL_DIR if model_loaded else None,
        "active_tasks": sum(1 for t in _tasks.values() if t.get("status") in ("pending", "running")),
    }


@app.post("/api/v1/segment")
async def start_segmentation(
    file: UploadFile = File(...),
    x_api_key: Optional[str] = Header(None, alias="X-API-Key"),
):
    """
    Upload a NIfTI file (.nii.gz) and start segmentation.

    Returns:
        {"task_id": "<uuid>"}

    The client should poll GET /api/v1/segment/{task_id} for progress,
    then GET /api/v1/segment/{task_id}/download for the result mask.
    """
    _verify_api_key(x_api_key)

    if not MODEL_DIR:
        raise HTTPException(status_code=503, detail="Server not configured: CMT_MODEL_DIR not set")

    # Create task directory
    task_id = uuid.uuid4().hex[:12]
    task_dir = os.path.join(TEMP_ROOT, task_id)
    os.makedirs(task_dir, exist_ok=True)

    # Save uploaded file
    input_path = os.path.join(task_dir, "input.nii.gz")
    try:
        content = await file.read()
        with open(input_path, "wb") as f:
            f.write(content)
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Failed to save uploaded file: {e}")

    if len(content) == 0:
        raise HTTPException(status_code=400, detail="Empty file uploaded")

    # Validate that it looks like a NIfTI file (gzip magic or NIfTI magic)
    if content[:2] != b'\x1f\x8b' and content[:4] != b'\x00\x00\x00\x00':
        # Not gzip, check for .nii magic (first 4 bytes: sizeof_hdr = 348)
        import struct
        if len(content) >= 4:
            hdr_size = struct.unpack('<i', content[:4])[0]
            if hdr_size != 348:
                raise HTTPException(status_code=400, detail="File does not appear to be a valid NIfTI file")

    # Register task
    _set_task(
        task_id,
        status="pending",
        progress_percent=0,
        progress_message="Task queued",
        task_dir=task_dir,
        input_path=input_path,
    )

    # Start segmentation in background thread
    thread = threading.Thread(
        target=_run_segmentation_task,
        args=(task_id, input_path, task_dir),
        daemon=True,
    )
    thread.start()

    return {"task_id": task_id}


@app.get("/api/v1/segment/{task_id}")
async def get_segmentation_status(
    task_id: str,
    x_api_key: Optional[str] = Header(None, alias="X-API-Key"),
):
    """
    Query segmentation task status.

    Returns:
        {
            "task_id": "...",
            "status": "pending" | "running" | "completed" | "failed",
            "progress_percent": 0-100,
            "progress_message": "...",
            "info": {...}          // only when completed
            "error": "..."         // only when failed
        }
    """
    _verify_api_key(x_api_key)

    task = _get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail=f"Task not found: {task_id}")

    response = {
        "task_id": task_id,
        "status": task.get("status", "unknown"),
        "progress_percent": task.get("progress_percent", 0),
        "progress_message": task.get("progress_message", ""),
    }

    if task.get("status") == "completed":
        response["info"] = task.get("info")

    if task.get("status") == "failed":
        response["error"] = task.get("error", "Unknown error")

    return response


@app.get("/api/v1/segment/{task_id}/download")
async def download_segmentation_result(
    task_id: str,
    x_api_key: Optional[str] = Header(None, alias="X-API-Key"),
):
    """
    Download the segmentation mask .nii.gz file.

    Returns the mask file as a downloadable attachment.
    """
    _verify_api_key(x_api_key)

    task = _get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail=f"Task not found: {task_id}")

    if task.get("status") != "completed":
        raise HTTPException(
            status_code=409,
            detail=f"Task not completed yet. Current status: {task.get('status')}",
        )

    mask_path = task.get("mask_path")
    if not mask_path or not os.path.isfile(mask_path):
        raise HTTPException(status_code=500, detail="Result file not found on server")

    return FileResponse(
        path=mask_path,
        media_type="application/gzip",
        filename="segmentation_mask.nii.gz",
    )


# ---------------------------------------------------------------------------
# Main — for running directly: python cmt_server.py
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    import uvicorn

    print(f"Model directory: {MODEL_DIR or '(NOT SET — will fail on segmentation)'}")
    print(f"API Key: {'configured' if API_KEY else '(NOT SET — auth disabled)'}")
    print(f"Starting server on 0.0.0.0:{PORT}")
    uvicorn.run(app, host="0.0.0.0", port=PORT)
