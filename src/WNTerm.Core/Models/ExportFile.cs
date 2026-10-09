using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WNTerm.Models;

public class ExportCipherBlock
{
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = "";

    [JsonPropertyName("cipherText")]
    public string CipherText { get; set; } = "";

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";
}

public class ExportProtection
{
    [JsonPropertyName("kdf")]
    public string Kdf { get; set; } = "PBKDF2-SHA256";

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = 600000;

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = "";

    [JsonPropertyName("check")]
    public ExportCipherBlock Check { get; set; } = new();
}

public class ExportSessionItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 22;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("keyFileName")]
    public string? KeyFileName { get; set; }

    [JsonPropertyName("secrets")]
    public ExportCipherBlock? Secrets { get; set; }
}

public class ExportSecretPayload
{
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; set; }

    [JsonPropertyName("keyFileContent")]
    public string? KeyFileContent { get; set; } // Base64
}

public class ExportFile
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "wnterm-sessions";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("protection")]
    public ExportProtection? Protection { get; set; }

    [JsonPropertyName("sessions")]
    public List<ExportSessionItem> Sessions { get; set; } = new();
}
