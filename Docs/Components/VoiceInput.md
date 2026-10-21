# Voice input

`IVoiceRecorder`, `Dictation`, `SpeechService` and `WhisperEngine` turn speech into text for the entry and the question boxes.

## Recording is the platform's job

`IVoiceRecorder` is implemented by each head: the Android head records 16 kHz mono PCM through `AudioRecord` after asking for the microphone permission (the first use shows Android's prompt, and a refusal becomes a message telling the user to allow it in the app's settings); the desktop preview head uses the Windows multimedia API or the first of `parecord` and `arecord` the system has. A client without a recorder simply shows no microphone buttons.

## One dictation per text box

`Dictation` is the behavior behind a microphone button, so the Journal and Ask screens share it and differ only in what they do with the text. The first press starts recording; the second stops it, wraps the PCM in a WAV header, sends it with `Transcribe` and hands the trimmed text to its owner, who adds it to the end of the existing text with a space when needed. A recording stops by itself after sixty seconds. One shorter than 300 milliseconds is dropped as nothing recorded, and a recording that produced no words reports that nothing was heard. The button is unavailable while a recording is being transcribed, and, for the entry, while an earlier day is shown, but it can always stop a recording in progress. Every failure reaches the screen's error bar.

## Transcribing on the server

`SpeechService` validates the base64 audio (not empty, at most 4 MB) and either runs the configured speech command, with `{file}` replaced by a temporary WAV file that is deleted afterwards and a two minute limit, or hands the audio to `WhisperEngine`.

`WhisperEngine` runs Whisper through whisper.cpp inside the server process. The native libraries are embedded in the server executable and extracted into the data folder on first use, because the library only searches below the application's base directory, which is not writable for a single-file app. The model named in the config is downloaded into the data folder when the server starts, so the first recording does not wait for it, and a name that is not a plain file name is refused so it can never point outside the models folder. The model is kept in memory for ten minutes after the last recording and then released. Text segments that are only a bracketed annotation, such as a noise marker, are left out.
