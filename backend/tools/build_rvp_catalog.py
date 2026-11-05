#!/usr/bin/env python3
"""Builds Timetable/Rvp/rvp-programs.json, the catalog of rámcové učební plány.

Vocational fields are read from the ministry's PDFs, the rest is written out
below from the texts of the RVPs. Needs Python 3 and pdftotext (poppler-utils):

    backend/tools/build_rvp_catalog.py --cache /some/temp/dir

The cache keeps the downloaded PDFs (about 180 MB), the repository doesn't.
What doesn't add up is printed: look at each such table, the PDFs' layout
differs and a few needed fixing by hand (see fix_rows).
"""
import argparse
import collections
import concurrent.futures
import glob
import json
import os
import re
import subprocess
import time
import unicodedata
import urllib.request

SOV_DATABASE = (
    "https://edu.gov.cz/dokumenty/rvp-ramcove-vzdelavaci-programy/"
    "rvp-sov-ramcove-vzdelavaci-programy-stredniho-odborneho-vzdelavani/databaze-rvp-sov-od-1-9-2023/"
)
DEFAULT_OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Soovioo.Api", "Timetable", "Rvp", "rvp-programs.json")

ELEMENTARY, SECONDARY = "Elementary", "Secondary"


def slug(text):
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode().lower()
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-")


def area(name, hours, kind=None, alt=None):
    result = dict(key=slug(name), name=name, hours=hours)
    if kind:
        result["kind"] = kind
    if alt:
        result["alt"] = alt
    return result


def stage(first, last, label, total, disponible, areas, total_max=None):
    result = dict(fromGrade=first, toGrade=last, label=label, total=total, disponible=disponible, areas=areas)
    if total_max:
        result["totalMax"] = total_max
    return result


NO_RULES = dict(englishFrom=None, secondFrom=None, peMin=None)

# RVP ZV: rámcový učební plán 2021; weeks by grade (§ 26 školského zákona).
ELEMENTARY_WEEKS = [(1, 2, 18, 22), (3, 5, 22, 26), (6, 7, 28, 30), (8, 9, 30, 32)]
ZV_FIRST = [
    area("Český jazyk a literatura", 33, "czech"),
    area("Cizí jazyk", 9, "foreign"),
    area("Matematika a její aplikace", 20, "math"),
    area("Informatika", 2, "informatics"),
    area("Člověk a jeho svět", 11, "world"),
    area("Umění a kultura", 12, "arts"),
    area("Člověk a zdraví", 10, "health"),
    area("Člověk a svět práce", 5, "work"),
]
ZV_SECOND = [
    area("Český jazyk a literatura", 15, "czech"),
    area("Cizí jazyk", 12, "foreign"),
    area("Další cizí jazyk", 6, "second"),
    area("Matematika a její aplikace", 15, "math"),
    area("Informatika", 4, "informatics"),
    area("Člověk a společnost", 10, "society"),
    area("Člověk a příroda", 20, "nature"),
    area("Umění a kultura", 9, "arts"),
    area("Člověk a zdraví", 10, "health"),
    area("Člověk a svět práce", 3, "work"),
]
assert sum(a["hours"] for a in ZV_FIRST) + 16 == 118
assert sum(a["hours"] for a in ZV_SECOND) + 18 == 122

# RVP G and RVP GSP: nature and society share one minimum there.
G_AREAS = [
    area("Český jazyk a literatura", 12, "czech"),
    area("Cizí jazyk", 12, "foreign"),
    area("Další cizí jazyk", 12, "second"),
    area("Matematika a její aplikace", 10, "math"),
    area("Člověk a příroda, Člověk a společnost", 36, "natureSociety"),
    area("Umění a kultura", 4, "arts"),
    area("Člověk a zdraví", 8, "health"),
    area("Informatika", 4, "informatics"),
    area("Volitelné vzdělávací aktivity", 8, "optional"),
]
GSP_AREAS = [area("Tělesná výchova a sportovní trénink", 64, "sport") if a["kind"] == "health" else a for a in G_AREAS]
assert sum(a["hours"] for a in G_AREAS) + 26 == 132
assert sum(a["hours"] for a in GSP_AREAS) + 22 == 184


def weeks_from(offset):
    """Elementary weeks in the grades of a school starting `offset` grades later."""
    weeks = []
    for first, last, least, most in ELEMENTARY_WEEKS:
        if last - offset >= 1:
            weeks.append(dict(fromGrade=max(first - offset, 1), toGrade=last - offset, min=least, max=most))
    return weeks


