using System;
using System.IO;
using System.Net.Sockets;
using Renci.SshNet.Common;

namespace SNTerm.Services;

public static class ErrorTranslator
{
    public static string Translate(Exception ex, string? host = null, int? port = null)
    {
        string hostPort = (host != null && port != null) ? $"{host}:{port}" : "máy chủ";

        if (ex is SshAuthenticationException)
        {
            return "Sai thông tin đăng nhập (user, mật khẩu hoặc SSH key).";
        }

        if (ex.Message.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return "Sai passphrase của key, hoặc key này bắt buộc phải có passphrase.";
        }

        if (ex is SocketException || ex.InnerException is SocketException)
        {
            return $"Không kết nối được tới {hostPort}. Vui lòng kiểm tra địa chỉ IP/port và kết nối mạng.";
        }

        if (ex is SshOperationTimeoutException || ex is TimeoutException)
        {
            return $"Hết thời gian chờ kết nối tới {hostPort}. Máy chủ không phản hồi.";
        }

        if (ex is SshConnectionException)
        {
            return $"Mất kết nối tới {hostPort}.";
        }

        if (ex is FileNotFoundException fnf)
        {
            return $"Không tìm thấy file key: {fnf.FileName}";
        }

        return $"Lỗi kết nối: {ex.Message}";
    }
}
