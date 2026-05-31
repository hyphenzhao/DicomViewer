namespace DicomViewer;

internal readonly record struct LoadProgress(
    int TotalPercent,
    int CurrentFilePercent,
    string TotalMessage,
    string CurrentFileMessage,
    bool ShowCurrentFileProgress = true);
