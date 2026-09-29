using System;

namespace WNTerm.Models;

public class KnownHost
{
    public string HostKey { get; set; } = ""; // host:port
    public string Algorithm { get; set; } = "";
    public string FingerprintSha256 { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
