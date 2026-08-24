namespace app_dev_assignment.Models;

public enum EvidenceType { Observed, Derived, Inferred, Unknown }

public sealed class BoundingBox { public int X { get; init; } public int Y { get; init; } public int Width { get; init; } public int Height { get; init; } }

public sealed class DetectedObject { public string Name { get; init; } = string.Empty; public double Confidence { get; init; } public BoundingBox? Location { get; init; } public EvidenceType EvidenceType { get; init; } = EvidenceType.Observed; public string EvidenceSource { get; init; } = "Azure Computer Vision object detection"; }

public sealed class EvidenceTag { public string Name { get; init; } = string.Empty; public double Confidence { get; init; } public EvidenceType EvidenceType { get; init; } = EvidenceType.Observed; }

public sealed class ColorAnalysis { public string? DominantForegroundColor { get; init; } public string? DominantBackgroundColor { get; init; } public IReadOnlyList<string> AccentColors { get; init; } = Array.Empty<string>(); public bool? IsBlackAndWhite { get; init; } }

public sealed class SceneAnalysis { public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>(); public string Environment { get; init; } = "Unavailable"; public double? Confidence { get; init; } public string EvidenceSource { get; init; } = "Azure Computer Vision categories"; }

public sealed class Interpretation { public string Statement { get; init; } = string.Empty; public IReadOnlyList<string> EvidenceBasis { get; init; } = Array.Empty<string>(); public double? Confidence { get; init; } public EvidenceType ReasoningLevel { get; init; } = EvidenceType.Inferred; }

public sealed class Uncertainty { public string Statement { get; init; } = string.Empty; public string Reason { get; init; } = string.Empty; public IReadOnlyList<string> RelatedEvidence { get; init; } = Array.Empty<string>(); }

public sealed class AnalysisMetadata { public Guid AnalysisId { get; init; } public string ImageHash { get; init; } = string.Empty; public DateTimeOffset CreatedAt { get; init; } public string EngineVersion { get; init; } = "Mystik Vision Analysis Engine 2.0"; public string Provider { get; init; } = "Azure Computer Vision"; public string ProviderApiVersion { get; init; } = "v3.2"; public string AnalysisMode { get; init; } = "Detailed"; public TimeSpan ProcessingDuration { get; init; } }

public sealed class ImageAnalysisResult { public AnalysisMetadata Metadata { get; init; } = new(); public string Overview { get; init; } = string.Empty; public IReadOnlyList<DetectedObject> Objects { get; init; } = Array.Empty<DetectedObject>(); public IReadOnlyList<EvidenceTag> Tags { get; init; } = Array.Empty<EvidenceTag>(); public SceneAnalysis Scene { get; init; } = new(); public ColorAnalysis Colors { get; init; } = new(); public IReadOnlyList<Interpretation> Interpretations { get; init; } = Array.Empty<Interpretation>(); public IReadOnlyList<Uncertainty> Uncertainties { get; init; } = Array.Empty<Uncertainty>(); public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>(); }
