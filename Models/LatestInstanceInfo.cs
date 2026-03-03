using System;

namespace HyPrism.Services;

/// <summary>
/// Stores information about the latest installed game instance version.
/// </summary>
public sealed class LatestInstanceInfo
{
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}
