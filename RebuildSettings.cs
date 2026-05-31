namespace DicomViewer;

internal enum VolumePreset
{
    Custom,
    CtBone,
    CtSoftTissue,
    MriBrain
}

internal sealed record class RebuildSettings
{
    public required VolumePreset Preset { get; init; }
    public required float ThresholdRatio { get; init; }
    public required int SmoothingPasses { get; init; }
}
