using System;
using System.IO;
using System.Net.Sockets;
using Renci.SshNet.Common;

namespace SNTerm.Services;

public static class ErrorTranslator
{
    public static string Translate(Exception ex, string? host = null, int? port = null, string? language = null)
    {
        string lang = language ?? LocalizationManager.CurrentLanguage;
        bool isVi = string.Equals(lang, "vi", StringComparison.OrdinalIgnoreCase);
        string hostPort = (host != null && port != null) ? $"{host}:{port}" : (isVi ? "máy chủ" : "server");

        if (ex is SshAuthenticationException)
        {
            return isVi
                ? "Sai thông tin đăng nhập (user, mật khẩu hoặc SSH key)."
                : "Invalid credentials (username, password or SSH key).";
        }

        if (ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return isVi
                ? "Sai passphrase của key, hoặc key này bắt buộc phải có passphrase."
                : "Invalid key passphrase or passphrase is required.";
        }

        if (ex is SocketException || ex.InnerException is SocketException)
        {
            return isVi
                ? $"Không kết nối được tới {hostPort}. Vui lòng kiểm tra địa chỉ IP/port và kết nối mạng."
                : $"Cannot connect to {hostPort}. Please check IP address/port and network.";
        }

        if (ex is SshOperationTimeoutException || ex is TimeoutException)
        {
            return isVi
                ? $"Hết thời gian chờ kết nối tới {hostPort}. Máy chủ không phản hồi."
                : $"Connection timeout to {hostPort}. Server did not respond.";
        }

        if (ex is SshConnectionException)
        {
            return isVi
                ? $"Mất kết nối tới {hostPort}."
                : $"Connection lost to {hostPort}.";
        }

        if (ex is FileNotFoundException fnf)
        {
            return isVi
                ? $"Không tìm thấy file key: {fnf.FileName}"
                : $"Key file not found: {fnf.FileName}";
        }

        return isVi
            ? $"Lỗi kết nối: {ex.Message}"
            : $"Connection error: {ex.Message}";
    }
}