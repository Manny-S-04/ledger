using Application.Providers;

namespace Application.Interfaces;

// Checks that a provider callback was signed with the secret only the ledger and the provider share.
public interface ICallbackSignatureVerifier
{
    CallbackSignatureResult Verify(string? timestamp, string? signature, ReadOnlySpan<byte> body);
}