def general_programs():
    programs = []
    for version, label, rules in (
        ("2021", "dosavadní RVP ZV", dict(englishFrom=3, secondFrom=8, peMin=None)),
        ("2025", "revidovaný RVP ZV", dict(englishFrom=3, secondFrom=7, peMin=2)),
    ):
        # The revised RVP has no minima of areas, no disponibilní hours to count.
        revised = version == "2025"
        first, second = ([], []) if revised else (ZV_FIRST, ZV_SECOND)
        first_free, second_free = (None, None) if revised else (16, 18)
        programs.append(dict(
            code="ZV" + version, name=f"Základní vzdělávání ({label})", family="ZV", schoolType=ELEMENTARY,
            stages=[stage(1, 5, "1. stupeň", 118, first_free, first), stage(6, 9, "2. stupeň", 122, second_free, second)],
            weekly=weeks_from(0), rules=rules, template="ZV", templateOffset=0))
        programs.append(dict(
            code=f"ZV{version}-G8", name=f"Nižší stupeň osmiletého gymnázia ({label})", family="ZV", schoolType=SECONDARY,
            stages=[stage(1, 4, "Prima až kvarta", 122, second_free, second)],
            weekly=weeks_from(5), rules={**rules, "englishFrom": None, "secondFrom": rules["secondFrom"] - 5},
            template="ZV", templateOffset=5))
        # Footnote 3 of the framework plan: 64 hours, no minima of their own.
        programs.append(dict(
            code=f"ZV{version}-G6", name=f"Nižší stupeň šestiletého gymnázia ({label})", family="ZV", schoolType=SECONDARY,
            stages=[stage(1, 2, "Nižší stupeň", 64, None, [])],
            weekly=weeks_from(7), rules={**rules, "englishFrom": None, "secondFrom": None},
            template="ZV", templateOffset=7))
    for prefix, label, areas, disponible, total, total_max, least, most in (
        ("G", "gymnázium", G_AREAS, 26, 132, 140, 27, 35),
        ("GSP", "gymnázium se sportovní přípravou", GSP_AREAS, 22, 184, None, 46, 46),
    ):
        for code, name, first in (
            (prefix + "4", f"Čtyřleté {label}", 1),
            (prefix + "-V8", f"Vyšší stupeň osmiletého – {label}", 5),
            (prefix + "-V6", f"Vyšší stupeň šestiletého – {label}", 3),
        ):
            programs.append(dict(
                code=code, name=name, family="G", schoolType=SECONDARY,
                stages=[stage(first, first + 3, "Celé studium", total, disponible, areas, total_max)],
                weekly=[dict(fromGrade=first, toGrade=first + 3, min=least, max=most)],
                rules=NO_RULES, template="GEN", templateOffset=0))
    return programs


def download(url):
    """The content of `url`; the ministry's sites fail now and then."""
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return response.read()
        except OSError:
            if attempt == 2:
                raise
            time.sleep(15)


def fetch(url, path):
    if not os.path.exists(path) or os.path.getsize(path) == 0:
        content = download(url)
        with open(path, "wb") as file:
            file.write(content)


def download_vocational(cache):
    """The ministry's PDFs as text, in cache/txt/<code>.txt."""
    os.makedirs(os.path.join(cache, "pdf"), exist_ok=True)
    os.makedirs(os.path.join(cache, "txt"), exist_ok=True)
    database = os.path.join(cache, "db.html")
    fetch(SOV_DATABASE, database)
    with open(database, encoding="utf-8", errors="ignore") as file:
        links = sorted(set(re.findall(r"https?://[^\"'\s]+/rvpsov/ciste/[^\"'\s]+\.pdf", file.read())))

    def one(link):
        name = os.path.basename(link)[:-4]
        pdf = os.path.join(cache, "pdf", name + ".pdf")
        text = os.path.join(cache, "txt", name + ".txt")
        fetch(link, pdf)
        if not os.path.exists(text):
            subprocess.run(["pdftotext", "-layout", pdf, text], check=True, stderr=subprocess.DEVNULL)

    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        list(pool.map(one, links))
    print(len(links), "PDFs")


ROW = re.compile(r"^(.*?)\s{2,}(\d[\d,]*)\s{2,}(\d[\d ]*\d|\d)\s*$")
ONLY_NUMBERS = re.compile(r"^\s*(\d[\d,]*)\s{2,}(\d[\d ]*\d|\d)\s*$")
HEADER = ("Minimální počet", "Vzdělávací oblasti a", "týdenních", "Týdenních")
NOT_A_ROW = ("okruhy", "vzdělávání", "celkový")


