namespace Samklang.Domain;

/// <summary>
/// The user's choice of which render device switching and Resting Format handling apply to: the
/// Windows default render device (tracking it as it changes) or a specific device pinned by ID,
/// per this issue's acceptance criteria.
/// </summary>
public enum DeviceTargetingMode
{
    FollowDefault,
    Pinned,
}
