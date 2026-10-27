# MauiChat: a minimal on-device chat

A whole chat app in one C# file, built on the Emgu LiteRT chat libraries:

- `Emgu.LiteRT.LM.Maui`'s `ChatPage` (`MainPage.cs`): the header with the model, the saved chats, a new chat and the
  settings; the chat itself (`ChatView`: the transcript, the message box with Send, Stop, attach and the microphone, the
  card that downloads and loads the model); the chips above the message box (Think, Web search); and the settings
  sheet - a side panel when the window is wide - with the built-in settings and each feature's section.
- Its features (`ChatPage.DefaultFeatures`): the date and time, a calculator and unit conversion; memories about the
  user; the web (the weather, Wikipedia, currency conversion, reading pages - and web search with a Tavily API key,
  entered in the settings); the user's location (off until allowed); the clipboard (only for a message about it); and
  the user's goals; and `WrittenDocumentsFeature` (documents the model writes with `create_document` - Word, PDF or
  Markdown - as a card with Open and Share, kept with the reply in the saved chat). Plus one of the app's own,
  `DiceFeature` (also in `MainPage.cs`): a `roll_die` tool, a line of instructions and a switch in the settings - about
  20 lines.
- `Emgu.LiteRT.LM.Chat.Sqlite`'s `ChatHistoryStore`, `SqliteAgentMemoryStore` and `SqliteAgentGoalStore`: the chats,
  memories and goals, saved in one SQLite database.

Two models to choose from: Qwen3 0.6B (`Emgu.LiteRT.LM.Models.Qwen3`, a 345 MB download, quick on a phone's CPU; it
takes no audio and calls no tools, so the microphone sends the words the platform's speech recognizer writes down) and
Gemma 4 E2B (2.6 GB; images, audio and the features' tools).

`App.xaml` shows how to restyle the chat: it changes the accent colour (the Send button, the user's bubbles, the chips
and the links follow), replaces the Send button's style and the user messages' template, by defining resources with
their keys. The colour and font keys are listed in the library's `Themes/ChatTheme.xaml`, the template and style keys
in `Themes/ChatStyles.xaml`. `MauiProgram.cs` calls `UseLiteRtChat()`, which registers the handlers the page needs.

## Building

As for the MAUI demo (`../Maui/README.md`): build the platform's native libraries, generate the projects'
`Directory.Build.props` with CMake, then for example

```bash
dotnet build Emgu.LiteRT.Example/MauiChat/MauiChat.csproj -f net10.0-android
dotnet build Emgu.LiteRT.Example/MauiChat/MauiChat.csproj -f net10.0-maccatalyst -p:ValidateXcodeVersion=false
```

The chats are kept in `MauiChat/chats` in the app's data folder: on Mac Catalyst that folder is `~/Library`, where the
demo keeps its own `chats/`. The microphone, speech and location permissions are in
`Platforms/Android/AndroidManifest.xml` and the `Info.plist` files.