def read_tables(lines):
    """Tables "Rámcové rozvržení obsahu vzdělávání": (form, rows, disponible, total)."""
    heads = [
        i for i, line in enumerate(lines)
        if "Rámcové rozvržení obsahu vzdělávání" in line and not line.rstrip().endswith(".")
        and any(re.search(r"Délka a forma vzdělávání\s*:|Minimální počet", x) for x in lines[i:i + 6])
    ]
    tables = []
    for head in heads:
        form = next((x.split(":", 1)[1].strip() for x in lines[head:head + 6] if "Délka a forma" in x and ":" in x), "")
        rows, pending, total, disponible = [], [], None, None
        for line in lines[head + 1:head + 80]:
            text = line.strip()
            if re.match(r"^Poznámky", text):
                break
            if not text or text.startswith(HEADER) or "Délka a forma" in text or text in NOT_A_ROW or re.fullmatch(r"\d+", text):
                continue
            with_label, numbers = ROW.match(line.rstrip()), ONLY_NUMBERS.match(line.rstrip())
            if not (with_label or numbers):
                pending.append(text)
                continue
            label, week = (with_label.group(1).strip(), with_label.group(2)) if with_label else ("", numbers.group(1))
            label = re.sub(r"\s+", " ", " ".join(pending + [label])).strip()
            pending = []
            # The end of a label wrapped onto the line of the next one.
            wrapped = re.match(r"(.*?)\s*(Disponibiln.*|Celkem.*)$", label)
            if wrapped and wrapped.group(1) and rows:
                rows[-1] = (rows[-1][0] + " " + wrapped.group(1), rows[-1][1])
                label = wrapped.group(2)
            hours = float(week.replace(",", "."))
            if re.match(r"Disponibiln", label):
                disponible = hours
            elif re.match(r"Celkem", label):
                total = hours
                break
            else:
                rows.append((label, hours))
        if total is not None:
            tables.append((form, rows, disponible, total))
    return tables


def clean_rows(code, rows):
    """Labels without what pdftotext glued to them: [label, hours, is an alternative]."""
    result, alternative = [], False
    for label, hours in rows:
        if re.match(r"(zaměření:|Týdenních)", label) and "okruhy" in label:
            label = re.sub(r"^.*?okruhy\b:?\s*", "", label)
        label = re.sub(r"^zaměření:.*?(Jazykové vzdělávání)", r"\1", label)
        is_alternative = bool(re.match(r"Profil\w* (obsahové )?okruhy", label))
        label = re.sub(r"^Profil\w* (obsahové )?okruhy:?\s*", "", label)
        label = re.sub(r"^Společný obsahový okruh\s*", "", label)
        label = re.sub(r"^Okruhy týdenních Celkový\s*", "", label, flags=re.I)
        label = re.sub(r"^Jazykové vzdělávání( a komunikace)?\d*\s*-?\s*", "", label)
        label = re.sub(r"^[-−–]\s*", "", label)
        label = re.sub(r"\d+$", "", label).strip()
        label = re.sub(r"^.*?(Opatření ministra|dodatek k Rámcovému).*?\)\.\s*", "", label)
        label = re.sub(r"\s*\(Pozn\.? ?\d*\)", "", label).strip()
        if not label:
            if code != "39-41-H01":
                continue
            label, is_alternative = "Profilující obsahový okruh", False
        label = label[0].upper() + label[1:]
        # Alternatives follow their heading, all with the same hours.
        is_alternative = bool(is_alternative or (alternative and result and result[-1][1] == hours))
        result.append([label, hours, is_alternative])
        alternative = is_alternative
    return result


