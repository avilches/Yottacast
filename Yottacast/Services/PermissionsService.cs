using System;
using System.Collections.Generic;
using System.Linq;

namespace Yottacast.Services;

public enum PermissionId { Accessibility, FullDiskAccess }

public enum PermissionStatus { Granted, Denied, Unknown }

public sealed record PermissionInfo(
    PermissionId Id,
    string Title,
    string Description,
    PermissionStatus Status);

public abstract class PermissionsService {
    /// <summary>True on platforms where Yottacast needs OS-level permissions (currently only macOS).</summary>
    public abstract bool IsSupported { get; }

    /// <summary>Permissions exposed in the Settings window for this platform.</summary>
    public abstract IReadOnlyList<PermissionId> Available { get; }

    /// <summary>Returns the current state of a single permission.</summary>
    public abstract PermissionInfo Check(PermissionId id);

    /// <summary>
    /// Triggers the platform-native flow to grant a permission: a system prompt where
    /// available, or opens the relevant System Settings panel as a fallback.
    /// </summary>
    public abstract void Request(PermissionId id);

    public IReadOnlyList<PermissionInfo> CheckAll() =>
        Available.Select(Check).ToList();
}

internal sealed class NoopPermissionsService : PermissionsService {
    public static readonly NoopPermissionsService Instance = new();
    public override bool IsSupported => false;
    public override IReadOnlyList<PermissionId> Available => Array.Empty<PermissionId>();
    public override PermissionInfo Check(PermissionId id) =>
        throw new NotSupportedException();
    public override void Request(PermissionId id) { }
}
