namespace MeetVault.Infrastructure;

/// <summary>
/// Embedded model registry: the complete catalog of CPU-only packs that MeetVault can
/// download, verify and install. Everything runs on CPU with 16 GB RAM (most entries fit
/// 4 GB). Weights are never bundled with the app — they are downloaded on demand and
/// SHA-256-verified before installation.
/// </summary>
public static class EmbeddedModelRegistry
{
    public const string Json = """
    {
      "packs": [
        {
          "id": "runtime-ffmpeg",
          "displayName": "FFmpeg (audio extraction)",
          "kind": "runtime",
          "version": "9.0.1",
          "description": "Extracts and converts meeting audio (any input format to 16 kHz mono WAV). Required for every import.",
          "license": "LGPL-2.1+ / GPL-3.0 (build)",
          "urls": [ "https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip" ],
          "sizeBytes": 111253802,
          "sha256": "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9",
          "fileName": "ffmpeg-9.0.1-essentials_build.zip",
          "extractZip": true,
          "executableRelativePath": "ffmpeg.exe",
          "minFreeDiskGb": 0.5,
          "group": "Runtimes (CPU)"
        },
        {
          "id": "runtime-whisper-cpu",
          "displayName": "whisper.cpp (CPU transcription)",
          "kind": "runtime",
          "version": "1.9.4 (b5130)",
          "description": "whisper.cpp command-line runtime built for plain CPUs. No GPU required.",
          "license": "MIT",
          "urls": [ "https://github.com/ggml-org/whisper.cpp/releases/download/b5130/whisper-bin-x64.zip" ],
          "sizeBytes": 8573270,
          "sha256": "f9ec6c52a2e949b62ab51fa21d0d497958f9e41c3010c157c4e42932d5316f3c",
          "fileName": "whisper-bin-x64.zip",
          "extractZip": true,
          "executableRelativePath": "whisper-cli.exe",
          "minFreeDiskGb": 0.2,
          "group": "Runtimes (CPU)"
        },
        {
          "id": "runtime-llama-cpu",
          "displayName": "llama.cpp (CPU inference)",
          "kind": "runtime",
          "version": "b6666",
          "description": "llama.cpp llama-server for CPU-only local inference. Analysis runs entirely on this machine.",
          "license": "MIT",
          "urls": [ "https://github.com/ggml-org/llama.cpp/releases/download/b6666/llama-b6666-bin-win-cpu-x64.zip" ],
          "sizeBytes": 14131408,
          "sha256": "7fd0ef2c9a001df2cea91f6b84d0e7ef24bf76ddec9c3aa52a89379921e45ca3",
          "fileName": "llama-b6666-bin-win-cpu-x64.zip",
          "extractZip": true,
          "executableRelativePath": "llama-server.exe",
          "minFreeDiskGb": 0.3,
          "group": "Runtimes (CPU)"
        },
        {
          "id": "runtime-piper-tts",
          "displayName": "Piper (neural TTS, offline)",
          "kind": "runtime",
          "version": "2023.11.14-2",
          "description": "Piper text-to-speech runtime. Produces the spoken audio brief fully offline; a Windows built-in voice is used until this is installed.",
          "license": "MIT",
          "urls": [ "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip" ],
          "sizeBytes": 22477236,
          "sha256": "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea",
          "fileName": "piper_windows_amd64.zip",
          "extractZip": true,
          "executableRelativePath": "piper.exe",
          "minFreeDiskGb": 0.3,
          "group": "Runtimes (CPU)"
        },
        {
          "id": "whisper-tiny-multilingual",
          "displayName": "Whisper Tiny multilingual (q5_1)",
          "kind": "whisper-model",
          "version": "1",
          "description": "Fastest transcription (~1 GB RAM). Best for quick drafts; decent accuracy in several languages.",
          "license": "MIT",
          "urls": [ "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny-q5_1.bin" ],
          "sizeBytes": 32152673,
          "sha256": "818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7",
          "fileName": "ggml-tiny-q5_1.bin",
          "extractZip": false,
          "minRamGb": 4,
          "group": "Transcription models (CPU)"
        },
        {
          "id": "whisper-base-multilingual",
          "displayName": "Whisper Base multilingual (q5_1)",
          "kind": "whisper-model",
          "version": "1",
          "description": "Good speed/accuracy balance (~1.2 GB RAM). Recommended default for meetings on CPU.",
          "license": "MIT",
          "urls": [ "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin" ],
          "sizeBytes": 59707625,
          "sha256": "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898",
          "fileName": "ggml-base-q5_1.bin",
          "extractZip": false,
          "minRamGb": 4,
          "group": "Transcription models (CPU)"
        },
        {
          "id": "whisper-small-multilingual",
          "displayName": "Whisper Small multilingual (q5_1)",
          "kind": "whisper-model",
          "version": "1",
          "description": "Most accurate lightweight option (~2.5 GB RAM). Slower on CPU; choose for difficult audio.",
          "license": "MIT",
          "urls": [ "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin" ],
          "sizeBytes": 190085487,
          "sha256": "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
          "fileName": "ggml-small-q5_1.bin",
          "extractZip": false,
          "minRamGb": 8,
          "group": "Transcription models (CPU)"
        },
        {
          "id": "whisper-vad-silero",
          "displayName": "Silero VAD (voice activity)",
          "kind": "whisper-model",
          "version": "5.1.2",
          "description": "Voice-activity detection for whisper.cpp. Skips silence so transcription is faster and cleaner. Recommended.",
          "license": "MIT",
          "urls": [ "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin" ],
          "sizeBytes": 885098,
          "sha256": "29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf",
          "fileName": "ggml-silero-v5.1.2.bin",
          "extractZip": false,
          "minRamGb": 1,
          "group": "Transcription models (CPU)"
        },
        {
          "id": "qwen2.5-0.5b-instruct-q4km",
          "displayName": "Qwen2.5 0.5B Instruct (Q4_K_M)",
          "kind": "llm-model",
          "version": "1",
          "description": "Smallest analysis model (~1.2 GB RAM). Runs on almost any CPU; quality is basic.",
          "license": "Apache-2.0 (Qwen) / GGUF q4_k_m",
          "urls": [ "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf" ],
          "sizeBytes": 491400032,
          "sha256": "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
          "fileName": "qwen2.5-0.5b-instruct-q4_k_m.gguf",
          "extractZip": false,
          "minRamGb": 4,
          "group": "Analysis models (CPU)"
        },
        {
          "id": "qwen2.5-1.5b-instruct-q4km",
          "displayName": "Qwen2.5 1.5B Instruct (Q4_K_M)",
          "kind": "llm-model",
          "version": "1",
          "description": "Balanced CPU analysis (~2.5 GB RAM). Solid summaries and action items on 8 GB machines.",
          "license": "Apache-2.0 (Qwen) / GGUF q4_k_m",
          "urls": [ "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf" ],
          "sizeBytes": 1117320736,
          "sha256": "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e",
          "fileName": "qwen2.5-1.5b-instruct-q4_k_m.gguf",
          "extractZip": false,
          "minRamGb": 8,
          "group": "Analysis models (CPU)"
        },
        {
          "id": "qwen2.5-3b-instruct-q4km",
          "displayName": "Qwen2.5 3B Instruct (Q4_K_M)",
          "kind": "llm-model",
          "version": "1",
          "description": "Best quality that stays comfortable on 16 GB RAM (~4 GB usage). Recommended default; pure CPU.",
          "license": "Qwen Research (Qwen) / GGUF q4_k_m",
          "urls": [ "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf" ],
          "sizeBytes": 2104932768,
          "sha256": "626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d",
          "fileName": "qwen2.5-3b-instruct-q4_k_m.gguf",
          "extractZip": false,
          "minRamGb": 16,
          "group": "Analysis models (CPU)"
        },
        {
          "id": "piper-voice-en-lessac-medium",
          "displayName": "Piper voice: en_US lessac (medium)",
          "kind": "tts-voice",
          "version": "1.0.0",
          "description": "Natural English (US) voice for the spoken audio brief. ~63 MB, runs instantly on CPU.",
          "license": "MIT",
          "urls": [ "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/lessac/medium/en_US-lessac-medium.onnx" ],
          "sizeBytes": 63201294,
          "sha256": "5efe09e69902187827af646e1a6e9d269dee769f9877d17b16b1b46eeaaf019f",
          "fileName": "en_US-lessac-medium.onnx",
          "extractZip": false,
          "minRamGb": 2,
          "extraFiles": [
            {
              "relativePath": "en_US-lessac-medium.onnx.json",
              "url": "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json",
              "sha256": "efe19c417bed055f2d69908248c6ba650fa135bc868b0e6abb3da181dab690a0",
              "sizeBytes": 4885,
              "downloadFileName": "en_US-lessac-medium.onnx.json"
            }
          ],
          "group": "TTS voices (CPU)"
        }
      ]
    }
    """;
}
