#!/usr/bin/env python3
# Builds Resources/Raw/places.tsv.gz, the offline place list photo search uses to name where a photo was taken
# (Emgu.LiteRT.LM.Agents' PlaceLookup): GeoNames' towns of 5,000+ people with their region and country names.
#
# Data: GeoNames (https://www.geonames.org), licensed under Creative Commons Attribution 4.0
# (https://creativecommons.org/licenses/by/4.0/): cities5000.zip, admin1CodesASCII.txt and countryInfo.txt from
# https://download.geonames.org/export/dump/.
#
# Format (UTF-8, tab-separated, gzip): "C <tab> country code <tab> country name", "A <tab> CC.admin1 <tab> region name",
# then "P <tab> name <tab> latitude <tab> longitude <tab> country code <tab> admin1 code <tab> population" per town,
# coordinates to 4 decimals (~10 m).
#
# Usage: tools/make_places.py [download folder]
import gzip, io, os, sys, urllib.request, zipfile

BASE = 'https://download.geonames.org/export/dump/'
folder = sys.argv[1] if len(sys.argv) > 1 else '.'
os.makedirs(folder, exist_ok=True)

def fetch(name):
    path = os.path.join(folder, name)
    if not os.path.exists(path):
        urllib.request.urlretrieve(BASE + name, path)
    return path

countries = {}
for line in open(fetch('countryInfo.txt'), encoding='utf-8'):
    if line.startswith('#'):
        continue
    f = line.rstrip('\n').split('\t')
    if len(f) > 4:
        countries[f[0]] = f[4]

regions = {}
for line in open(fetch('admin1CodesASCII.txt'), encoding='utf-8'):
    f = line.rstrip('\n').split('\t')
    if len(f) > 1:
        regions[f[0]] = f[1]

# Left out: sections of a town (PPLX - e.g. Paris's arrondissements, "Paris 16 Passy", which a photo at the Eiffel
# Tower would otherwise be placed in), and historical, abandoned or destroyed places.
SKIPPED = {'PPLX', 'PPLH', 'PPLQ', 'PPLW'}
towns = []
with zipfile.ZipFile(fetch('cities5000.zip')) as z:
    for line in io.TextIOWrapper(z.open('cities5000.txt'), encoding='utf-8'):
        f = line.rstrip('\n').split('\t')
        if f[7] in SKIPPED:
            continue
        # name, latitude, longitude, country code, admin1 code, population
        towns.append((f[1], round(float(f[4]), 4), round(float(f[5]), 4), f[8], f[10], int(f[14] or 0)))

used_regions = {t[3] + '.' + t[4] for t in towns}
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Resources', 'Raw', 'places.tsv.gz')
with gzip.open(out, 'wt', encoding='utf-8', compresslevel=9) as g:
    for code, name in sorted(countries.items()):
        g.write('C\t%s\t%s\n' % (code, name))
    for code, name in sorted(regions.items()):
        if code in used_regions:
            g.write('A\t%s\t%s\n' % (code, name))
    for name, lat, lon, cc, a1, pop in towns:
        g.write('P\t%s\t%g\t%g\t%s\t%s\t%d\n' % (name, lat, lon, cc, a1, pop))
print('%d towns, %d regions, %d countries -> %s (%d bytes)' % (len(towns), len(used_regions), len(countries), os.path.normpath(out), os.path.getsize(out)))
