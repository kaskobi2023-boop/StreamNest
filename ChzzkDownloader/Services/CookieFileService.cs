using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed class CookieFileService
{
    private readonly string _temporaryDirectory;

    public CookieFileService(string? temporaryDirectory = null)
    {
        _temporaryDirectory = temporaryDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChzzkLocalDownloader",
            "Temp");

        try
        {
            EnsureSecureTemporaryDirectory();
            CleanupOrphanedFiles();
        }
        catch
        {
            // 공개 영상에는 쿠키가 필요하지 않으므로 앱 시작을 막지 않는다.
            // 로그인 작업에서는 CreateAsync가 다시 검사하고 안전하게 실패한다.
        }
    }

    public async Task<string?> CreateAsync(IReadOnlyCollection<BrowserCookie>? cookies)
    {
        if (cookies is null || cookies.Count == 0)
            return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exportableCookies = cookies
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                        !string.IsNullOrWhiteSpace(c.Value) &&
                        c.IsSecure &&
                        LoginSecurityPolicy.IsAllowedCookieDomain(c.Domain) &&
                        (c.Expires <= 0 || c.Expires > now))
            .DistinctBy(c => $"{c.Name}\n{c.Domain}\n{c.Path}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exportableCookies.Count == 0)
            return null;

        EnsureSecureTemporaryDirectory();
        CleanupOrphanedFiles();
        var path = Path.Combine(
            _temporaryDirectory,
            $"cookies-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        var builder = new StringBuilder("# Netscape HTTP Cookie File\n# This file is temporary and is deleted after the operation.\n\n");

        foreach (var cookie in exportableCookies)
        {
            var domain = Sanitize(cookie.Domain);
            var httpOnlyDomain = cookie.IsHttpOnly ? $"#HttpOnly_{domain}" : domain;
            var includeSubdomains = domain.StartsWith('.') ? "TRUE" : "FALSE";
            var pathValue = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : Sanitize(cookie.Path);
            var expires = cookie.Expires > 0
                ? Math.Floor(cookie.Expires).ToString(CultureInfo.InvariantCulture)
                : "0";
            builder.Append(httpOnlyDomain).Append('\t')
                .Append(includeSubdomains).Append('\t')
                .Append(pathValue).Append('\t')
                .Append(cookie.IsSecure ? "TRUE" : "FALSE").Append('\t')
                .Append(expires).Append('\t')
                .Append(Sanitize(cookie.Name)).Append('\t')
                .Append(Sanitize(cookie.Value)).Append('\n');
        }

        try
        {
            await using (var stream = new FileStream(
                             path,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var bytes = new UTF8Encoding(false).GetBytes(builder.ToString());
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }

            RestrictFileToCurrentUser(path);
            return path;
        }
        catch
        {
            Delete(path);
            throw;
        }
    }

    public void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 종료 시 운영체제가 정리할 수 있도록 민감 정보는 로그에 남기지 않는다.
        }
    }

    private void EnsureSecureTemporaryDirectory()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        if (!OperatingSystem.IsWindows())
            return;

        var currentUser = WindowsIdentity.GetCurrent().User ??
                          throw new InvalidOperationException("현재 Windows 사용자 SID를 확인하지 못했습니다.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(_temporaryDirectory), security);
    }

    private static void RestrictFileToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var currentUser = WindowsIdentity.GetCurrent().User ??
                          throw new InvalidOperationException("현재 Windows 사용자 SID를 확인하지 못했습니다.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new FileInfo(path), security);
    }

    private void CleanupOrphanedFiles()
    {
        if (!Directory.Exists(_temporaryDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(_temporaryDirectory, "cookies-*.txt", SearchOption.TopDirectoryOnly))
        {
            if (TryReadOwnerProcessId(Path.GetFileName(path), out var processId) && IsProcessRunning(processId))
                continue;

            Delete(path);
        }
    }

    private static bool TryReadOwnerProcessId(string fileName, out int processId)
    {
        processId = 0;
        const string prefix = "cookies-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var separator = fileName.IndexOf('-', prefix.Length);
        return separator > prefix.Length &&
               int.TryParse(fileName.AsSpan(prefix.Length, separator - prefix.Length), out processId);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Sanitize(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
}
