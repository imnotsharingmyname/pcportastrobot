using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AstroHybrid.App.Models
{
    public class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("releaseName")]
        public string ReleaseName { get; set; } = "";

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = "";

        [JsonPropertyName("publishedAt")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("isPrerelease")]
        public bool IsPrerelease { get; set; }

        public bool IsNewerThan(string currentVersion)
        {
            return CompareVersions(Version, currentVersion) > 0;
        }

        public static int CompareVersions(string v1, string v2)
        {
            var parts1 = v1.Split('.', '-');
            var parts2 = v2.Split('.', '-');

            for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
            {
                int n1 = i < parts1.Length ? ParseVersionPart(parts1[i]) : 0;
                int n2 = i < parts2.Length ? ParseVersionPart(parts2[i]) : 0;

                if (n1 != n2) return n1.CompareTo(n2);
            }
            return 0;
        }

        private static int ParseVersionPart(string part)
        {
            if (int.TryParse(part, out int num)) return num;
            // Handle pre-release labels: alpha < beta < rc < release
            part = part.ToLowerInvariant();
            if (part.Contains("alpha")) return -4;
            if (part.Contains("beta")) return -3;
            if (part.Contains("rc")) return -2;
            return -1;
        }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = "";
    }

    public class UpdateState
    {
        public bool UpdateAvailable { get; set; }
        public UpdateInfo? LatestUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public DateTime LastCheckTime { get; set; }
        public string? IgnoredVersion { get; set; }
        public bool IsChecking { get; set; }
        public string? ErrorMessage { get; set; }
        public double DownloadProgress { get; set; }
        public string DownloadStatus { get; set; } = "";
        public bool IsDownloading { get; set; }
        public bool IsInstalling { get; set; }
    }
}