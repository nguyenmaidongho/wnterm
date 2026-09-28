using System;
using System.Collections.Generic;
using System.IO;
using SNTerm.Models;

namespace SNTerm.Services;

public static class MobaXtermImporter
{
    public static bool IsMobaXtermFile(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.Contains("[Bookmarks", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("#109#", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("#106#", StringComparison.OrdinalIgnoreCase);
    }

    public static List<ExportSessionItem> Parse(string content)
    {
        var result = new List<ExportSessionItem>();
        if (string.IsNullOrWhiteSpace(content)) return result;

        string currentGroup = "Chưa phân nhóm";

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Section header
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentGroup = "Chưa phân nhóm";
                continue;
            }

            // SubRep defines the group/folder in MobaXterm
            if (line.StartsWith("SubRep=", StringComparison.OrdinalIgnoreCase))
            {
                string subRepVal = line.Substring("SubRep=".Length).Trim();
                currentGroup = string.IsNullOrWhiteSpace(subRepVal) ? "Chưa phân nhóm" : subRepVal.Replace('\\', '/');
                continue;
            }

            if (line.StartsWith("ImgNum=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            string key = line.Substring(0, eqIdx).Trim();
            string val = line.Substring(eqIdx + 1).Trim();

            // Check for SSH (#109#) or SFTP (#106#) session
            int tagIdx = val.IndexOf("#109#", StringComparison.OrdinalIgnoreCase);
            int tagLen = 5;
            if (tagIdx < 0)
            {
                tagIdx = val.IndexOf("#106#", StringComparison.OrdinalIgnoreCase);
                tagLen = 5;
            }

            if (tagIdx < 0) continue;

            string sessionData = val.Substring(tagIdx + tagLen);
            var parts = sessionData.Split('%');
            if (parts.Length < 4) continue;

            // Parts layout:
            // parts[0]: Subtype (e.g. "0")
            // parts[1]: Host
            // parts[2]: Port
            // parts[3]: Username
            string host = parts[1].Trim();
            if (string.IsNullOrEmpty(host)) continue;

            int port = 22;
            if (int.TryParse(parts[2].Trim(), out int p) && p > 0 && p <= 65535)
            {
                port = p;
            }

            string username = parts[3].Trim();
            if (string.IsNullOrEmpty(username) || username.Equals("<default>", StringComparison.OrdinalIgnoreCase))
            {
                username = "root";
            }

            // Search for key file in remaining parts
            string? keyFilePath = null;
            for (int i = 4; i < parts.Length; i++)
            {
                string part = parts[i];
                int tickIdx = part.IndexOf((char)96);
                if (tickIdx >= 0) part = part.Substring(0, tickIdx);
                part = part.Trim();

                if (string.IsNullOrEmpty(part)) continue;

                // Check if looks like a private key path
                if (part.Contains('\\') || part.Contains('/') ||
                    part.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith("id_rsa", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith("id_ed25519", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(part))
                {
                    keyFilePath = part.Trim('"', '\'');
                    break;
                }
            }

            string name = string.IsNullOrWhiteSpace(key) ? $"{username}@{host}" : key;

            result.Add(new ExportSessionItem
            {
                Id = Guid.NewGuid(),
                Name = name,
                Group = currentGroup,
                Host = host,
                Port = port,
                Username = username,
                KeyFileName = keyFilePath,
                Secrets = null
            });
        }

        return result;
    }
}