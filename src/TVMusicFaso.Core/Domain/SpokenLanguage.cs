namespace TVMusicFaso.Core.Domain;

public sealed class SpokenLanguage : IEquatable<SpokenLanguage>
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public ClipLanguage EnumValue { get; set; } = ClipLanguage.Autre;

    public bool IsSeeded { get; set; }

    public bool Equals(SpokenLanguage? other) =>
        other is not null && string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as SpokenLanguage);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Code);

    public override string ToString() => Label;
}
