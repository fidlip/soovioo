#!/usr/bin/env python3
"""Tells whether the RVPs changed since rvp-programs.json was built.

Run monthly by .github/workflows/rvp-sources.yml. Compares
- the vocational fields: the catalog built anew against the one in the repository,
- the framework plans written out by hand in build_rvp_catalog.py: a hash of
  their text against rvp-sources.json,
- the ministry's pages listing RVPs: their links against rvp-sources.json.

    backend/tools/check_rvp_sources.py --cache /tmp/rvp --report /tmp/rvp-report.md

The report is written only when something differs. After dealing with the
changes, --update stores the sources' current state.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

sys.dont_write_bytecode = True

import build_rvp_catalog as catalog  # noqa: E402

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASELINE = os.path.join(TOOLS, "rvp-sources.json")
EDU = "https://edu.gov.cz/dokumenty/rvp-ramcove-vzdelavaci-programy/"

PLANS = [
    dict(name="Revidovaný RVP ZV", url="https://prohlednout.rvp.cz/export/pdf?rvp_type_id=6&mandatory_part_only=true",
         start="RUP pro základní vzdělávání závazně stanovuje", end="Pojetí hodnocení žáků"),
    dict(name="RVP G", url="https://digitalizace.rvp.cz/files/rvp-g-7-ramcovy-ucebni-plan.pdf"),
    dict(name="RVP GSP", url="https://digitalizace.rvp.cz/files/rvp-gsp-7-ramcovy-ucebni-plan.pdf"),
]
PAGES = [
    dict(name="Stránka MŠMT s RVP pro gymnázia", url=EDU + "rvp-g-ramcove-vzdelavaci-programy-pro-gymnazia/"),
    dict(name="Stránka MŠMT s RVP ZV", url=EDU + "rvp-zv-ramcovy-vzdelavaci-program-pro-zakladni-vzdelavani/"),
    dict(name="Stránka MŠMT s RVP SOV", url=EDU + "rvp-sov-ramcove-vzdelavaci-programy-stredniho-odborneho-vzdelavani/"),
]


def plan_hash(plan, cache):
    """Of the plan's text, without what moves with the rest of the document."""
    path = os.path.join(cache, catalog.slug(plan["name"]) + ".pdf")
    with open(path, "wb") as file:
        file.write(catalog.download(plan["url"]))
    text = subprocess.run(["pdftotext", "-layout", path, "-"], check=True, capture_output=True).stdout.decode("utf-8")
    if "start" in plan:
        first = text.rfind(plan["start"])
        last = text.find(plan["end"], first)
        if first < 0 or last < 0:
            raise ValueError("v textu chybí očekávaná kapitola")
        text = text[first:last]
    text = re.sub(r"\b\d+/\d+\b", " ", text)
    return hashlib.sha256(" ".join(text.split()).encode("utf-8")).hexdigest()


def page_links(page):
    """Documents the page links to, and pages under the RVP section."""
    html = catalog.download(page["url"]).decode("utf-8", errors="ignore")
    links = re.findall(r"https?://[^\"'\s<>]+", html)
    return sorted({link for link in links if link.endswith(".pdf") or "/rvp-ramcove-vzdelavaci-programy/" in link})


def read_sources(cache):
    """Current state by source, and what couldn't be read."""
    state, errors = {}, []
    for plan in PLANS:
        try:
            state[plan["name"]] = plan_hash(plan, cache)
        except (OSError, ValueError, subprocess.CalledProcessError) as error:
            errors.append(f"{plan['name']} ({plan['url']}): {error}")
    for page in PAGES:
        try:
            state[page["name"]] = page_links(page)
        except OSError as error:
            errors.append(f"{page['name']} ({page['url']}): {error}")
    return state, errors


def compare_sources(state, baseline):
    urls = {source["name"]: source["url"] for source in PLANS + PAGES}
    lines = []
    for name, current in state.items():
        before = baseline.get(name)
        if current == before:
            continue
        if isinstance(current, list):
            added = sorted(set(current) - set(before or []))
            removed = sorted(set(before or []) - set(current))
            lines.append(f"- **{name}** ({urls[name]}): změnily se odkazy")
            lines += [f"  - nový: {link}" for link in added] + [f"  - zmizel: {link}" for link in removed]
        else:
            lines.append(f"- **{name}** ({urls[name]}): změnil se text rámcového učebního plánu")
    return lines


def compare_catalog(built, stored):
    built = {p["code"]: p for p in built}
    stored = {p["code"]: p for p in stored}
    lines = [f"- nový obor: {built[code]['name']}" for code in sorted(built.keys() - stored.keys())]
    lines += [f"- zmizel obor: {stored[code]['name']}" for code in sorted(stored.keys() - built.keys())]
    for code in sorted(built.keys() & stored.keys()):
        if built[code] != stored[code]:
            fields = [key for key in built[code] if built[code][key] != stored[code].get(key)]
            lines.append(f"- změněný program: {built[code]['name']} ({', '.join(fields)})")
    return lines


def report_text(catalog_lines, source_lines, errors):
    parts = ["Měsíční kontrola našla rozdíl proti tomu, co je v repozitáři. Může jít i o změnu formátu zdroje, ne jen obsahu.\n"]
    if catalog_lines:
        parts += ["## Katalog sestavený z PDF MŠMT\n", *catalog_lines, ""]
    if source_lines:
        parts += ["## Zdroje hlídané otiskem\n", *source_lines, ""]
    if errors:
        parts += ["## Nešlo přečíst\n", *[f"- {error}" for error in errors], ""]
    parts += [
        "## Co udělat\n",
        "1. Projít změny. Hodnoty RVP ZV, G a GSP jsou zapsané ručně v `backend/tools/build_rvp_catalog.py`.",
        "2. Přegenerovat katalog: `backend/tools/build_rvp_catalog.py --cache <adresář>`.",
        "3. Uložit stav zdrojů: `backend/tools/check_rvp_sources.py --cache <adresář> --update`.",
    ]
    return "\n".join(parts) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cache", required=True, help="directory for what's downloaded")
    parser.add_argument("--report", help="where to write the report, if something differs")
    parser.add_argument("--update", action="store_true", help="store the sources' current state")
    args = parser.parse_args()
    os.makedirs(args.cache, exist_ok=True)

    state, errors = read_sources(args.cache)
    if args.update:
        if errors:
            sys.exit("\n".join(errors))
        with open(BASELINE, "w", encoding="utf-8") as file:
            json.dump(state, file, ensure_ascii=False, indent=2)
            file.write("\n")
        return

    with open(BASELINE, encoding="utf-8") as file:
        source_lines = compare_sources(state, json.load(file))
    catalog_lines = []
    try:
        with open(catalog.DEFAULT_OUTPUT, encoding="utf-8") as file:
            stored = json.load(file)["programs"]
        catalog_lines = compare_catalog(catalog.build(args.cache), stored)
    except (OSError, ValueError, AssertionError, subprocess.CalledProcessError) as error:
        errors.append(f"Katalog z PDF MŠMT: {error}")

    if catalog_lines or source_lines or errors:
        text = report_text(catalog_lines, source_lines, errors)
        print(text)
        if args.report:
            with open(args.report, "w", encoding="utf-8") as file:
                file.write(text)
    else:
        print("Bez rozdílu.")


if __name__ == "__main__":
    main()
