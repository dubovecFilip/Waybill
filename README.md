# Waybill

Lokálny tracker zásielok pre **Euro Truck Simulator 2** a **American Truck
Simulator**. Sám rozpozná začiatok a koniec zákazky, zapíše ju do lokálnej
databázy a ukáže štatistiky — bez účtu, bez internetu, bez druhého programu.

*Waybill* je nákladný list — dokument, ktorý sprevádza zásielku a nesie trasu,
náklad a odosielateľa. Presne to, čo tento program o každej jazde uchová.

## Prečo

TrucksBook zneplatní doručenie, ak bol počas neho zapnutý asistent jazdy v pruhu.
Toto je náhrada postavená na opačnom princípe:

> **Zásielka sa nikdy nezneplatní kvôli tomu, že si použil asistenta.**

Asistenty, tempomat, prekračovanie rýchlosti, kolízie či pokuty sa ukladajú ako
**metadáta** a zobrazujú v štatistikách. Tvrdo sa odmieta len to, čo je priamym
dôkazom, že sa nejazdilo — teleport k cieľu, chýbajúca ukončovacia udalosť alebo
prakticky nulová vzdialenosť. Všetko ostatné dostane príznak na prezretie, nie
zákaz.

## Ako to spustiť

1. **Zbuilduj:**
   ```bash
   dotnet build src/Waybill
   ```
   Spusti `Waybill.exe` z `src/Waybill/bin/Debug/net9.0-windows/`.
2. **Nainštaluj telemetry plugin** — v menu *Hrať → Nainštalovať telemetry plugin*
   a ukáž na `Win64/scs-telemetry.dll` zo
   [`third-party/`](third-party/README.md). Bez neho hra nič neposiela.
3. **Hraj** — v menu *Hrať* spusti ATS alebo ETS2 priamo z appky. Hry sa hľadajú
   vo všetkých knižniciach Steamu a spúšťajú cezeň, takže overlay aj cloud save
   fungujú normálne.

Poradie nehrá rolu — appku môžeš zapnúť pred hrou aj po nej. Ak ju zavrieš
uprostred jazdy (alebo spadne), pri ďalšom spustení na tú istú zákazku nadviaže,
vrátane kilometrov najazdených, kým nebežala.

### Samostatný .exe

