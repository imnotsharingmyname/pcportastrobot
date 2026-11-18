using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AstroHybrid.App.Avalonia.Services;

public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

public sealed record UpdateInfo(string Version, string Notes, UpdateAsset Asset);

/// <summary>
/// Mirrors AstroHybrid.App/Services/UpdateService.cs: read the repository's
/// releases, take the newest non-prerelease that carries a launcher package,
/// download it, verify the digest when GitHub publishes one, back up the current
/// binary and replace it.
///
/// The Windows launcher accepts any AstroHybrid .zip. On Linux the package must
/// also name Linux, because applying a Windows build here would replace a working
/// launcher with an executable that cannot run.
/// </summary>
public sealed class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/imnotsharingmyname/pcportastrobot/releases";
    private const long MaxAssetBytes = 300L * 1024 * 1024;

    /// <summary>Version compiled into this launcher; compared with the release tag.</summary>
    public const string LauncherVersion = "1.1.0";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AstroHybrid-Linux");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<Asset> Assets { get; set; } = new();
    }

    private sealed class Asset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }

    /// <summary>Newest applicable release, or null when this launcher is current.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        var releases = await Http.GetFromJsonAsync<List<Release>>(ReleasesApi);
        if (releases is null)
        {
            return null;
        }

        foreach (var release in releases)
        {
            if (release.Prerelease)
            {
                continue;
            }

            var asset = FindLinuxAsset(release);
            if (asset is null)
            {
                continue;
            }

            var version = release.TagName.TrimStart('v', 'V');
            if (!IsNewer(version, LauncherVersion))
            {
                return null;
            }

            var sha = asset.Digest is { Length: > 7 } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest[7..]
                : null;

            return new UpdateInfo(
                release.TagName,
                release.Body ?? "",
                new UpdateAsset(asset.Name, asset.Url, asset.Size, sha));
        }

        return null;
    }

    /// <summary>
    /// The Windows launcher accepts "astrohybrid*.zip". Linux also requires the
    /// package name to say Linux, in .tar.gz or .zip form.
    /// </summary>
    private static Asset? FindLinuxAsset(Release release)
    {
        foreach (var asset in release.Assets)
        {
            var name = asset.Name.ToLowerInvariant();
            if (!name.Contains("astrohybrid") || !name.Contains("linux"))
            {
                continue;
            }

            if (!(name.EndsWith(".zip", StringComparison.Ordinal) ||
                  name.EndsWith(".tar.gz", StringComparison.Ordinal) ||
                  name.EndsWith(".tgz", StringComparison.Ordinal)))
            {
                continue;
            }

            if (asset.Size > 0 && asset.Size < MaxAssetBytes)
            {
                return asset;
            }
        }

        return null;
    }

    private static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string value) => value
            .Split('-', '+')[0]
            .Split('.')
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .ToArray();

        var left = Parts(candidate);
        var right = Parts(current);
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var a = index < left.Length ? left[index] : 0;
            var b = index < right.Length ? right[index] : 0;
            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }

    public async Task<bool> DownloadAndApplyAsync(UpdateInfo info, Action<double, string>? progress = null)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "astrohybrid-update");
        Directory.CreateDirectory(temporary);
        var package = Path.Combine(temporary, info.Asset.Name);

        try
        {
            progress?.Invoke(0, "Starting download…");
            using (var response = await Http.GetAsync(info.Asset.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.Asset.Size;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(package);

                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count));
                    read += count;
                    if (total > 0)
                    {
                        progress?.Invoke((double)read / total, $"Downloading… {read * 100 / total}%");
                    }
                }
            }

            if (info.Asset.Sha256 is { Length: 64 } expected)
            {
                progress?.Invoke(1, "Verifying…");
                await using var stream = File.OpenRead(package);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Invoke(0, "Download failed: checksum mismatch");
                    File.Delete(package);
                    return false;
                }
            }

            var extracted = Path.Combine(temporary, "extract");
            if (Directory.Exists(extracted))
            {
                Directory.Delete(extracted, true);
            }

            Directory.CreateDirectory(extracted);
            progress?.Invoke(1, "Extracting…");
            if (info.Asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(package, extracted);
            }
            else
            {
                var tar = Process.Start(new ProcessStartInfo("tar")
                {
                    ArgumentList = { "-xzf", package, "-C", extracted },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (tar is null)
                {
                    progress?.Invoke(0, "Extracting failed: tar is missing");
                    return false;
                }

                await tar.WaitForExitAsync();
                if (tar.ExitCode != 0)
                {
                    progress?.Invoke(0, "Extracting failed");
                    return false;
                }
            }

            var launcher = Directory.GetFiles(extracted, "AstroHybrid", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (launcher is null)
            {
                progress?.Invoke(0, "The package has no AstroHybrid executable");
                return false;
            }

            var current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current))
            {
                progress?.Invoke(0, "Cannot locate the running launcher");
                return false;
            }

            progress?.Invoke(1, "Installing…");
            var backup = current + ".bak";
            File.Copy(current, backup, true);
            try
            {
                File.Copy(launcher, current, true);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(current,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
            catch
            {
                File.Copy(backup, current, true);
                progress?.Invoke(0, "Installing failed; the previous launcher was restored");
                return false;
            }

            progress?.Invoke(1, "Update installed. Restart the launcher.");
            try
            {
                File.Delete(package);
                Directory.Delete(extracted, true);
            }
            catch
            {
                // Cleanup is best effort.
            }

            return true;
        }
        catch (Exception exception)
        {
            progress?.Invoke(0, $"Update failed: {exception.Message}");
            return false;
        }
    }
}
