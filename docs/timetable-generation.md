# Rozvrh — automatické generování, verzování a plánování změn (analýza)

Stav: analýza probraná v [#17](https://github.com/fidlip/soovioo/issues/17) — generátor nad
CP-SAT, fáze jako sub-issues. Fáze 1 (verze rozvrhu,
[#159](https://github.com/fidlip/soovioo/issues/159)) je hotová podle návrhu níže; navíc
stav **Archivovaná** (verze zahozená z platnosti) a `ValidTo` se dopočítává z následující
zveřejněné verze, ručně se nezadává. Zveřejněnou verzi, která ještě neplatí, jde archivovat
nebo vrátit mezi koncepty (zrušit zveřejnění); verze, která platí nebo platila, zůstává —
odkazuje na ni minulá docházka. Před zveřejněním dialog ukáže problémy rozvrhu
(`TimetableCheck`: kolize třídy, učitele a učebny, hodiny proti učebnímu plánu a přidělení,
výuka ve dny, kdy učitel nemůže, hodiny bez učebny); zveřejnit jde i tak.

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
| Dělení tříd na skupiny | Aj ve 2 skupinách, Tv chlapci/dívky | ano (`StudentGroup`, `GroupDivision`, #166) |
| Dvouhodinovky / bloky | Pracovní činnosti 2 h v kuse | ano (`Subject.DoubleLessons`, přepis v učebním plánu) |
| Učebny a jejich typ | Tělocvična, počítačová | ano (`Classroom`, `ClassroomType`) |
| Předmět → učebny / typ učebny | Tv v tělocvičně, jinak jakékoli tělocvičně | ano (`Subject.Classrooms`) |
| Třída → kmenová učebna | 5.A v učebně 101 | ano (`SchoolClass.HomeroomId`) |
| Dostupnost učitelů | Novák nemůže v pátek | ano (`TeacherProfile.Availability`, přání) |
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
- **Suplování** (#215) je samostatná vrstva změn nad verzí, verze se nemění, viz níže.

## Úvazky — návrh modelu (vstup generátoru)

Ve fázi 2 (#160) jsou úvazky rozdělené na vstupy, které škola zadává, a přidělení,
které z nich vychází:

```
TeacherProfile (Osoby → učitel)
  + Qualifications: TeacherQualification (SubjectId, GradeFrom?, GradeTo?)  — aprobace
  + QualificationNote (dosavadní volný text aprobace)
  + ContractedHours (nasmlouvané hodiny týdně)
  + Availability (#162): den × hodina → nemůže / raději ne; celý den = Period null
  + přání (#162): FreeDays, Min/MaxLessonsPerDay, MaxGapsPerDay — poslední tři s výchozí
    hodnotou školy (School.TeacherMin/MaxLessonsPerDay, TeacherMaxGapsPerDay)

CurriculumItem (učební plán, u verze rozvrhu)
  TimetableVersionId, Grade (ročník), SubjectId, HoursPerWeek

ClassCurriculumItem (odchylka třídy od plánu ročníku, 0 h = třída předmět nemá)
  TimetableVersionId, SchoolClassId, SubjectId, HoursPerWeek

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
- Skupiny žáků (i napříč třídami), viz níže „Skupiny žáků“.
- Pro generátor (fáze 3) je to vstup „kdo, co, kolik“; generátor řeší jen „kdy“.

Ve fázi 4 (#162) přibyla dostupnost a přání učitelů (viz výše) a dvouhodinovky:
`Subject.DoubleLessons`, přepis `CurriculumItem.DoubleLessons` (ročník) a
`ClassCurriculumItem.DoubleLessons` (třída; řádek třídy má teď hodiny i bloky volitelné,
zmizí, když neříká nic vlastního).

## Skupiny žáků (#166)

Administrace → Výuka → Skupiny; vše patří verzi rozvrhu a kopíruje se s ní
(skupiny, dělení, členství i vazba hodin na skupinu):

- `StudentGroup`: název, předmět, hodiny týdně (prázdné = podle učebního plánu jejích tříd;
  když se třídy liší nebo předmět v plánu nemají, hlásí to) a dvouhodinovky, učitel (prázdný =
  vybere ho systém jako u třídy, skupinám jednoho dělení různé učitele; stejný učitel dvou
  skupin dělení se hlásí), učebna (prázdná = podle předmětu; kmenová jen u skupiny jedné třídy),
  třídy a žáci z nich. Třída, jejíž je skupina předmětu, ten předmět jako celá třída nemá
  (řádek třídy ukazuje „Ve skupinách: …“, celotřídní hodiny předmětu hlásí jako problém).
- `GroupDivision` (dělení): skupiny stejných tříd se stejnými hodinami (prázdné = podle plánu
  tříd pro předměty skupin, musí se shodovat), vždy ve stejný čas
  (Tv 2.A+2.B dívky/chlapci v různých tělocvičnách, Nj | Fj). Žák je v dělení nejvýš
  v jedné skupině (do jiné ho jde přidat, až když ho z původní odeberete); kdo není v žádné, má volno (kontrola před
  zveřejněním ho vypíše).
- Dělení může učit **řádek učebního plánu** (`CoversSubjectId`) — zástupný předmět jako
  „Povinně volitelný předmět“, „Další cizí jazyk“ nebo „Seminář“, za který si žáci volí
  skutečné předměty skupin (Dramatická výchova | Domácnost, Nj | Fj). Bez vlastních hodin
  má dělení hodiny toho řádku; víc dělení jednoho řádku (semináře 3 h + 2 h) musí mít
  dohromady jeho hodiny. Řádek se tak počítá do týdne třídy i do RVP, sám se ale neučí:
  nedostane učitele ani hodiny v rozvrhu (ruční úprava rozvrhu ho nenabízí a hodinu celé
  třídy z něj API odmítne) a známku nebo úkol z něj API odmítne (dávají se z předmětu skupiny).
- Hodina skupiny je `TimetableEntry` se `StudentGroupId`, jedna za každou třídu skupiny
  (úprava a smazání platí pro všechny); rozvrh třídy ukáže v políčku hodiny všech jejích
  skupin, žák a rodič jen skupiny žáka, učitel hodinu jednou („Nj (7.A, 7.B)“).
- Generátor: samostatná skupina = jedna poptávka, která blokuje všechny své třídy; dělení =
  jedna poptávka s částí za každou skupinu (každá svůj učitel, předmět a učebna, počítá se
  kapacita učebny na počet žáků skupiny). Generátor tedy nedá do stejné doby dvě samostatné
  skupiny stejné třídy ani bez společných žáků — takové patří do dělení.
- Změnu členství, po které by žák měl ve stejnou dobu jinou hodinu (celé třídy nebo jiné své
  skupiny mimo dělení) nebo by se skupina nevešla do učebny některé své hodiny, API odmítne;
  kontroluje se jen přidávaný žák.
- Kdo skupinu učí, učí jen její žáky: rozsah „moje třídy“ (`GetTeacherClassIdsAsync`) dávají
  jen celotřídní hodiny a třídnictví, žáky (`GetTeacherStudentIdsAsync`) i členové jeho skupin.
  Známka jde zapsat žákovi v předmětu, který ho učitel učí ve třídě nebo ve skupině
  (`ReachesStudentSubjectAsync`); absence, chování, akce a platby žákům jeho skupin, hromadná
  zpráva a adresář třídy jen celé třídě, kterou učí. Žák a rodič mají v adresáři a kontaktech
  učitele svých skupin, ne cizích. Dialogy Známky, Docházka a Úkoly nabízejí jen to, kam
  oprávnění sahá (`GET /api/students/reach`), čipy hodin v absenci podle rozvrhu žáka.
- Úkol skupiny (`Homework.StudentGroupId`, třída pak prázdná) je pro její aktuální členy (nebo
  vybrané z nich), učitel ho zadá u „Nj (7.A, 7.B) — Německý jazyk“. Skupinu s úkoly nejde smazat.

## Učební plán podle RVP (#165)

- **Program třídy** (`SchoolClass.RvpProgramCode`, Administrace → Škola → Třídy): základní vzdělávání podle dosavadního nebo revidovaného RVP ZV, nižší stupeň víceletého gymnázia, RVP G a RVP GSP, nebo obor středního odborného vzdělávání. Přechod na revidovaný RVP ZV jde po ročnících s kohortou, proto program patří třídě, ne škole.
- **Nabídka podle typu školy**: ZŠ vidí jen základní vzdělávání, SŠ gymnázia (i jejich nižší stupně) a obory SOŠ. Při změně typu školy se třídám zruší program, který pro nový typ neplatí.
- **Katalog** (`Timetable/Rvp/rvp-programs.json`): rámcové učební plány z textů MŠMT. Ročníky jsou ročníky školy, takže nižší stupeň víceletého gymnázia začíná 1. ročníkem. Revidovaný RVP ZV minima oblastí nemá, závazný je součet 118/122 h, týdenní rozsah, angličtina, další cizí jazyk a Tv alespoň 2 h týdně.
- **RVP SOV**: 271 oborů z [databáze MŠMT platné od 1. 9. 2023](https://edu.gov.cz/dokumenty/rvp-ramcove-vzdelavaci-programy/rvp-sov-ramcove-vzdelavaci-programy-stredniho-odborneho-vzdelavani/databaze-rvp-sov-od-1-9-2023/), vytěženo z PDF tabulek Rámcové rozvržení obsahu vzdělávání skriptem `backend/tools/build_rvp_catalog.py`, který katalog vygeneruje celý (ruční opravy tabulek jsou v něm popsané). Mimo katalog je 82-44-M/P01, který má hodiny v rozpětí podle zaměření. U 69-41-L/51 a 75-41-M/01 v textu RVP řádky nedávají uvedený součet, katalog je přebírá beze změny.
- **Změny RVP**: workflow `rvp-sources.yml` jednou za měsíc spustí `backend/tools/check_rvp_sources.py`. Znovu sestaví katalog oborů SOV a porovná ho s uloženým, u ručně zapsaných RVP ZV, G a GSP hlídá otisk textu a na stránkách MŠMT seznam odkazů. Při rozdílu založí issue, nebo ke stávajícímu přidá komentář. Dosavadní RVP ZV se nehlídá, je zrušený a dobíhá. Katalog a otisky (`rvp-sources.json`) aktualizuje vývojář.
- **Kontrola** (Výuka → Učební plán, kontrola před zveřejněním): počítá plán ročníků tak, jak je ve verzi teď; v ročníku rozhoduje třída programu s nejmenším počtem hodin. Stupeň s chybějícím ročníkem kontroluje jen týdenní rozsah. Předmět se k oblasti přiřadí podle názvu nebo zkratky, škola to může změnit.
- **Vyplnění** (jen v konceptu): RVP nedává hodiny po ročnících, takže návrh není oficiální údaj. U ZŠ je to typický plán, jinde se minima rozloží rovnoměrně. Co se stane s dosavadními hodinami a předměty, volí uživatel v dialogu.

## Generátor (fáze 3, #161)

Administrace → Výuka → Rozvrh → koncept verze → **Generovat** (`TimetableGenerator` nad OR-Tools CP-SAT, běží jako
Hangfire job `TimetableGenerationJob` ve frontě `timetable` s jedním workerem — generuje se
vždy jen jeden rozvrh najednou; CP-SAT paralelně 8–16 vláken, na méně jádrech se střídají):

- Vstup: učební plán a přidělení výuky verze (i automaticky vybraní učitelé), dostupnost
  a přání učitelů, dvouhodinovky, časy hodin a učebny předmětu (`Subject.Classrooms`: seznam v pořadí
  priority, každá položka konkrétní učebna nebo „jakákoli“ daného typu; „jakákoli kmenová“ je
  kmenová učebna dané třídy a předmět bez seznamu ji má taky, třída bez ní nechá takový předmět
  bez učebny). Učebna s kapacitou
  menší než počet žáků třídy se pro třídu nepoužije; když se nevejde do žádné, výsledek to hlásí.
- Režimy: **Doplnit chybějící** (hodiny v konceptu zůstanou, přibude, co podle plánu
  chybí), **Přegenerovat vše** (zůstanou jen zamčené hodiny, `TimetableEntry.Locked`)
  a **Upravit podle změn** (#179, `TimetableUpdate`) po změně plánu, přidělení, dostupnosti
  nebo přání. Hodina, kdy učitel nemůže, se přesune a chybějící učitel nebo učebna se doplní
  vždy. Volby: **Přesuny** — *co nejméně přesunů* (všechny nezamčené hodiny jsou přesunutelné,
  odchod z dosavadního místa stojí 2, méně než nejslabší přání i požadavky tříd, takže se kvůli nim hodina přesunout může) nebo *jen dotčené hodiny*
  (ostatní jsou pevné; dotčené jsou i hodiny „raději ne“ a nad denním maximem učitele, volné dny,
  nejméně hodin a okna tahle volba neřeší); **Neshody s plánem nebo přidělením** (jiný učitel
  či učebna, přebytečná hodina, předmět mimo plán) — *upravit podle plánu* nebo *ponechat
  a upozornit*. Hodina, která zůstala na místě, si nechá učebnu, pokud ji předmět stále může
  mít; výsledek vypíše, co se změnilo. Hodiny v lichém/sudém týdnu se přesouvají, jen když je
  změna přímo týká.
- Tvrdá omezení: třída a učitel nejvýš jednou v čase, nikdy když učitel „nemůže“ (hodina
  i celý den), dvouhodinovka = dvě hodiny po sobě ve stejný den a učebně (lichá zbude
  samostatně; předmět → ročník → třída, každá úroveň může přepsat), třída nejvýš
  ⌈týdenní hodiny / 5⌉ + 1 hodin denně.
- Požadavky (váhy, porušení se hlásí místo nesplnitelného modelu): každá hodina umístěná
  (1000 za hodinu), volná učebna ze seznamu předmětu (200; souběžné hodiny ≤ volné učebny
  každé skupiny učeben), plný hlavní výukový blok třídy (100 za každou hodinu bloku bez hodiny
  pro celou třídu, viz níže), každý den aspoň ⌊týdenní hodiny / 5⌋ hodin (20 za chybějící),
  třída bez oken (10), 0. hodina (8), předmět nejvýš jednou denně (6, dvouhodinovka se počítá jednou).
- Hlavní výukový blok (#225): hodiny od–do, kdy má třída každý den výuku. Škola ho má povinně
  (výchozí 1.–4. hodina), ročník ho dědí ze školy, třída z ročníku, oba ho mohou přepsat
  (Výuka → Nastavení; `School.MainBlockFrom/To`, `GradeMainBlock`, `SchoolClass.MainBlockFrom/To`,
  globálně, ne per verze). Blok nahradil dřívější pravidlo „den začíná první hodinou“: den,
  který začíná až blokem pozdější než 1. hodina, není porušení, a okno je jen volná hodina mezi
  dvěma hodinami. Volitelná hodina (ne všichni žáci) blok nepokrývá, dělené skupiny ano. Váha 100
  je nad všemi přáními učitelů a okny, pod chybějící učebnou (200) a neumístěnou hodinou (1000).
  Má-li třída méně hodin pro celou třídu, než blok za týden potřebuje, výsledek to hlásí („5.A:
  hodin pro celou třídu je 18 týdně, hlavní výukový blok (1.–4. hodina) jich potřebuje 20 …“)
  a chybějící hodiny se za porušení nepočítají; dny s neplným blokem vypíše hodnocení rozvrhu.
- Volitelné hodiny (#205): hodina samostatné skupiny nebo dělení, na kterou nechodí všichni žáci
  třídy (samostatná skupina zatím bez žáků se počítá), patří před hodiny pro všechny nebo za ně.
  Hodina pro všechny před ní i za ní stojí za každou takovou třídu 10, stejně jako okno třídy;
  výsledek vypíše, kde to i tak zůstalo.
- Délka dne (slabší než přání učitelů): hodina třídy od 5. hodiny dál (5. = 1, 6. = 3, 7. = 5,
  8. = 7). Přesun hodiny do delšího dne stojí 1–2, méně než nejslabší přání (3). To, co si vynutí
  počet hodin třídy při co nejrovnoměrnějších dnech, se za porušení nepočítá.
- Přání učitelů (#162, ve stejném hledání, slabší než požadavky tříd): volné dny
  (12 za chybějící den), nejvýš hodin denně (6 za hodinu navíc), nejméně hodin v den, kdy
  učí (4 za chybějící), nejvýš oken denně (4 za okno navíc), „raději ne“ (3 za hodinu).
  Nesplněná přání vypíše výsledek („Nesplněná přání učitelů: …“).
- Konec podle stavu, ne času: 1. hledání skončí, jakmile jsou splněné všechny požadavky
  (penalizace 0), když solver dokáže, že líp to nejde, nebo když se 30 s nezlepší
  (nesplnitelná data, např. málo tělocvičen nebo třídní učitelka, která nemůže 1. hodinu —
  dokázat, že líp to nejde, trvá déle než limit). Třídu, jejíž blok kvůli dostupnosti učitelů
  nemůže začínat včas, výsledek vysvětlí („1.A: 1. hodinu může mít jen 1 den
  v týdnu, … ji učit nemůže; hlavní výukový blok proto začíná později 4 dny“). 2. hledání pak se zachováním požadavků snižuje okna učitelů (2 za blok);
  skončí, když se 10 s nezlepší, nejpozději po 60 s. Limit 5 min je jen pojistka: jeho
  vypršení se loguje jako chyba a výsledek to hlásí. Na 2 jádrech trvá demo škola (18 tříd)
  zhruba 70–80 s.
- Učebny po vyřešení: třída má pro předmět pokud možno celý týden stejnou učebnu (tu,
  která je volná pro nejvíc jejích hodin, pak tu, kterou už má, pak dřív v seznamu);
  jen kolidující hodiny jdou do první volné dle pořadí. Když není volná žádná, hodina
  zůstane bez učebny a výsledek hlásí „nedostatek učeben … doplňte ji ručně“.
- Dialog průběhu (stav se zjišťuje jen, dokud je otevřený): ve frontě — počet běhů před námi
  (všech škol) a odhad čekání z průměru posledních běhů, údaj chybí, když fronta není; krok 1
  (hledání, neurčitý bar) a krok 2 (doladění oken učitelů, bar podle času) s průběžnými
  čísly nejlepšího dosud nalezeného rozvrhu (umístěné hodiny, bez učebny, okna tříd, hodiny
  hlavního výukového bloku bez výuky, okna učitelů; `TimetableGeneration.Progress`, job je zapisuje každé 2 s). Po
  doběhnutí ukáže v tomtéž dialogu výsledek: co upravit a jak rozvrh vypadá.
- Kdo generování spustil, dostane notifikaci (druh „Dokončená generování rozvrhu“, jen pro
  role s oprávněním Rozvrh); klik otevře Rozvrh se správnou verzí a výsledek. Když výsledek
  viděl v dialogu, notifikace je rovnou přečtená. Celkový progressbar: #170.
- Běh přerušený restartem serveru Hangfire spustí znovu; nedokončený dlouho po limitu se
  ukáže jako neúspěšný.
- Ruční přidání hodiny předvyplní první učebnu ze seznamu předmětu volnou v daném čase.
- 0,5 h = hodina v lichém týdnu, slot drží v obou týdnech (dvojice lichý/sudý zatím ne).
- Zatím ne: náročné předměty dopoledne (chybí údaj u předmětu), limity hodin denně podle
  vyhlášky, nastavitelné váhy.

## Ruční přesouvání (#206)

Administrace → Výuka → Rozvrh → koncept verze (`TimetableBoardController`, `TimetableSlots`):

- Hodina se táhne myší, na dotykové obrazovce po podržení, takže krátké přejetí dál roluje. Zamčená hodina přetáhnout nejde a přetažení hodinu nezamyká. Hodina skupiny jde ve všech jejích třídách a dělení se všemi skupinami; dvouhodinovka po jedné hodině.
- Při tažení se vyhodnotí každé pole bez spouštění generátoru. *Nedostupné* (drop nejde, server to ověří znovu): třída nemá volno, tedy jiná hodina, kde se potkají žáci, učitel učí, učitel nemůže. *Nevhodné*: „raději ne“, nad denním maximem učitele, nultá hodina, nové okno třídy v daném dni. Dopad na celý rozvrh ukáže až hodnocení.
- Učebna zůstane, když je v novém čase volná; jinak první volná ze seznamu předmětu, do které se žáci vejdou, jinak žádná a hodnocení to hlásí.
- **Zámek** je přímo na políčku hodiny (#209): nevýrazný otevřený, výrazný zavřený, kliknutím se přepne.
- **Neumístěné hodiny** (jen pohled na třídu) nejsou uložené: je to rozdíl plánu a rozvrhu, po celých hodinách, 0,5 h jako lichý týden. Hodina z nich dostane učitele podle přidělení. Přetažení z rozvrhu do nich hodinu smaže; hodinu navíc oproti plánu to odmítne, protože by mezi nimi nebyla.
- **Hodnocení rozvrhu** pod rozvrhem: tytéž řádky jako výsledek generování a kontrola před zveřejněním, načtené znovu po každé změně.

## Suplování (#215)

- **Nepřítomnost učitele** od–do, celé dny nebo tytéž hodiny každý den. Jeho hodiny
  bez zástupu agenda (Administrace → Výuka → Suplování) ukáže jako „Chybí zástup“.
- **Agenda** je mřížka dne: řádky třídy (nebo učitelé), sloupce hodiny.
  Výchozí pohled ukazuje hodiny bez zástupu a vyřešené (přepínač „Vyřešené hodiny“),
  „Všechny hodiny dne“ celý den.
  Klik na hodinu ji upraví, na prázdné místo třídy přidá hodinu navíc.
- **Změna hodiny** (den × hodina × třída nebo skupina): jiný učitel, předmět, učebna,
  odpadá, přesun na jinou hodinu téhož týdne, spojení s hodinou jiné třídy či skupiny
  ve stejný čas, hodina navíc. Upravit jde i jednotlivou hodinu bez nepřítomnosti.
- **Zobrazení**: změněný údaj přeškrtnutý a vedle nový (~~Aj~~ M), štítek jen druh změny —
  „Supl“, „Odpadá“, „Přesun na/z …“, „Spojeno s …“, „Navíc“. Stejně v rozvrhu, v editoru
  (jen u zveřejněné verze platné v daném týdnu, jen ke čtení), na Domů i v upozornění.
- **Nabídka zástupu**: volní a přítomní (ne „nemůže“), nejdřív s aprobací, pak ti, kdo
  ten den učí, pak s nejméně suplováními v měsíci. „Doplnit zástupy“ dá každé hodině
  prvního nabídnutého; nezveřejňuje.
- **Zveřejnění dne**: změny se připravují a ostatní je vidí (rozvrh, Domů) až po
  zveřejnění dne, které uloží jejich stav. Upozornění dostanou učitelé, žáci a rodiče,
  jimž se hodiny oproti dříve zveřejněnému liší, se všemi změnami dne, které se jich
  týkají; minulé dny se zveřejňují bez upozornění.
- **Přístup**: kdo v daný den zveřejněně supluje (nebo k jeho hodině je spojená jiná třída),
  má ten den třídu či skupinu jako „svou“ — docházka, známky, úkoly, chování, zprávy.
  Původnímu učiteli přístup zůstává. Adresáře a „kdo učí mé dítě“ zůstávají podle rozvrhu.
- **Rozhraní verzí**: změna si pamatuje předmět a učitele hodiny, pro kterou vznikla. Má-li
  nová verze v tu dobu jinou hodinu, změna se nepoužije a agenda ji ukáže jako problém
  (lze ji zrušit). Zveřejnění verze upozorní na dny se suplováním od jejího začátku.

## Navržené fáze

1. **Verze rozvrhu** (model, migrace, výběr verze podle týdne, koncept + zveřejnění).
   Přínos i bez generátoru: příprava rozvrhu na 2. pololetí dopředu.
2. **Učební plán, aprobace a přidělení výuky** (Administrace → Výuka, kontroly
   pokrytí rozvrhu a nasmlouvaných hodin).
3. **Generátor CP-SAT** pro koncept verze: tvrdá omezení → první použitelná verze;
   pak měkká omezení a váhy nastavitelné školou; výsledek vždy jen jako koncept.
4. **Dostupnost učitelů a přání**, bloky (#162); skupiny žáků samostatně (#166).
5. (Samostatně) **Suplování** nad verzemi (#215).

## Rizika a otevřené otázky

- Kvalita dat: generátor je jen tak dobrý jako úvazky — bez úvazků nic nevygeneruje.
- Skupiny a dělené hodiny výrazně zvětšují model; začít bez nich.
- Měkká omezení: priority se liší škola od školy → nastavitelné váhy, rozumné výchozí.
- Licence FET (AGPL), pokud by se volila varianta B.
- Výkon: CP-SAT běží v procesu API — pro větší SŠ zvážit samostatný worker / limit času
  řešení (např. 2 min) a vrácení nejlepšího nalezeného.