```bash
dotnet publish src/Waybill -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

Vyrobí jediný `dist/Waybill.exe` (~50 MB), ktorý beží aj tam, kde .NET nie je
nainštalovaný. Nastavenia pre publish sú v `Waybill.csproj` schválne podmienené —
inak by zasiahli aj bežný `dotnet build` a presunuli jeho výstup inam.

## Príkazový riadok

Okno je bežný spôsob použitia, ale všetko sa dá aj zo skriptu:

```bash
Waybill.exe --list [n]                  # posledné zásielky
Waybill.exe --stats [dni]               # súhrn (celkovo alebo za obdobie)
Waybill.exe --export csv|json [cesta]   # export histórie
Waybill.exe --import-trucksbook <csv>   # import histórie z TrucksBooku
Waybill.exe --backup [cesta]            # záloha databázy
Waybill.exe --restore <cesta>           # obnova zo zálohy
Waybill.exe --rebuild                   # prepočíta zásielky z nahrávok
Waybill.exe --replay <nahrávka>         # prehrá starú nahrávku
Waybill.exe --test-resume <nahrávka> <riadok>   # test obnovy po reštarte
```

`--rebuild` sa hodí po každej oprave detekcie: staré záznamy si inak natrvalo
nesú verdikt, ktorý vydala vtedajšia verzia. Je to bezstratové, lebo za každou
sledovanou zásielkou stojí nahrávka — importované riadky sa nedotkne, tie
prepočítať nemá z čoho.

## Kde sú dáta

Všetko je v `%LOCALAPPDATA%\Waybill\` — teda **mimo** projektu, takže
prebuildovanie ani `dotnet clean` o nič nepríde:

| Čo | Kde |
|---|---|
| Databáza zásielok | `deliveries.db` |
| Zálohy | `backups/` |
| Surové nahrávky telemetrie | `sessions/` |
| Rozpracovaná zákazka | `in-progress.json` |
| Nastavenia | `settings.json` |

Nahrávky sa po ukončení automaticky zabalia do `.gz` — komprimujú sa asi **13×**
(9,9 MB nahrávka → 750 kB), takže sa nič nemusí mazať. Prehrávanie ich číta
zabalené aj nezabalené.

## Jednotky

Predvolene podľa hry — **ATS imperiálne** (mi, gal, mph, $), **ETS2 metrické**
(km, l, km/h, €). V menu *Jednotky* sa dá vynútiť jeden systém.

Databáza ukladá **vždy metricky** a prevádza sa až pri zobrazení, takže história
nezávisí od toho, aké nastavenie bolo v čase jazdy, a prepnutie prekreslí aj
staré zásielky.

## Ako sa meria vzdialenosť

Toto je jediná časť, kde sa dá ľahko pomýliť, tak nech je zapísaná: hra pracuje
s **dvomi sústavami jednotiek** a nesmú sa miešať.

| Meranie | Jedna reálna jazda | Sústava |
|---|---|---|
| Odometer | 176,60 km | simulované km |
| Rýchlosť × **herný** čas | 175,62 km | simulované km |
| `JobDelivered.DistanceKm` z hry | 176 km | simulované km |
| Pozícia vo svete | 13,08 km | world space |
| Rýchlosť × **reálny** čas | 12,94 km | world space |

Mapa je zmenšená (~13,5× na meraných trasách) a herný čas beží ~13× rýchlejšie.
Zásielka sa vykazuje v **simulovaných km** (to je to, čo hlási hra aj ponuka
zákazky), takže vedie **odometer**. Pozícia sa ukladá zvlášť — na detekciu
teleportu a do budúcna na kreslenie trasy.

Priemerná rýchlosť musí deliť simulované km **herným** časom stráveným jazdou.
Delenie reálnym časom vykáže ako rýchlosť kompresiu času (~770 km/h), delenie
celkovým herným časom zas počíta aj spánok.

## Štruktúra

```
src/Waybill/
├── Tracking/       stavový automat zákazky, adaptér SDK, engine, formátovanie
├── Storage/        SQLite (deliveries, events, trip_points), import z TrucksBooku
├── SCSSdkClient/   vendorovaný C# klient SDK (MIT, s lokálnymi opravami)
├── GameLauncher.cs hľadanie a spúšťanie hier cez Steam
├── MainForm.cs     okno
└── Program.cs      CLI + vstupný bod
assets/             logo a zdroj ikony
docs/roadmap.md     vízia a plán
third-party/        telemetry plugin do hry
archive/            odložené, už nepoužívané
```

## Testovanie

Nahrávky v `sessions/` sú regresné testy. Po zmene v trackeri:

```bash
Waybill.exe --replay <stará-nahrávka.jsonl>
```

a porovnaj, či čísla sedia. `--test-resume` navyše simuluje reštart uprostred
jazdy a porovná výsledok s jedným súvislým behom.

## Import z TrucksBooku

*Data → Importovať históriu z TrucksBooku* a vyber CSV export. Import je
idempotentný (kľúčom je TrucksBookID), takže ten istý súbor sa dá pustiť
opakovane bez duplikátov.

Export je v jednotkách daného profilu a hodnoty si nesú jednotku so sebou
(`157 mi`, `5.9 mpg`), takže sa prevádza podľa toho, čo je naozaj v súbore.
Importované zásielky dostanú stav `imported` — nemá za nimi telemetriu, ktorú by
šlo overiť, a tvrdiť o nich `accepted` by znamenalo dať im dôveru, ktorú si
nezaslúžili.

Zásielky, ktoré má TrucksBook so započítanou vzdialenosťou 0 (tie, čo neuznal),
sa importujú s plánovanou vzdialenosťou a poznámkou — Waybill ich započíta.

## Stav

Funguje: automatické sledovanie a ukladanie, spustenie hry z appky, obnova po
reštarte, história s hľadaním a poznámkami, štatistiky, časová os udalostí,
import z TrucksBooku, export, zálohy.

Chýba: mapa a prehrávanie trasy (súradnice sa už zbierajú), achievementy,
štatistiky za herné sedenie. Podrobnosti v [`docs/roadmap.md`](docs/roadmap.md).

## Licencia

[MIT](LICENSE). Vendorovaný SDK klient aj plugin od RenCloud sú tiež MIT, takže
celý projekt je pod jednou licenciou.

Pozor pri preberaní cudzieho kódu: projekty pod GPL 3.0 (napríklad TruckNav-Sim,
ktorý by sa hodil pri mape) by vynútili GPL na celom Waybille. Ak má zostať MIT,
takú funkciu treba napísať po svojom.
