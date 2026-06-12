using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DicomViewer;

/// <summary>
/// HTTP client for communicating with the remote CartiMorph segmentation server.
/// </summary>
internal sealed class RemoteSegmentationClient : IDisposable
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Status of a remote segmentation task as returned by the server.
    /// </summary>
    public enum TaskStatus
    {
        Unknown,
        Pending,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// Result returned by GetStatusAsync.
    /// </summary>
    public sealed class TaskStatusResult
    {
        public required TaskStatus Status { get; init; }
        public required int ProgressPercent { get; init; }
        public required string ProgressMessage { get; init; }
        /// <summary>Segmentation info JSON string (only available when Completed).</summary>
        public string? InfoJson { get; init; }
        /// <summary>Error message (only available when Failed).</summary>
        public string? Error { get; init; }
    }

    public RemoteSegmentationClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan // Upload/download can take minutes
        };
    }

    /// <summary>
    /// Uploads a NIfTI file to the server to start a segmentation task.
    /// </summary>
    /// <returns>The task ID assigned by the server.</returns>
    public async Task<string> UploadAsync(
        string niftiPath,
        string apiKey,
        string serverUrl,
        CancellationToken ct = default)
    {
        string url = $"{serverUrl.TrimEnd('/')}/api/v1/segment";

        using var fileStream = File.OpenRead(niftiPath);
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/gzip");
        content.Add(fileContent, "file", Path.GetFileName(niftiPath));

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthHeader(request, apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessOrThrowAsync(response);

        string body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<UploadResponse>(body);
        if (result?.TaskId is null)
        {
            throw new InvalidOperationException($"Server returned unexpected response: {body}");
        }

        return result.TaskId;
    }

    /// <summary>
    /// Polls the server for the current status of a segmentation task.
    /// </summary>
    public async Task<TaskStatusResult> GetStatusAsync(
        string taskId,
        string apiKey,
        string serverUrl,
        CancellationToken ct = default)
    {
        string url = $"{serverUrl.TrimEnd('/')}/api/v1/segment/{taskId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request, apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessOrThrowAsync(response);

        string body = await response.Content.ReadAsStringAsync(ct);
        var status = JsonSerializer.Deserialize<StatusResponse>(body);
        if (status is null)
        {
            throw new InvalidOperationException($"Server returned unexpected response: {body}");
        }

        return new TaskStatusResult
        {
            Status = ParseStatus(status.Status),
            ProgressPercent = status.ProgressPercent,
            ProgressMessage = status.ProgressMessage ?? string.Empty,
            InfoJson = status.Info is not null ? JsonSerializer.Serialize(status.Info) : null,
            Error = status.Error
        };
    }

    /// <summary>
    /// Downloads the segmentation result mask from the server.
    /// </summary>
    /// <returns>Path to the downloaded file.</returns>
    public async Task<string> DownloadResultAsync(
        string taskId,
        string apiKey,
        string serverUrl,
        string outputPath,
        CancellationToken ct = default)
    {
        string url = $"{serverUrl.TrimEnd('/')}/api/v1/segment/{taskId}/download";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request, apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessOrThrowAsync(response);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(outputPath);
        await stream.CopyToAsync(fileStream, ct);

        return outputPath;
    }

    /// <summary>
    /// Tests connectivity to the server by calling the health endpoint.
    /// </summary>
    /// <returns>Health check response body.</returns>
    public async Task<string> TestConnectionAsync(string serverUrl, CancellationToken ct = default)
    {
        string url = $"{serverUrl.TrimEnd('/')}/api/v1/health";

        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static void AddAuthHeader(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("X-API-Key", apiKey);
        }
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();
        string detail = body;
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(body);
            if (error?.Detail is not null)
            {
                detail = error.Detail;
            }
        }
        catch
        {
            // Use raw body as detail
        }

        throw new HttpRequestException(
            $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}",
            null,
            response.StatusCode);
    }

    private static TaskStatus ParseStatus(string? status)
    {
        return status switch
        {
            "pending" => TaskStatus.Pending,
            "running" => TaskStatus.Running,
            "completed" => TaskStatus.Completed,
            "failed" => TaskStatus.Failed,
            _ => TaskStatus.Unknown
        };
    }

    // JSON deserialization types

    private sealed class UploadResponse
    {
        [JsonPropertyName("task_id")]
        public string? TaskId { get; set; }
    }

    private sealed class StatusResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("progress_percent")]
        public int ProgressPercent { get; set; }

        [JsonPropertyName("progress_message")]
        public string? ProgressMessage { get; set; }

        [JsonPropertyName("info")]
        public JsonElement? Info { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
