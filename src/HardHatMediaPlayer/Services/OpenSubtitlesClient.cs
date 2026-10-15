using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HardHatMediaPlayer.Services;

public sealed record SubtitleResult(
    long FileId,
    string FileName,
    string Language,
    string Release,
    int Downloads,
    bool HashMatch,
    bool HearingImpaired,
    string? Title);

public sealed class OpenSubtitlesException : Exception
{
    public OpenSubtitlesException(string message) : base(message) { }
}

/// <summary>
/// Minimal client for the OpenSubtitles.com REST API (the service that replaced the opensubtitles.org
/// XML-RPC API the original VLsub used). Needs the user's own API key; logging in raises the download quota.
/// </summary>
public sealed class OpenSubtitlesClient : IDisposable
{
    private const string DefaultHost = "api.opensubtitles.com";
    private const string UserAgent = "Hard Hat Media Player v0.1";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly SubtitleSettings _settings;
    private string _host = DefaultHost;
    private string? _token;
    private string? _tokenUser;
    private DateTime _tokenExpires;

    public OpenSubtitlesClient(SubtitleSettings settings) => _settings = settings;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    /// <summary>Forget the session, e.g. after the account settings changed.</summary>
    public void Reset()
    {
        _token = null;
        _host = DefaultHost;
    }

    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(string? movieHash, string? query, string languages, CancellationToken ct = default)
    {
        if (movieHash is null && string.IsNullOrWhiteSpace(query))
            throw new OpenSubtitlesException("Nothing to search for.");
        await EnsureLoginAsync(ct);

        // The API asks for parameters sorted by name, in lower case; otherwise it answers with a redirect.
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(languages)) parameters["languages"] = NormalizeLanguages(languages);
        if (movieHash != null) parameters["moviehash"] = movieHash;
        if (!string.IsNullOrWhiteSpace(query)) parameters["query"] = query.Trim().ToLowerInvariant();
        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        using var request = Request(HttpMethod.Get, "subtitles?" + queryString);
        using var doc = await SendAsync(request, ct);

        var results = new List<SubtitleResult>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return results;
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out var a)) continue;
            if (!a.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0) continue;
            var file = files[0];
            if (!file.TryGetProperty("file_id", out var id) || !id.TryGetInt64(out var fileId)) continue;

            string? title = null;
            if (a.TryGetProperty("feature_details", out var fd) && fd.ValueKind == JsonValueKind.Object)
                title = Str(fd, "movie_name") ?? Str(fd, "title");

            results.Add(new SubtitleResult(
                fileId,
                Str(file, "file_name") ?? "",
                Str(a, "language") ?? "",
                Str(a, "release") ?? "",
                a.TryGetProperty("download_count", out var dc) && dc.TryGetInt32(out var n) ? n : 0,
                a.TryGetProperty("moviehash_match", out var hm) && hm.ValueKind == JsonValueKind.True,
                a.TryGetProperty("hearing_impaired", out var hi) && hi.ValueKind == JsonValueKind.True,
                title));
        }
        return results;
    }

    /// <summary>Downloads one subtitle file. Returns its contents, suggested file name and the remaining daily quota.</summary>
    public async Task<(byte[] Data, string FileName, int? Remaining)> DownloadAsync(long fileId, CancellationToken ct = default)
    {
        await EnsureLoginAsync(ct);
        using var request = Request(HttpMethod.Post, "download");
        request.Content = new StringContent(JsonSerializer.Serialize(new { file_id = fileId }), Encoding.UTF8, "application/json");
        using var doc = await SendAsync(request, ct);
        var root = doc.RootElement;

        var link = Str(root, "link");
        if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new OpenSubtitlesException(Str(root, "message") ?? "OpenSubtitles didn't return a download link.");
        int? remaining = root.TryGetProperty("remaining", out var r) && r.TryGetInt32(out var rem) ? rem : null;

        var bytes = await _http.GetByteArrayAsync(uri, ct);
        return (bytes, Str(root, "file_name") ?? "subtitle.srt", remaining);
    }

    private async Task EnsureLoginAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new OpenSubtitlesException("Add your OpenSubtitles.com API key first.");
        var user = _settings.Username.Trim();
        var password = Secret.Unprotect(_settings.PasswordProtected);
        if (user.Length == 0 || string.IsNullOrEmpty(password)) return; // anonymous: search works, downloads are limited
        if (_token != null && _tokenUser == user && DateTime.UtcNow < _tokenExpires) return;

        _host = DefaultHost;
        using var request = Request(HttpMethod.Post, "login", withToken: false);
        request.Content = new StringContent(JsonSerializer.Serialize(new { username = user, password }), Encoding.UTF8, "application/json");
        using var doc = await SendAsync(request, ct);
        _token = Str(doc.RootElement, "token") ?? throw new OpenSubtitlesException("OpenSubtitles login failed.");
        _tokenUser = user;
        _tokenExpires = DateTime.UtcNow.AddHours(12);

        // VIP accounts get a different API host. It comes from the server, so only accept OpenSubtitles hosts.
        var baseUrl = Str(doc.RootElement, "base_url");
        if (baseUrl != null)
        {
            var host = baseUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
            if (host.EndsWith(".opensubtitles.com", StringComparison.OrdinalIgnoreCase) && !host.Contains('/')) _host = host;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, bool withToken = true)
    {
        var request = new HttpRequestMessage(method, $"https://{_host}/api/v1/{path}");
        request.Headers.TryAddWithoutValidation("Api-Key", _settings.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (withToken && _token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new OpenSubtitlesException("Couldn't reach OpenSubtitles. Check your internet connection.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OpenSubtitlesException("OpenSubtitles took too long to answer.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(body); } catch (JsonException) { }

            if (response.IsSuccessStatusCode && doc != null) return doc;

            var message = doc != null ? Str(doc.RootElement, "message") ?? FirstError(doc.RootElement) : null;
            doc?.Dispose();
            throw new OpenSubtitlesException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "OpenSubtitles didn't accept the API key or login" + (message is null ? "" : $" (\u201c{message}\u201d)") + ". Check them under Account\u2026",
                HttpStatusCode.NotAcceptable => message ?? "You've reached today's download limit. Logging in raises it.",
                HttpStatusCode.TooManyRequests => "Too many requests; wait a few seconds and try again.",
                _ => message ?? $"OpenSubtitles returned an error ({(int)response.StatusCode}).",
            });
        }
    }

    private static string? FirstError(JsonElement root) =>
        root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0
            ? e[0].ToString()
            : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string NormalizeLanguages(string languages) =>
        string.Join(",", languages.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().ToLowerInvariant()).Distinct().OrderBy(l => l, StringComparer.Ordinal));

    /// <summary>
    /// The OpenSubtitles "moviehash": file size plus the 64-bit little-endian word sums of the first
    /// and last 64 KiB. Returns null for files too small to hash.
    /// </summary>
    public static string? ComputeMovieHash(string path)
    {
        const int chunk = 65536;
        try
        {
            using var stream = File.OpenRead(path);
            var size = stream.Length;
            if (size < chunk * 2) return null;

            var hash = (ulong)size;
            var buffer = new byte[chunk];
            hash = unchecked(hash + SumChunk(stream, buffer));
            stream.Seek(-chunk, SeekOrigin.End);
            hash = unchecked(hash + SumChunk(stream, buffer));
            return hash.ToString("x16");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ulong SumChunk(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        ulong sum = 0;
        for (var i = 0; i + 8 <= read; i += 8) sum = unchecked(sum + BitConverter.ToUInt64(buffer, i));
        return sum;
    }

    public void Dispose() => _http.Dispose();
}
