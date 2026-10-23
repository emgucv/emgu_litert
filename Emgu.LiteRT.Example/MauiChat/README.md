# MauiChat: a minimal on-device chat

A whole chat app in a few lines, built on the Emgu LiteRT chat libraries:

- `Emgu.LiteRT.LM.Maui`'s `ChatView` (`MainPage.xaml`): the transcript, the message box with Send, Stop, attach and the
  microphone, and the card that downloads and loads the model.
- `Emgu.LiteRT.LM.Chat`'s `ChatConversation`, which the view creates for its model.
- `Emgu.LiteRT.LM.Chat.Sqlite`'s `ChatHistoryStore` (`MainPage.xaml.cs`): every exchange is saved, the last chat is
  reopened when the app starts, and the toolbar's **New chat** starts another.

The model is Qwen3 0.6B (`Emgu.LiteRT.LM.Models.Qwen3`, a 345 MB download, quick on a phone's CPU). It takes no audio,
so the microphone sends the words the platform's speech recognizer writes down (Android 13 and later, iOS, Mac
Catalyst). Swap in `Gemma4E2B` to send images and audio to the model itself.

`App.xaml` shows how to restyle the chat: it replaces the Send button's style and the user messages' template by
defining resources with their keys. The other keys are listed in the library's `Themes/ChatStyles.xaml`.

## Building

As for the MAUI demo (`../Maui/README.md`): build the platform's native libraries, generate the projects'
`Directory.Build.props` with CMake, then for example

```bash
dotnet build Emgu.LiteRT.Example/MauiChat/MauiChat.csproj -f net10.0-android
dotnet build Emgu.LiteRT.Example/MauiChat/MauiChat.csproj -f net10.0-maccatalyst -p:ValidateXcodeVersion=false
```

The chats are kept in `MauiChat/chats` in the app's data folder: on Mac Catalyst that folder is `~/Library`, where the
demo keeps its own `chats/`. The microphone and speech permissions are in `Platforms/Android/AndroidManifest.xml` and
the `Info.plist` files.
