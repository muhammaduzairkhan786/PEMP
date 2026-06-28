namespace Pemp.Domain;

/// <summary>
/// Outcome of a guarded transition. A rejected transition carries the reason a
/// guard was not satisfied (surfaced in the UI's "locked" hover, DESIGN_PLAN §8).
/// </summary>
public readonly record struct Result(bool Ok, string? Error)
{
    public static Result Success() => new(true, null);
    public static Result Fail(string error) => new(false, error);

    public bool Failed => !Ok;
    public override string ToString() => Ok ? "Ok" : $"Fail: {Error}";
}
