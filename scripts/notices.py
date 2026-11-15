"""Collects the license notices of everything shipped in gesh into
Assets/notices.txt, shown in Home > Info > Copyright notices.

The texts come from the source on disk: the StereoKit fork and the libraries
it bundles, the dependencies CMake fetched for the Android build, the NuGet
packages, and the .NET for Android runtime packs. Run it after `just play`
(so the Android build's dependencies are fetched) whenever a dependency
changes.

Usage: python scripts/notices.py
"""
import glob
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SK = os.path.join(ROOT, "stereokit")
LIBS = os.path.join(SK, "StereoKitC", "libraries")
DEPS = os.path.join(SK, "bin", "intermediate", "Android_Arm64_Release", "_deps")
PACKS = r"C:\Program Files\dotnet\packs"
OUT = os.path.join(ROOT, "Assets", "notices.txt")

MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."""

# zstd's LICENSE file isn't in the amalgamated source StereoKit bundles; this
# is that file, the BSD license zstd is distributed under.
ZSTD_BSD = """BSD License

For Zstandard software

Copyright (c) Meta Platforms, Inc. and affiliates. All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

 * Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

 * Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

 * Neither the name Facebook, nor Meta, nor the names of its contributors may
   be used to endorse or promote products derived from this software without
   specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."""


def read(*parts):
    return open(os.path.join(*parts), encoding="utf8", errors="replace").read().strip()


def block(path, start, end):
    """The lines from the one containing `start` through the first after it
    containing `end`, without comment markers."""
    lines = open(path, encoding="utf8", errors="replace").read().splitlines()
    first = next(i for i, l in enumerate(lines) if start in l)
    last = next(i for i in range(first, len(lines)) if end in lines[i])
    out = [re.sub(r"^\s*(/\*+|\*/|\*|//)? ?", "", l).rstrip() for l in lines[first:last + 1]]
    return "\n".join(out).replace("*/", "").strip()


def pack(name):
    """The newest version folder of a .NET pack."""
    versions = sorted(glob.glob(os.path.join(PACKS, name, "*")), key=lambda p: [int(x) for x in re.findall(r"\d+", os.path.basename(p))])
    if not versions:
        raise SystemExit(f"No {name} pack in {PACKS}")
    return versions[-1]


def main():
    if not os.path.isdir(DEPS):
        raise SystemExit(f"{DEPS} is missing: run `just play` once first")
    mono = pack("Microsoft.NETCore.App.Runtime.Mono.android-arm64")
    android = pack("Microsoft.Android.Runtime.Mono.36.android-arm64")
    apache = read(DEPS, "vulkan_headers-src", "LICENSES", "Apache-2.0.txt")

    sections = [
        ("StereoKit", read(SK, "LICENSE")),
        ("sk_renderer", read(DEPS, "sk_renderer-src", "LICENSE")),
        ("sk_app", read(DEPS, "sk_app-src", "LICENSE")),
        ("micro_ply, ferr_hash, ferr_thread", block(os.path.join(LIBS, "micro_ply.h"), "ALTERNATIVE A - MIT License", "SOFTWARE.")),
        ("SharpGLTF", "Copyright (c) 2026 Vicente Penades\n\n" + MIT),
        ("meshoptimizer", read(DEPS, "meshoptimizer-src", "LICENSE.md")),
        ("cgltf", block(os.path.join(LIBS, "cgltf.h"), "cgltf is distributed under MIT license", "SOFTWARE.")),
        ("OpenXR loader", read(DEPS, "openxr_loader-src", "LICENSE")),
        ("Vulkan headers", read(DEPS, "vulkan_headers-src", "LICENSE.md") + "\n\n" + read(DEPS, "vulkan_headers-src", "LICENSES", "MIT.txt")),
        ("volk", read(DEPS, "volk-src", "LICENSE.md")),
        ("stb_image, stb_image_write, stb_truetype", block(os.path.join(LIBS, "stb_image.h"), "This software is available under 2 licenses", "SOFTWARE.")),
        ("miniaudio", block(os.path.join(LIBS, "miniaudio.h"), "This software is available as a choice of the following licenses", "SOFTWARE.")),
        ("nanosvg", block(os.path.join(LIBS, "nanosvg.h"), "Copyright (c) 2013-14 Mikko Mononen", "Maxim Shemanarev")),
        ("sokol_time", block(os.path.join(LIBS, "sokol_time.h"), "zlib/libpng license", "distribution.")),
        ("qoi", block(os.path.join(LIBS, "qoi.h"), "LICENSE: The MIT License", "SOFTWARE.")),
        ("zstd", ZSTD_BSD),
        ("Aileron font", "By Sora Sagano (dotcolon.net). Released under CC0 1.0 Universal: dedicated to the public domain."),
        ("Material Symbols icons", "Copyright Google LLC. Licensed under the Apache License, Version 2.0, below."),
        (".NET runtime", read(mono, "LICENSE.TXT") + "\n\n" + read(mono, "THIRD-PARTY-NOTICES.TXT")),
        (".NET for Android", read(android, "LICENSE.TXT") + "\n\n" + read(android, "THIRD-PARTY-NOTICES.TXT")),
        ("Apache License 2.0", apache),
    ]

    rule = "=" * 72
    text = "\n\n".join(f"{rule}\n{name}\n{rule}\n\n{body}" for name, body in sections) + "\n"
    open(OUT, "w", encoding="utf8", newline="\n").write(text)
    for name, body in sections:
        print(f"{len(body):8} {name}")
    print(f"{len(text)} bytes in {OUT}")


if __name__ == "__main__":
    main()
