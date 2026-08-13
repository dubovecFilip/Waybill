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

1. **Nainštaluj telemetry plugin** do hry — pozri
   [`third-party/README.md`](third-party/README.md).
2. **Zbuilduj a spusti:**
   ```bash
   dotnet build src/Waybill
   ```
   Potom spusti `Waybill.exe` z `src/Waybill/bin/Debug/net9.0-windows/`.
3. **Hraj.** Appku môžeš zapnúť pred hrou aj po nej, poradie nehrá rolu. Zákazky
   sa rozpoznajú a uložia samé.

Ak appku zavrieš uprostred jazdy (alebo spadne), pri ďalšom spustení na tú istú
zákazku nadviaže — vrátane kilometrov najazdených, kým nebežala.

## Príkazový riadok

Okno je bežný spôsob použitia, ale všetko sa dá aj zo skriptu:

```bash
Waybill.exe --list [n]                  # posledné zásielky
Waybill.exe --stats [dni]               # súhrn (celkovo alebo za obdobie)
Waybill.exe --export csv|json [cesta]   # export histórie
Waybill.exe --backup [cesta]            # záloha databázy
Waybill.exe --restore <cesta>           # obnova zo zálohy
Waybill.exe --replay <súbor.jsonl>      # prehrá starú nahrávku
Waybill.exe --test-resume <súbor> <riadok>   # test obnovy po reštarte
```

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
├── Storage/        SQLite (deliveries, events, trip_points)
├── SCSSdkClient/   vendorovaný C# klient SDK (MIT, s lokálnymi opravami)
├── MainForm.cs     okno
└── Program.cs      CLI + vstupný bod
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

## Stav

Funguje: automatické sledovanie a ukladanie, obnova po reštarte, história s
hľadaním a poznámkami, štatistiky, časová os udalostí, export, zálohy.

Chýba: mapa a prehrávanie trasy (súradnice sa už zbierajú), achievementy,
štatistiky za herné sedenie, detekcia spustenia hry, import z TrucksBooku.
Podrobnosti v [`docs/roadmap.md`](docs/roadmap.md).

## Licencie

Vendorovaný SDK klient a plugin sú MIT (RenCloud). Licenciu vlastného kódu si
ešte zvoľ — ak by si niekedy preberal kód z projektov ako TruckNav-Sim (GPL-3.0),
vynúti si to GPL-3.0 na celom projekte.
