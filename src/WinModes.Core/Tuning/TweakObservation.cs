namespace WinModes.Core.Tuning;

/// <summary>A read-only observation, separate from the user's requested value and the undo journal.</summary>
public sealed record TweakPartObservation(int Index, bool? Applied, string Actual, string Desired, bool IsAvailable, string? Error = null, bool RegistryValuePresent = false)
{
    public bool CanChange => IsAvailable && Applied is not null && Error is null;
}

/// <summary>Optional richer task discovery; a failed read must not be mistaken for a missing task.</summary>
public interface ITaskObservation
{
    TweakPartObservation Observe(string path, int index);
}
