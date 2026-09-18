using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// TTS facade: picks the configured engine (Windows built-in voices by default, Piper when
/// installed and selected) and falls back gracefully. Fully offline in all configurations.
/// </summary>
public sealed class TtsService : ITtsService
{
    private readonly WindowsTtsService _windows;
    private readonly PiperTtsService _piper;
    private readonly Func<bool> _preferPiper;

    public TtsService(WindowsTtsService windows, PiperTtsService piper, Func<bool> preferPiper)
    {
        _windows = windows;
        _piper = piper;
        _preferPiper = preferPiper;
    }

    public bool IsAvailable => _windows.IsAvailable || _piper.IsAvailable;

    public IReadOnlyList<string> ListVoices()
    {
        var voices = new List<string>(_windows.ListVoices());
        if (_piper.IsAvailable) voices.AddRange(_piper.ListVoices());
        return voices;
    }

    public async Task SynthesizeToWavAsync(string text, string voiceId, string outputWavPath, CancellationToken cancellationToken = default)
    {
        if (_preferPiper() && _piper.IsAvailable)
        {
            try
            {
                await _piper.SynthesizeToWavAsync(text, voiceId, outputWavPath, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // fall through to Windows voice
            }
        }

        if (voiceId.StartsWith("piper:", StringComparison.OrdinalIgnoreCase) && _piper.IsAvailable)
        {
            await _piper.SynthesizeToWavAsync(text, voiceId, outputWavPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _windows.SynthesizeToWavAsync(text, voiceId, outputWavPath, cancellationToken).ConfigureAwait(false);
    }
}
