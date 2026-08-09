namespace MogCoach.References;

/// <summary>Where trusted rotation reference files live.</summary>
public sealed class ReferenceOptions
{
    public const string SectionName = "References";

    /// <summary>
    /// Directory of rotation JSON files. Lookup order for job X, encounter Y:
    /// "{X}.{Y}.json" then "{X}.json" (case-insensitive).
    /// </summary>
    public string RotationsDirectory { get; set; } = "reference-data/rotations";

    /// <summary>JSON file mapping ability id (hex) → AoE shape, for the positional "in the danger zone" test.</summary>
    public string AoeShapesFile { get; set; } = "reference-data/aoe/shapes.json";
}
