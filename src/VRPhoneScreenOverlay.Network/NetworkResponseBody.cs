using System.Text.Json;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Bounds response-body waits after ResponseHeadersRead has completed.</summary>
public static class NetworkResponseBody
{
    public const int MaximumJsonBytes = 256 * 1024;
    /// <summary>Reads a small JSON response with one deadline for the entire body.</summary>
    public static async ValueTask<T?> ReadJsonAsync<T>(
        HttpContent content,
        JsonSerializerOptions options,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await using Stream source = await content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaximumJsonBytes)
                { throw new NetworkException(NetworkReasonCodes.ResponseTooLarge, "服务返回的内容过大，请稍后重试"); }
                await buffer.WriteAsync(chunk.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            }
            deadline.Token.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw BodyTimeout(exception);
        }
        catch (JsonException exception)
        {
            throw new NetworkException(NetworkReasonCodes.ResponseInvalid,
                "服务返回的内容无效，请稍后重试", inner: exception);
        }
    }

    /// <summary>
    /// Opens a caller-owned download stream. Each asynchronous read has an idle deadline,
    /// allowing a progressing large package to take longer than one request timeout.
    /// </summary>
    public static async ValueTask<Stream> OpenDownloadAsync(
        HttpContent content,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(idleTimeout);
        try
        {
            Stream source = await content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            return new NetworkDownloadStream(source, idleTimeout);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw BodyTimeout(exception);
        }
    }

    internal static NetworkException BodyTimeout(OperationCanceledException exception) => new(
        NetworkReasonCodes.Timeout, "网络数据等待超时，请稍后重试", inner: exception);
}
