using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// Offline TTS using the Windows built-in speech voices (SAPI). Keeps the base installation
/// tiny: no model download is required. Output is a 16 kHz (or voice default) mono WAV.
/// </summary>
public sealed class WindowsTtsService : ITtsService
{
    public const string DefaultVoiceId = "sapi:default";

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                return synth.GetInstalledVoices().Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public IReadOnlyList<string> ListVoices()
    {
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices()
                .Select(v => "sapi:" + v.VoiceInfo.Name)
                .ToList();
            voices.Insert(0, DefaultVoiceId);
            return voices;
        }
        catch (Exception)
        {
            return [DefaultVoiceId];
        }
    }

    public Task SynthesizeToWavAsync(string text, string voiceId, string outputWavPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("TTS text is empty.", nameof(text));

            Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
            using var synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();

            var voiceName = ResolveVoice(synth, voiceId);
            if (voiceName is not null)
                synth.SelectVoice(voiceName);

            // Monaural, speech-friendly format.
            synth.SetOutputToWaveFile(outputWavPath, new SpeechAudioFormatInfo(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.Speak(text);
            synth.SetOutputToNull();

            if (!File.Exists(outputWavPath) || new FileInfo(outputWavPath).Length <= 44)
                throw new InvalidOperationException("TTS produced no audio output.");
        }, cancellationToken);
    }

    private static string? ResolveVoice(SpeechSynthesizer synth, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId) || voiceId == DefaultVoiceId) return null;
        var name = voiceId.StartsWith("sapi:", StringComparison.OrdinalIgnoreCase) ? voiceId[5..] : voiceId;
        var match = synth.GetInstalledVoices()
            .FirstOrDefault(v => v.VoiceInfo.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || v.VoiceInfo.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return match?.VoiceInfo.Name;
    }
}