def fix_rows(code, rows):
    """Tables whose layout loses rows in pdftotext, as the PDFs read."""
    czech, foreign = ["Český jazyk", 3, False], ["Cizí jazyk", 6, False]
    maths, social = ["Matematické vzdělávání", 6, False], ["Společenskovědní vzdělávání", 3, False]
    if code == "37-42-L51":
        kept = ("Estetické vzdělávání", "Vzdělávání pro zdraví", "Informatické vzdělávání", "Logistika a finančnictví", "Ekonomika")
        return [czech, foreign, maths] + [r for r in rows if r[0] in kept]
    if code == "26-57-H01":
        return [czech, foreign, social] + [r for r in rows if not r[0].startswith(("Český", "Cizí"))]
    if code == "41-45-L51":
        return [czech, foreign, maths] + [r for r in rows if not re.match(r"(Český|Cizí|Jazyk|Matemat)", r[0])]
    if code == "65-51-H01":
        return [czech, foreign] + [r for r in rows if not re.match(r"(Český|Cizí|Jazyk)", r[0])]
    if code == "28-45-L51":
        return [r for r in rows if not r[0].startswith("Technologie zpracování keramiky")] + [
            ["Technologie zpracování keramiky", 8, True], ["Technologie zpracování skla a bižuterie", 8, True]]
    return rows


KINDS = [
    (r"^český jazyk", "czech"), (r"^dva cizí jazyky", "foreignTwo"), (r"^cizí jazyk", "foreign"),
    (r"^společenskovědní", "society"), (r"^občanský vzdělávací", "society"), (r"^přírodovědné", "nature"),
    (r"^matematické", "math"), (r"^estetické", "arts"), (r"^vzdělávání pro zdraví", "health"),
    (r"^informatické", "informatics"), (r"^ekonomické", "economic"),
]


def vocational_programs(cache):
    programs, without_table, not_adding_up = [], [], []
    for path in sorted(glob.glob(os.path.join(cache, "txt", "*.txt"))):
        code = os.path.basename(path)[:-4]
        with open(path, encoding="utf-8") as file:
            text = file.read().replace("\f", "\n")
        tables = read_tables(text.split("\n"))
        if not tables:
            without_table.append(code)
            continue
        for form, rows, disponible, total in tables:
            found = re.search(r"\d\d\s*[–-]\s*\d\d\s*[–-]\s*[A-Z]{1,2}/\d\d\s+(.+(?:\n\s+[^\n]{3,60})?)", text)
            name = re.sub(r"\s+", " ", found.group(1)).strip() if found else code
            name = re.split(r"\s+(Obsah|Schváleno|Č\. ?j\.)", name)[0].strip()
            name = re.sub(r"\s*Rámcový vzdělávací.*$", "", name)
            name = re.sub(r"\s*\d+$", "", name)

            rows = fix_rows(code, clean_rows(code, rows))
            if code == "53-41-M04":
                total = 128  # "12" and "8" on two lines of the PDF.
            areas = []
            for label, hours, alternative in rows:
                if re.search(r"\d", label) or len(label) > 70:
                    print("look at the label:", code, label[:150])
                kind = next((k for pattern, k in KINDS if re.match(pattern, label.lower())), None)
                areas.append(area(label, hours, kind, "profil" if alternative else None))
            assert len({a["key"] for a in areas}) == len(areas), (code, areas)

            alternatives = [a["hours"] for a in areas if a.get("alt")]
            hours = sum(a["hours"] for a in areas if not a.get("alt")) + max(alternatives, default=0) + (disponible or 0)
            if abs(hours - total) > 0.01:
                not_adding_up.append((code, hours, total))

            years = int(re.match(r"\d+", form).group()) if form else round(total / 32)
            note = re.search(r"hodin\s+týdně\s+je\s+minimálně\s+(\d+)", re.sub(r"\s+", " ", text))
            week = dict(fromGrade=1, toGrade=years, min=int(note.group(1)) if note else 29)
            if total / years <= 35:
                week["max"] = 35
            full = f"{code[:2]}-{code[3:5]}-{code[6:-2]}/{code[-2:]}"
            programs.append(dict(
                code=full, name=f"{full} {name}", family="SOV", schoolType=SECONDARY,
                stages=[stage(1, years, "Celé studium", total, disponible, areas)], weekly=[week],
                rules=NO_RULES, template="GEN", templateOffset=0))
    print("without a table the script reads:", without_table)
    print("not adding up (in the text of the RVP if not fixed above):", not_adding_up)
    return programs


def build(cache):
    download_vocational(cache)
    programs = general_programs() + vocational_programs(cache)
    assert len({p["code"] for p in programs}) == len(programs)
    return programs


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cache", required=True, help="directory for the downloaded PDFs")
    parser.add_argument("--output", default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    programs = build(args.cache)
    with open(args.output, "w", encoding="utf-8") as file:
        json.dump(dict(programs=programs), file, ensure_ascii=False, separators=(",", ":"))
    print(len(programs), "programs:", collections.Counter(p["family"] for p in programs))


if __name__ == "__main__":
    main()
