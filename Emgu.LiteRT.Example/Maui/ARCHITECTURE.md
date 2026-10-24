# MAUI demo architecture

How the MAUI demo's code is organised. Moved here from CLAUDE.md, which keeps the short version.

## Folders, the chat page and its features

- The demo's sources are in folders by area - `Chat/` (the chat page: `LiteRtLmChatPage.cs` holds its fields, nested types, constructor and lifecycle - the constructor calls the `Build...` methods of `.Layout.cs` and `.Settings.cs` - and the partials `.ModelList`, `.Loading`, `.Context` (window size, usage, compaction), `.Sending`, `.Agent` (tools, instructions, client, web search), `.Attachments`, `.VoiceMessages`, `.SavedChats`, `.Memories`, `.TranscriptView` and the settings ones; templates, `ChatAgentTools`), `Chat/Features/` (one partial of `LiteRtLmChatPage` per feature, or - moving there one at a time - a class of its own, `MeetingFeature`, `ExerciseFeature`, `ImageFeature`, `WrittenDocumentsFeature`, `TaskFeature`, `TranslationFeature`, `GoalsFeature`, `DeviceActionsFeature`, `PastChatsFeature`, `PhotoSearchFeature` so far; a reply's page data is `StreamingBubble` (`Chat/StreamingBubble.cs`), that reaches the page only through `IChatHost` (`Chat/IChatHost.cs`, implemented explicitly in `LiteRtLmChatPage.Host.cs`, which also creates the features); add a member to `IChatHost` only when a feature needs it); document, photo and past-chat search share the search model through `SearchEmbedder` (`Chat/SearchEmbedder.cs`: loading, the GPU-to-CPU fallback, `Gate` - one embedding at a time - and releasing it), which reads Settings > Embedding model through `ISearchModelChoice` (implemented in `LiteRtLmChatPage.ModelSettings.cs`), `Chat/Visual/` (visual agents and camera actions), `Vision/` (the image demo pages, `LiveCamera`), `Audio/`, `Services/` (photo library, device actions, documents, downloads), `Pages/` (About, Licences, Storage, Model Checker) - all in namespace `Maui.Demo.Lite`.
- Code that needs no UI or device - saved chats (`ChatStore`), `ChatToolGroups`, `ChatPlannerInstructions`, `ChatSessionTools`, `McpServers`, chat export and search, storage bookkeeping, `VisualHostCatalog` and the camera monitors' actions, `WorkloadPolicy` - is in the class library `Emgu.LiteRT.Example/Maui.Demo.Lite.Core` (net<major>.0 from `cmake/Maui.Demo.Lite.Core.Directory.Build.props.in`, `InternalsVisibleTo` the demo and the test project), which both reference instead of the tests linking demo files; it must not use MAUI.
- Add new testable logic there.

## The home page

- The MAUI demo's home page (`MainPage.xaml.cs`) has two tabs, pills like the Emgu CV demo's category pills: "LiteRT-LM" (the chat; on the platforms with LiteRT-LM) and "TF Lite" (Coco SSD MobileNet, MobileNet, Inception, Model Checker); the last one used is kept in Preferences (`main_page_tab`).

## Settings

- The chat page's settings are `ChatSettings` (`Maui.Demo.Lite.Core/Chat/ChatSettings.cs`): typed properties with their Preferences keys and defaults in one place, never throwing (a failing store gives the default). The app's instance is `AppSettings.Chat` (`Services/AppSettings.cs`, over MAUI's Preferences); tests use `InMemorySettingsStore` (`UnitTestChatSettings`). Add a new setting there rather than calling `Preferences` directly.
