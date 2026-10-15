# Pixel Fold responsiveness investigation — 2026-10-15

Test device: Google Pixel Fold (`felix`), Android 17, connected through wireless ADB. Debug arm64 build, with the
existing `UiStallWatchdog` enabled. Qwen3 0.6B CPU and EmbeddingGemma 2 Text Vision 440M CPU were already downloaded.
The starting battery was 78% on external power. The native/runtime pins were unchanged.

## Finding

A synthetic saved chat reproduced long main-thread stalls. Opening all of its rows in one UI callback attaches
native Android views, formats Markdown, and requests animated scrolling for every row/reply. Those queued scrolls
also compete with layout when the fold configuration changes or a reply streams into the already-large transcript.

The existing log contained a roughly two-second event from October 13 whose Java stack was idle in `nativePollOnce`.
Android process-exit history also contained background ANR annotations, low-memory exits and one older crash, with
no retrievable trace attached to those entries. These records do not establish the cause of the original
October 10 “isn't responding” dialog. A watchdog ping delay is not itself an Android ANR report; see Android's
[ANR diagnosis guidance](https://developer.android.com/topic/performance/anrs/diagnose-and-fix-anrs).

## Reproduction and evidence

A temporary, explicitly enabled module-initializer probe navigated to a chat page backed by a separate database in
the app cache. It seeded 80 synthetic exchanges (160 messages), with Markdown paragraphs, code blocks and lists in
the replies, then exercised reopening, dropping context, streaming a new reply, summary compaction, document
indexing and photo indexing. Test conversations/documents did not enter the user's normal chat database. Existing
model files were reused. Photo indexing used the app's already-granted media access and its real embedding path.

Baseline measurements (Toronto local time):

- 11:30:28: open 160-message conversation; the method returned after 3,420 ms.
- The watchdog reported a continuous **4,767 ms** UI delay during opening/initial layout. Its stack included
  `TextView.drawableStateChanged`, `View.dispatchAttachedToWindow` and `ViewGroup.addView`, under MAUI's main-thread
  `RunnableImplementor` callback.
- A subsequent **4,159 ms** UI delay during the long transcript's layout/streaming/fold exercise included
  `ContentViewGroup.n_onLayout`, `ShellPageContainer.n_onLayout`, `CoordinatorLayout.onLayout` and
  `ViewRootImpl.performTraversals`.
- Shorter events (about 1.0–1.4 seconds) included activity-stop and Android content-capture traversal stacks.
- Drop-context compaction, summary compaction and synthetic document indexing completed. The baseline photo pass
  saved ten embeddings before its three-minute cancellation budget was observed between native calls. It was a
  bounded stress run, not a completed 12-photo throughput benchmark.

Raw device logs, app-only logcat and temporary-database counts were collected locally. The report includes only
synthetic workload details, timings and stack summaries, not personal chat text, photos or their metadata.

## Change

`OpenConversationAsync` now marks the page busy while restoring and gives input/layout a turn between roughly
16 ms rendering slices. It shows opening progress and prevents another chat/model action from interleaving with a
partially restored transcript. Leaving private mode still happens before entering this busy state.

Per-row scrolling is suppressed throughout restoration, followed by one nonanimated scroll to the bottom. Normal
streaming/tool/media scroll requests are coalesced to one pending frame/animation; updates arriving during an
animation request a final catch-up scroll. Message storage, model context and the contents/order of restored
messages are unchanged. The Debug watchdog remains enabled.

## Validation and limits

The same 160-message fixture reopened in 6,294 ms with **no watchdog event at or above one second** during that
opening. Total completion is slower because it yields to the UI; the result is improved responsiveness, not a
claim of faster overall rendering. The fixed pass also completed drop/summary compaction, streaming and document
indexing without a new threshold event through those stages.

The lifecycle exercise used Home/normal app launch and shell-requested fold states 0/2, with the override reset
after testing. Those exercise display/configuration transitions; they do not replace extended manual hinge testing.
An interrupted animated navigation in the temporary probe was rerun without animation before measuring the fixed
open. Startup/locked-display events were kept separate from the measured saved-chat window.

The fixed pass saved all four requested photo embeddings and reopened the resulting 162-message chat, with no
one-second watchdog event through that reopen. That later reopen took roughly 32 seconds under the post-indexing
workload/display conditions, so total latency for large histories still needs improvement. A subsequent two-second
event came from synchronous embedder disposal in the temporary probe; production `ReleaseEmbedderAsync` already
disposes on a worker. The probe was removed from the installed normal build, its scratch databases were removed,
and the fold-state override was reset. Original app data/models and the watchdog log were retained.

Android exit history after redeploy also carried non-user-perceptible `anrType=9`, 8,000 ms annotations on forced-stop
entries (the same ANR ID on successive entries). Android identifies type 9 as a
[job-service start timeout](https://developer.android.com/reference/android/app/AnrTypes#ANR_TYPE_JOB_SERVICE_START).
There was no matching retained `lastanr` dump identifying this app, and no job-service entry in its merged manifest.
These annotations remain un-attributed; the watchdog result must not be read as proof that all ANR sources are gone.

Android and Mac Catalyst builds passed; 13 private-session/workload-policy tests passed. Windows and iOS were not
run for this UI change. A later optional timing pass was interrupted by the locked display; it was not used to
replace the verified 16 ms rendering budget or the recorded comparison.

Full native image-generation stress remains untested on this phone: the model is absent and about 22 GB was free,
while the model plus weight caches need roughly 20–21 GB. It was not downloaded as part of this investigation.
Very large histories can still impose layout/memory costs because the transcript is not virtualized. The original
October 10 ANR remains unconfirmed; the evidence supports fixing the reproduced rendering stalls, not claiming its
historical cause has been proven.

For another occurrence, keep the app-specific `files/ui_stalls.log` and collect
`adb shell dumpsys activity exit-info com.emgu.litert`, along with the time, selected model/backend, active operation
and whether a fold/background transition occurred. An empty/idle Java stack alone is insufficient to identify the
blocked operation.
