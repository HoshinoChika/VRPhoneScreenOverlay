namespace VRPhoneScreenOverlay.Contracts;

public interface IPhoneClipboardSink
{
    public void Receive(string text);
    public void SetConnection(bool connected);
}

public sealed record PhoneClipboardEntry(Guid Id, string Text, Uri? Url, DateTimeOffset CopiedAt, string Status = "", bool Busy = false);

public interface IVideoLinkResolver
{
    public bool Supports(Uri source) => false;
    public Task<string> ResolveAsync(Uri source, CancellationToken cancellationToken);
}
