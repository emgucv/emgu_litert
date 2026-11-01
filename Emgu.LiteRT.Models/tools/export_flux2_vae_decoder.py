#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Export FLUX.2's VAE decoder as a GPU-friendly LiteRT graph, for Bonsai Image 4B at 512 x 512.

Why: litert-community's bonsai-flux2-4b.litertlm (LiteRT-LM's omni text2image, model "bonsai-flux2") holds 256 x 256
graphs only. Its Hugging Face repository has the 512 diffusion transformer (dit_gpu_int4b32.tflite) but a 512 VAE
decoder (vae_dec_fp32.tflite) that writes GroupNorm with GATHER_ND and SUM, which LiteRT's GPU delegates reject - and a
.litertlm runs every graph on one backend. This script exports the decoder again from the PyTorch weights with
GroupNorm as reshape, MEAN and RSQRT (the ops the 256 decoder in the .litertlm uses), which runs on the CPU and the GPU.

Contract (LiteRT-LM's Flux2VaeDecoderStage): input the unpatchified, de-normalized latent, NCHW [1, 32, size/8, size/8];
output the image, NCHW [1, 3, size, size], as diffusers' AutoencoderKLFlux2.decode returns it (post_quant_conv, then the
decoder).

Environment (2026-11-01): Python 3.12, litert-torch 0.10.0, torch 2.14.1, diffusers 0.42.1, safetensors; the weights
are prism-ml/bonsai-image-ternary-4B-unpacked's vae/ (revision 30bbc032dd26939bff95c3d523477ad5065c3e1a; the same 168
MB as black-forest-labs/FLUX.2-klein-4B's vae/).

    python3.12 -m venv venv && venv/bin/pip install litert-torch diffusers safetensors
    venv/bin/python export_flux2_vae_decoder.py <vae folder> vae_dec_512_gpu.tflite --size 512 [--reference vae_dec_fp32.tflite]

Then the .litertlm (LiteRT-LM's CLI, bazel target //python/litert_lm_cli:litert-lm, run with PYTHONPATH set to its
runfiles' litert_lm/python and litert_lm): `litert-lm unpack bonsai-flux2-4b.litertlm --output-dir unpacked`; in the
copy, set `img_size: 512` in Text2ImageMetadataProto.pbtext, replace the image_denoiser section's file with
dit_gpu_int4b32.tflite and the image_decoder's with this export; `litert-lm pack <folder> --output
bonsai-flux2-4b-512.litertlm`. The result (2026-11-01, SHA-256 0bd495cf...0fbdba, 4,267,950,124 bytes) is on Emgu's
public S3 with its notice and licence (emgu-public/bonsai/); `Emgu.LiteRT.LM.Models`' BonsaiImage4B512 downloads it.
Checked: this export matched vae_dec_fp32.tflite to 6e-6 on a random latent, and the package drew 512 x 512 on an
M1's CPU (66 s) and GPU (Metal, 25 s).
"""
import argparse
import sys

import torch
import torch.nn as nn
from diffusers import AutoencoderKLFlux2


class MeanGroupNorm(nn.Module):
    """GroupNorm as reshape -> mean -> variance as the mean of squares -> rsqrt: ops GPU delegates take."""

    def __init__(self, norm: nn.GroupNorm):
        super().__init__()
        self.groups = norm.num_groups
        self.eps = norm.eps
        self.weight = norm.weight
        self.bias = norm.bias

    def forward(self, x):
        # [N, C, H, W] in the blocks, [N, C, L] in the attention.
        shape = x.shape
        n, c = shape[0], shape[1]
        rest = 1
        for d in shape[2:]:
            rest *= d
        g = x.reshape(n, self.groups, (c // self.groups) * rest)
        mean = g.mean(dim=-1, keepdim=True)
        centered = g - mean
        var = (centered * centered).mean(dim=-1, keepdim=True)
        x = (centered * torch.rsqrt(var + self.eps)).reshape(shape)
        affine = [1, c] + [1] * (len(shape) - 2)
        return x * self.weight.reshape(affine) + self.bias.reshape(affine)


def replace_group_norms(module: nn.Module) -> int:
    count = 0
    for name, child in module.named_children():
        if isinstance(child, nn.GroupNorm):
            setattr(module, name, MeanGroupNorm(child))
            count += 1
        else:
            count += replace_group_norms(child)
    return count


class Decoder(nn.Module):
    """AutoencoderKLFlux2.decode without the patchify and batch-norm steps LiteRT-LM does on the host."""

    def __init__(self, vae):
        super().__init__()
        self.post_quant_conv = vae.post_quant_conv
        self.decoder = vae.decoder

    def forward(self, z):
        if self.post_quant_conv is not None:
            z = self.post_quant_conv(z)
        return self.decoder(z)


def compare(path, reference, latent):
    """The largest difference between two exported decoders on the same latent."""
    import numpy as np
    from ai_edge_litert.interpreter import Interpreter

    outputs = []
    for model in (path, reference):
        run = Interpreter(model_path=model, num_threads=6).get_signature_runner()
        outputs.append(run(args_0=latent)["output_0"])
    return float(np.abs(outputs[0] - outputs[1]).max())


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("vae", help="the VAE folder (config.json, diffusion_pytorch_model.safetensors)")
    parser.add_argument("output", help="the .tflite to write")
    parser.add_argument("--size", type=int, default=512, help="the picture size, a multiple of 16")
    parser.add_argument("--reference", help="a decoder .tflite to compare with, e.g. the repository's vae_dec_fp32.tflite")
    args = parser.parse_args()

    vae = AutoencoderKLFlux2.from_pretrained(args.vae, torch_dtype=torch.float32).eval()
    model = Decoder(vae).eval()
    latent = torch.randn(1, 32, args.size // 8, args.size // 8, generator=torch.Generator().manual_seed(0))
    with torch.no_grad():
        before = model(latent)
        replaced = replace_group_norms(model)
        after = model(latent)
    print(f"replaced {replaced} GroupNorms; largest change {float((before - after).abs().max()):.2e}; "
          f"output {list(after.shape)}")

    import litert_torch
    litert_torch.convert(model, (latent,)).export(args.output)
    print("wrote", args.output)

    if args.reference:
        difference = compare(args.output, args.reference, latent.numpy())
        print(f"largest difference from {args.reference}: {difference:.2e}")
        if difference > 1e-3:
            sys.exit("the export doesn't match the reference")


if __name__ == "__main__":
    main()
