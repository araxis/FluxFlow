namespace FluxFlow.Conversations.Dictation;

public sealed record DictationSettings(bool Enabled = false, string Location = "Local", string Model = "", string Language = "auto")
{
    public void Validate()
    {
        if (Location is not ("Local" or "Remote")) throw new ArgumentException("Choose Local or Remote dictation.");
        if (Language is not ("auto" or "fa" or "en")) throw new ArgumentException("Choose Auto, Persian or English dictation.");
        if (Model is null || Model.Length > 200 || Model.Any(char.IsControl))
            throw new ArgumentException("Enter a valid transcription model identifier.");
    }
}
