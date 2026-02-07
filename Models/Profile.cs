using System;

namespace HyPrism.Models;

/// <summary>
/// A user profile with DDID and display name.
/// </summary>
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UUID { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOfficial { get; set; } = false;
    public TimeSpan TotalPlaytime { get; set; } = TimeSpan.Zero;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

