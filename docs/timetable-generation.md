# Rozvrh — automatické generování, verzování a plánování změn (analýza)

Stav: analýza probraná v [#17](https://github.com/fidlip/soovioo/issues/17) — generátor nad
CP-SAT, fáze jako sub-issues. Fáze 1 (verze rozvrhu,
[#159](https://github.com/fidlip/soovioo/issues/159)) je hotová podle návrhu níže; navíc
stav **Archivovaná** (verze zahozená z platnosti) a `ValidTo` se dopočítává z následující
zveřejněné verze, ručně se nezadává.

## Dnešní stav

- `TimetableEntry` = jedna hodina: třída × den × číslo hodiny × sudý/lichý týden,
  předmět, volitelně učitel a učebna. Rozvrh platí „navždy“ — žádná platnost od–do.
- `SchoolPeriod` = časy hodin (0–15, nultá hodina).
- Kolize učitele/učebny se hlásí jako varování (neblokují uložení).
- Rozvrh se zadává ručně buňku po buňce.

## Co chceme

1. **Generování**: ze zadaných úvazků a omezení navrhnout celý rozvrh školy
   (všechny třídy najednou), ručně ho doladit a teprve pak zveřejnit.
2. **Verzování**: rozvrh má platnost (např. 1. 9. – 31. 1., od 1. 2. jiný), stará verze
   zůstane k nahlédnutí (docházka a známky z minulosti se k ní vztahují).
3. **Plánování změn**: připravit novou verzi dopředu (koncept), zveřejnit ji k datu.
   Jednorázové změny (suplování, výměna hodin na jeden den) jsou **jiná agenda**
   (suplování) — mimo tento dokument, jen s ním počítat v modelu.

## Vstupy pro generátor

| Vstup | Příklad | Dnes v datech? |
|---|---|---|
| Úvazky: třída × předmět × hodin týdně × učitel | 5.A, Matematika, 5 h, Novák | ne (jen ručně v rozvrhu) |
| Dělení tříd na skupiny | Aj ve 2 skupinách, Tv chlapci/dívky | ne |
| Dvouhodinovky / bloky | Pracovní činnosti 2 h v kuse | ne |
| Učebny a jejich typ | Tělocvična, počítačová | ano (`Classroom`, `ClassroomType`) |
| Předmět → požadovaný typ učebny | Tv jen v tělocvičně | částečně (`Subject.DefaultClassroomId`) |
| Dostupnost učitelů | Novák nemůže v pátek | ne |
| Časy hodin | 0.–8. hodina | ano (`SchoolPeriod`) |

### Tvrdá omezení (musí platit)

- učitel, třída/skupina i učebna nejvýš jednou v daném čase (s ohledem na sudý/lichý týden),
- splněný počet hodin každého úvazku,
- učebna správného typu a kapacity,
- dostupnost učitelů, max. počet hodin denně pro třídu (věk žáků — vyhláška).

### Měkká omezení (optimalizuje se)

- minimum „oken“ u tříd (u žáků ideálně nula) a u učitelů,
- rozložení předmětu do týdne (ne 3× matematika v jednom dni),
- náročné předměty spíš dopoledne,
- přání učitelů (volné dny, nejdřív/nejpozději),
- co nejméně přecházení mezi budovami/učebnami.

## Možnosti řešení

### A) Constraint solver v backendu — Google OR-Tools CP-SAT (doporučeno)

- NuGet `Google.OrTools`, běží přímo v .NET 10 (nativní knihovny pro linux-x64/arm64,
  win, osx), licence Apache 2.0.
- Model: booleovská proměnná „úvazek U má hodinu v čase T (a v učebně R)“, tvrdá
  omezení jako lineární podmínky, měkká jako penalizace v účelové funkci.
- Pro školu velikosti ZŠ (≈ 20 tříd, 40 učitelů, 35 h týdně) jde typicky o sekundy až
  jednotky minut — běh jako Hangfire job (už ho máme), UI ukazuje průběh a výsledek.
- Plusy: vše v našem kódu a datech, žádný externí proces, dobře se rozšiřuje o další
  pravidla. Minusy: modelování je práce (≈ desítky hodin), ladění měkkých vah.

### B) FET (Free Timetabling Software) jako externí engine

- Osvědčený školní generátor, umí většinu omezení výše, má CLI (`fet-cl`).
- Integrace: export úvazků do jeho XML, spuštění, import výsledku.
- Plusy: hotová optimalizace. Minusy: licence **AGPL** (při distribuci/SaaS nutno
  posoudit), externí binárka na serveru, převod formátů, horší kontrola nad chybami.

### C) UniTime

- Výkonný, ale určený pro univerzity, Java, velký systém — pro ZŠ/SŠ zbytečně těžký.

### D) Vlastní heuristika (greedy + lokální prohledávání)

- Bez závislostí, ale kvalita a rychlost výrazně horší než CP-SAT; nedoporučuju.

**Doporučení: A (CP-SAT)**, s FET jako záložní variantou, kdyby se modelování ukázalo
příliš drahé.

## Verzování a plánování — návrh modelu

```
TimetableVersion
  Id, SchoolId, Name ("1. pololetí 2026/27")
  ValidFrom (DateOnly), ValidTo (DateOnly?, null = do odvolání)
  Status: Draft | Published | Archived
  CreatedAt, PublishedAt, CreatedByUserId

TimetableEntry
  + TimetableVersionId   (nově povinné; stávající záznamy → jedna verze
                          "Výchozí", platná od data migrace)
```

- **Zobrazení rozvrhu** pro týden = verze, jejíž `ValidFrom ≤ pondělí ≤ ValidTo`
  (a Published). Týdenní navigace v rozvrhu tak automaticky ukáže správnou verzi.
- **Koncept**: nová verze `Draft` jako kopie aktuální (nebo výsledek generátoru),
  edituje se mimo ostrý rozvrh, kolize se počítají jen v rámci verze.
- **Zveřejnění k datu**: `Published` s `ValidFrom` v budoucnu; předchozí verzi se nastaví
  `ValidTo = ValidFrom − 1 den`. Žádný plánovač není potřeba — rozhoduje datum.
- **Docházka** (zameškané hodiny) a **Home „dnes“** čtou verzi platnou k danému dni.
- **Suplování** později jako samostatná vrstva „výjimek“ nad verzí (den × hodina ×
  třída → náhradní učitel/předmět/učebna/odpadá), verze se nemění.

## Úvazky — návrh modelu (vstup generátoru)

Ve fázi 2 (#160) jsou úvazky rozdělené na vstupy, které škola zadává, a přidělení,
které z nich vychází:

```
TeacherProfile (Osoby → učitel)
  + Qualifications: TeacherQualification (SubjectId, GradeFrom?, GradeTo?)  — aprobace
  + QualificationNote (dosavadní volný text aprobace)
  + ContractedHours (nasmlouvané hodiny týdně), TeachingDays (dny, kdy může učit)

CurriculumItem (učební plán, u verze rozvrhu)
  TimetableVersionId, Grade (ročník), SubjectId, HoursPerWeek

TeachingAssignment (přidělení výuky, u verze rozvrhu)
  TimetableVersionId, SchoolClassId, SubjectId, TeacherId
```

- Třída × předmět bez přidělení dostane učitele automaticky (`TeachingPlanner`): z
  učitelů s aprobací pro předmět a ročník, přednost má ten, kdo to ve třídě učí v
  rozvrhu, pak kdo se vejde do nasmlouvaných hodin, třídní, kdo už ve třídě učí, kdo
  má nejvíc volných hodin. Automatické přidělení jde potvrdit nebo změnit.
- Kontroly: plán vs. rozvrh (hodina jen v lichém/sudém týdnu = 0,5 h), jiný učitel
  v rozvrhu než přidělený, chybějící aprobace, přidělené vs. nasmlouvané hodiny,
  hodiny v den, kdy učitel učit nemůže.
- Pro generátor (fáze 3) je to vstup „kdo, co, kolik“; generátor řeší jen „kdy“.

Ve fázi 4 přibude:

```
TeachingAssignment + GroupName? ("sk. 1"), BlockLength, RequiredClassroomType?, PreferredClassroomId?

TeacherAvailability
  TeacherId, DayOfWeek, PeriodNumber, Kind: Unavailable | Avoid
```

## Navržené fáze

1. **Verze rozvrhu** (model, migrace, výběr verze podle týdne, koncept + zveřejnění).
   Přínos i bez generátoru: příprava rozvrhu na 2. pololetí dopředu.
2. **Učební plán, aprobace a přidělení výuky** (Rozvrh → Přidělení výuky, kontroly
   pokrytí rozvrhu a nasmlouvaných hodin).
3. **Generátor CP-SAT** pro koncept verze: tvrdá omezení → první použitelná verze;
   pak měkká omezení a váhy nastavitelné školou; výsledek vždy jen jako koncept.
4. **Dostupnost učitelů a přání**, dělení na skupiny, bloky.
5. (Samostatně) **Suplování** nad verzemi.

## Rizika a otevřené otázky

- Kvalita dat: generátor je jen tak dobrý jako úvazky — bez úvazků nic nevygeneruje.
- Skupiny a dělené hodiny výrazně zvětšují model; začít bez nich.
- Měkká omezení: priority se liší škola od školy → nastavitelné váhy, rozumné výchozí.
- Licence FET (AGPL), pokud by se volila varianta B.
- Výkon: CP-SAT běží v procesu API — pro větší SŠ zvážit samostatný worker / limit času
  řešení (např. 2 min) a vrácení nejlepšího nalezeného.
