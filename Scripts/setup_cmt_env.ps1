# setup_cmt_env.ps1
# CartiMorph inference environment setup for Windows
# Run in PowerShell as Administrator

param(
    [switch]$GPU = $false
)

Write-Host "=== CartiMorph 推理环境配置 ===" -ForegroundColor Cyan
Write-Host ""

# Check for conda
$conda = Get-Command conda -ErrorAction SilentlyContinue
if (-not $conda) {
    $condaPaths = @(
        "$env:USERPROFILE\miniconda3\Scripts\conda.exe",
        "$env:USERPROFILE\anaconda3\Scripts\conda.exe",
        "C:\ProgramData\miniconda3\Scripts\conda.exe",
        "C:\ProgramData\anaconda3\Scripts\conda.exe"
    )
    foreach ($p in $condaPaths) {
        if (Test-Path $p) {
            $conda = $p
            break
        }
    }
}

if (-not $conda) {
    Write-Host "未找到 Conda。请先安装 Miniconda: https://docs.conda.io/en/latest/miniconda.html" -ForegroundColor Red
    exit 1
}

Write-Host "找到 Conda: $conda" -ForegroundColor Green

# Create environment
$envName = "cmt-inference"
Write-Host "创建 Conda 环境: $envName ..." -ForegroundColor Yellow
conda create -n $envName python=3.10 -y
conda activate $envName

# Install PyTorch
if ($GPU) {
    Write-Host "安装 PyTorch (CUDA 12.1)..." -ForegroundColor Yellow
    pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121
}
else {
    Write-Host "安装 PyTorch (CPU)..." -ForegroundColor Yellow
    pip install torch torchvision torchaudio
}

# Install TensorFlow
if ($GPU) {
    Write-Host "安装 TensorFlow (GPU)..." -ForegroundColor Yellow
    pip install tensorflow==2.12.0
}
else {
    Write-Host "安装 TensorFlow (CPU)..." -ForegroundColor Yellow
    pip install tensorflow-cpu==2.12.0
}

# Install CartiMorph packages
Write-Host "安装 CartiMorph 包..." -ForegroundColor Yellow
pip install CartiMorph-nnUNet
pip install CartiMorph-vxm

# Install quantification dependencies
Write-Host "安装量化分析依赖..." -ForegroundColor Yellow
pip install SimpleITK nibabel scipy numpy scikit-image

Write-Host ""
Write-Host "=== 环境配置完成 ===" -ForegroundColor Green
Write-Host "激活环境: conda activate $envName" -ForegroundColor Cyan
Write-Host "Python 路径: $(Join-Path (Split-Path (Split-Path $conda)) 'envs' $envName 'python.exe')" -ForegroundColor Cyan
