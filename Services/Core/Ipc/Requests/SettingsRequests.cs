using System.Collections.Generic;
using System.Text.Json;

namespace HyPrism.Services.Core.Ipc.Requests;

public record TestMirrorSpeedRequest(string MirrorId, bool? ForceRefresh = null);
public record TestOfficialSpeedRequest(bool? ForceRefresh = null);
public record AddMirrorRequest(string Url, string? Headers = null);
public record MirrorIdRequest(string MirrorId);
public record ToggleMirrorRequest(string MirrorId, bool Enabled);
public record SetInstanceDirRequest(string Path);
public record PingAuthServerRequest(string? AuthDomain = null);

/// <summary>Settings update — arbitrary key/value pairs from the frontend.</summary>
public record UpdateSettingsRequest(Dictionary<string, JsonElement> Updates);
