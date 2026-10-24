# Stable Diffusion 1.5 as a second image model - evaluated, not adopted

**Decision (2026-10-24): skipped.** The demo keeps Z-Image-Turbo (`ZImageTurbo`, see [MODELS.md](MODELS.md)) as its only
image model. This note records what was found, so the question can be picked up again without repeating the research.

## Why it was considered

Z-Image-Turbo is heavy for phones: a 10.6 GB download plus ~9 GB of XNNPACK weight caches written by the first picture,
256 x 256 only, and minutes per picture on a phone's CPU (the GPU doesn't help). MediaPipe's on-device image generation
uses Stable Diffusion 1.5, which is far smaller and made for phone GPUs, so it looked like a candidate for phones while
Macs and Windows keep Z-Image.

## Comparison

Z-Image-Turbo's numbers are measured in this repo; Stable Diffusion's are from Google's documentation and announcements and
the published files, not measured here.

| | Stable Diffusion 1.5 | Z-Image-Turbo (in use) |
|---|---|---|
| Released | October 2022 (RunwayML; after CompVis / Stability AI's 1.4, August 2022) | November 2025 (Alibaba Tongyi-MAI) |
| Model | ~0.9 B UNet, CLIP text encoder, VAE | 6 B diffusion transformer, Qwen3-4B text encoder |
| Size | ~1.6-2 GB as LiteRT int8 (see the conversions below) | 10.6 GB, plus ~9 GB of caches |
| Resolution | 512 x 512 | 256 x 256 (the only size the LiteRT conversion offers) |
| Steps | ~20 | 8 |
| Speed | Built for phone GPUs; Google showed pictures in seconds to tens of seconds on high-end Android phones | M1 CPU 23 s a picture (67 s for the first); Pixel Fold 883 s for the first, including writing the caches |
| Quality | Clearly weaker: loose prompt following, frequent anatomy errors | Much better prompt following and realism |
| Licence | CreativeML OpenRAIL-M (use restrictions, see below) | Apache-2.0 |

## What is published (checked 2026-10-24)

- **No official LiteRT conversion.** Google's `litert-community` organisation on Hugging Face has no diffusion model other
  than `litert-community/Z-Image-Turbo-LiteRT`, which we use.
- **Google's conversion route:** [LiteRT Torch](https://github.com/google-ai-edge/litert-torch) (formerly AI Edge Torch)
  has a maintained example, `litert_torch/generative/examples/stable_diffusion/` (last changed January 2026, when it was
  renamed). Its `convert_to_tflite.py` turns `v1-5-pruned-emaonly.safetensors` into `clip.tflite` (text encoder),
  `diffusion.tflite` (UNet) and `decoder.tflite` (VAE); the image encoder (image-to-image) isn't supported. `pipeline.py` is
  a reference Python pipeline with k-LMS, Euler and Euler-ancestral samplers, run at 20 steps. This is the path we would
  take: convert, then port the pipeline to C# on `CompiledModel`, as was done for Z-Image.
- **The weights:** RunwayML deleted the official `runwayml/stable-diffusion-v1-5` repository (and its GitHub repository) in
  2024. LiteRT Torch's README points to the community mirror
  [stable-diffusion-v1-5/stable-diffusion-v1-5](https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5):
  ungated, ~1.15 million downloads, CreativeML OpenRAIL-M, "a mirror of the now deprecated `runwayml/stable-diffusion-v1-5`
  … not affiliated in any way with RunwayML". The mirror has no separate licence file; the canonical text is the
  [CompVis repository's LICENSE](https://github.com/CompVis/stable-diffusion/blob/main/LICENSE).
- **Third-party LiteRT conversions** (anonymous accounts; not something to depend on):
  - [SearchingMan/stable-diffusion-v1-5-litert](https://huggingface.co/SearchingMan/stable-diffusion-v1-5-litert) - fp32,
    and int8: text encoder 493 MB, UNet 866 MB, VAE 198 MB; no licence stated. An identical copy exists under another
    account.
  - [timmyboy26/stable-diffusion-v1-5-LiteRT-NPU](https://huggingface.co/timmyboy26/stable-diffusion-v1-5-LiteRT-NPU) -
    LiteRT Torch-style `clip`/`diffusion`/`decoder` (~1.95 GB), plus compiled NPU builds for MediaTek MT6991/MT6993 and
    Google Tensor G5/G6 (Pixel 10/11-class phones; not our test phones). OpenRAIL-M.
- **MediaPipe's Image Generator** is still "experimental", "only available for Android" (Java/Kotlin, Android 12+, 8 GB of
  RAM recommended), and has no ready model download: SD 1.5 checkpoints are converted with its own script into its own
  format. It would only help as an Android-only binding, which doesn't fit the C# approach.
- **Not available:** SD-Turbo / SDXL-Turbo conversions (they would also be non-commercial).

## The licence: CreativeML OpenRAIL-M

Read from the canonical text. Not legal advice.

- **Allows:** a perpetual, worldwide, royalty-free licence to use, modify and redistribute the model and models derived from
  it, commercially too; the licensor claims no rights in generated pictures.
- **Use restrictions (Attachment A):** not to use the model to break laws; to exploit or harm minors; to generate or spread
  verifiably false information meant to harm others; to generate or spread personal information that can be used to harm
  someone; to defame, disparage or harass; for fully automated decisions that adversely affect someone's legal rights; to
  discriminate based on social behaviour or personal characteristics, or on legally protected characteristics; to exploit a
  group's vulnerabilities in a way likely to cause harm; for medical advice or interpreting medical results; for law
  enforcement, justice, immigration or asylum purposes (e.g. predicting crime).
- **Redistribution obliges us (sections 4 and 5) to:**
  1. include the use restrictions "as an enforceable provision" in any agreement governing use or distribution, tell users
     the model is subject to them, and require users to comply;
  2. give recipients a copy of the licence;
  3. mark modified files with a prominent notice (a LiteRT conversion is a derived model);
  4. keep the original copyright and attribution notices.
- **Also:** section 7 reserves the licensor's right to restrict use that violates the licence and asks for "reasonable
  efforts to use the latest version"; section 8 grants no trademark use.

## Why it was skipped

- **Age and quality:** a 2022 model, clearly weaker than Z-Image-Turbo.
- **Licence burden:** unlike Apache-2.0, it needs an enforceable user agreement. The demo has none, so a click-through when
  the feature is turned on would be needed, with the terms passed on by any package that ships it.
- **Uncertain source:** the original repository was withdrawn; only a community mirror and anonymous conversions remain.

## If it is revisited

What would make it worth another look: a Google-hosted LiteRT conversion (e.g. in `litert-community`); a newer small
diffusion model with a permissive licence, which would likely be the better candidate; or a real need for image generation
on phones that Z-Image can't meet.

The plan would be:

1. Convert from the community mirror with LiteRT Torch's `convert_to_tflite.py` (Mac), int8.
2. Port the pipeline to C# in `Emgu.LiteRT.Models`, next to `ZImageTurbo`: the CLIP BPE tokenizer, one sampler (Euler),
   classifier-free guidance, the VAE decode. Check it against `pipeline.py`'s output, as `ZImageTurbo` was checked against
   its reference.
3. Host the converted files on Emgu's public storage (hash-checked, like the YOLOX weights), with the licence and a notice
   that they were converted.
4. In the demo: offer it on phones (Z-Image on Macs and Windows), with a click-through for the use restrictions when
   image generation is turned on, the acceptance recorded, and the model and licence on the licences page.
5. Measure CPU and GPU speed and memory on a Pixel Fold and a Galaxy S10, and on the Mac.
