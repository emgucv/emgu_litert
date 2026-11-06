#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Builds Emgu.LiteRT.LM.Chat/emoji_shortcodes.tsv.gz, the table behind `EmojiShortcodes` (":smile:" -> the emoji).

The names are iamcal/emoji-data's `short_names` (MIT; the names Slack, GitHub and Campfire use, aliases included), for
the emoji's fully qualified code points. One line per name: `name<TAB>emoji`, sorted, gzip-compressed without a
timestamp (the same input gives the same bytes). Skin tone variants (`name::skin-tone-2`) are not listed.

    python3 tools/make_emoji_shortcodes.py [path/to/emoji.json]

Without an argument it downloads emoji.json from the pinned commit below (needs network access). To move to a newer
Unicode version, change the commit, run it, and run the tests (UnitTestEmojiShortcodes).
"""

import gzip
import json
import os
import sys
import urllib.request

# iamcal/emoji-data, Unicode / Emoji 17.0 (2026-10-31).
COMMIT = "e77759c296ed37beb0dbd63908b43d8619509c6d"
URL = "https://raw.githubusercontent.com/iamcal/emoji-data/" + COMMIT + "/emoji.json"

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
OUTPUT = os.path.join(REPO, "Emgu.LiteRT.LM.Chat", "emoji_shortcodes.tsv.gz")


def main():
    if len(sys.argv) > 1:
        with open(sys.argv[1], "rb") as f:
            data = json.load(f)
    else:
        with urllib.request.urlopen(URL) as response:
            data = json.load(response)

    table = {}
    for entry in data:
        emoji = "".join(chr(int(code, 16)) for code in entry["unified"].split("-"))
        for name in entry["short_names"]:
            name = name.lower()
            # The first entry wins if two emoji ever claim a name.
            table.setdefault(name, emoji)

    lines = "".join("%s\t%s\n" % (name, table[name]) for name in sorted(table))
    with open(OUTPUT, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, compresslevel=9, mtime=0) as out:
            out.write(lines.encode("utf-8"))
    print("%d names, %d bytes: %s" % (len(table), os.path.getsize(OUTPUT), OUTPUT))


if __name__ == "__main__":
    main()
