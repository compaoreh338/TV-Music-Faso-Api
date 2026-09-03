using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

internal static class ClipValidation
{
    public static void Apply(IClipRepository repository, Guid id, ClipValidationStatus status, string? note)
    {
        var clip = repository.GetById(id)
            ?? throw new InvalidOperationException($"Clip introuvable : {id}");
        switch (status)
        {
            case ClipValidationStatus.Validated:
                clip.Approve();
                break;
            case ClipValidationStatus.Rejected:
                clip.Reject(note ?? string.Empty);
                break;
            default:
                clip.SubmitForValidation();
                break;
        }

        repository.Update(clip);
    }
}
