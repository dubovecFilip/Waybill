# Waybill

Lokálny tracker zásielok pre **Euro Truck Simulator 2** a **American Truck
Simulator**. Sám rozpozná začiatok a koniec zákazky, zapíše ju do lokálnej
databázy a ukáže štatistiky. Bez účtu, bez internetu, bez druhého programu.

*Waybill* je nákladný list, teda dokument, ktorý sprevádza zásielku a nesie
trasu, náklad a odosielateľa. Presne to, čo tento program o každej jazde uchová.

![Okno aplikácie](assets/screenshot.png)

## Prečo vznikol

TrucksBook zneplatní doručenie, ak bol počas neho zapnutý asistent jazdy v pruhu.
Waybill stojí na opačnom princípe:

> **Zásielka sa nikdy nezneplatní kvôli tomu, že vodič použil asistenta.**

Asistenty, tempomat, prekračovanie rýchlosti, kolízie aj pokuty sa ukladajú ako
metadáta a zobrazujú v štatistikách. Zamietne sa len to, čo je priamym dôkazom,
že sa nejazdilo: teleport k cieľu, chýbajúca ukončovacia udalosť alebo prakticky
nulová vzdialenosť. Všetko ostatné dostane príznak na prezretie, nie zákaz.

## Čo vie

* Rozpozná začiatok aj koniec zákazky bez akéhokoľvek manuálneho zásahu
* Meria vzdialenosť, spotrebu, rýchlosť, poškodenie, pokuty, mýto a trajekty
* Zaznamenáva časovú os udalostí, teda kedy presne padla pokuta či nastala kolízia
* Ukladá súradnice trasy pre budúce vykreslenie mapy
* Po páde alebo zavretí uprostred jazdy nadviaže na rozpracovanú zákazku
* Spustí hru priamo z okna, aj s kontrolou telemetry pluginu
* Naimportuje históriu z TrucksBooku
* Ukladá do SQLite, exportuje do CSV a JSON, zálohuje a obnovuje

## Čo je potrebné

* Windows
* [.NET 9 SDK](https://dotnet.microsoft.com/download) na zostavenie zo zdrojákov
* Telemetry plugin od RenCloud, priložený v [`third-party/`](third-party/README.md)

## Inštalácia

Zostavenie zo zdrojákov:

```bash
dotnet build src/Waybill
```

Aplikácia sa spustí súborom `Waybill.exe` v priečinku
`src/Waybill/bin/Debug/net9.0-windows/`.

Prvý krok po spustení vedie do menu *Hrať → Nainštalovať telemetry plugin*, kde
stačí ukázať na `Win64/scs-telemetry.dll` z priečinka `third-party/`. Bez pluginu
hra telemetriu neposiela a Waybill nemá čo sledovať.

### Samostatný .exe

Z koreňového priečinka repozitára, keďže cesty v príkaze sú relatívne:

```bash
dotnet publish src/Waybill -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

Výsledkom je jediný súbor `dist\Waybill.exe` s veľkosťou okolo 50 MB, ktorý beží
aj na počítači bez nainštalovaného .NET. Dá sa presunúť kamkoľvek, databáza aj
nahrávky sú v `%LOCALAPPDATA%\Waybill\` nezávisle od jeho umiestnenia.

## Používanie

Poradie spustenia nehrá rolu, aplikácia sa na hru napojí sama, keď ju nájde.
Zákazky sa rozpoznajú a uložia bez zásahu.

Horná časť okna ukazuje priebeh aktuálnej zákazky, karta *Zásielky* obsahuje
históriu s vyhľadávaním, filtrom a poznámkami, karta *Štatistiky* súhrn.

## Príkazový riadok

Okno je bežný spôsob použitia, ale všetko funguje aj zo skriptu:

```bash
Waybill.exe --list [n]                  # posledné zásielky
Waybill.exe --stats [dni]               # súhrn celkovo alebo za obdobie
Waybill.exe --export csv|json [cesta]   # export histórie
Waybill.exe --import-trucksbook <csv>   # import histórie z TrucksBooku
Waybill.exe --backup [cesta]            # záloha databázy
Waybill.exe --restore <cesta>           # obnova zo zálohy
Waybill.exe --rebuild                   # prepočíta zásielky z nahrávok
Waybill.exe --replay <nahrávka>         # prehrá starú nahrávku
Waybill.exe --test-resume <nahrávka> <riadok>   # test obnovy po reštarte
```

## Kde sú dáta

Všetko je v `%LOCALAPPDATA%\Waybill\`, teda mimo priečinka projektu, takže
prebuildovanie ani `dotnet clean` o nič nepríde.

| Čo | Kde |
|---|---|
| Databáza zásielok | `deliveries.db` |
| Zálohy | `backups/` |
| Surové nahrávky telemetrie | `sessions/` |
| Rozpracovaná zákazka | `in-progress.json` |
| Nastavenia | `settings.json` |

Nahrávky sa po ukončení automaticky zabalia do `.gz`, čo je asi 13x menej miesta.
Nemažú sa, slúžia ako podklad pre `--rebuild` a `--replay`, ktoré ich čítajú
zabalené aj nezabalené.

## Jednotky

Predvolene sa riadia hrou. ATS používa imperiálne (mi, gal, mph, $), ETS2
metrické (km, l, km/h, €). V menu *Jednotky* sa dá vynútiť jeden systém.

Databáza ukladá vždy metricky a prevádza sa až pri zobrazení. História preto
nezávisí od toho, aké nastavenie platilo v čase jazdy, a prepnutie prekreslí aj
staré zásielky.

## Ako sa meria vzdialenosť

Vzdialenosť vedie odometer, teda tá istá sústava, v akej hlási čísla samotná hra
aj ponuka zákazky. Pozícia vo svete sa ukladá zvlášť, na detekciu teleportu a do
budúcna na kreslenie trasy. Podrobne v
[`docs/measurement.md`](docs/measurement.md).

## Import z TrucksBooku

*Data → Importovať históriu z TrucksBooku* a vybrať CSV export. Import je
idempotentný, kľúčom je TrucksBookID, takže ten istý súbor sa dá pustiť
opakovane bez duplikátov.

Export je v jednotkách daného profilu a hodnoty si nesú jednotku so sebou
(`157 mi`, `5.9 mpg`), takže sa prevádza podľa toho, čo je naozaj v súbore.
Importované zásielky dostanú stav `imported`, pretože za nimi nestojí telemetria,
ktorú by bolo možné overiť.

Zásielky, ktoré má TrucksBook so započítanou vzdialenosťou 0, teda tie neuznané,
sa importujú s plánovanou vzdialenosťou a poznámkou. Waybill ich započíta.

## Vývoj

Nahrávky v `sessions/` slúžia ako regresné testy. Po zmene v trackeri:

```bash
Waybill.exe --replay <nahrávka>
```

a porovnať, či čísla sedia. `--test-resume` navyše simuluje reštart uprostred
jazdy a porovná výsledok s jedným súvislým behom.

Príkaz `--rebuild` sa hodí po každej oprave detekcie, pretože staré záznamy si
inak natrvalo nesú verdikt vydaný vtedajšou verziou. Je bezstratový, keďže za
každou sledovanou zásielkou stojí nahrávka. Importované riadky sa nedotkne, tie
nemá z čoho prepočítať.

## Štruktúra

```
src/Waybill/
├── Tracking/       stavový automat zákazky, adaptér SDK, engine, formátovanie
├── Storage/        SQLite (deliveries, events, trip_points), import z TrucksBooku
├── SCSSdkClient/   vendorovaný C# klient SDK (MIT, s lokálnymi opravami)
├── GameLauncher.cs hľadanie a spúšťanie hier cez Steam
├── MainForm.cs     okno
└── Program.cs      CLI a vstupný bod
assets/             logo a zdroj ikony
docs/               vízia, plán a technické poznámky
third-party/        telemetry plugin do hry
archive/            odložené, už nepoužívané
```

## Stav

Funguje automatické sledovanie a ukladanie, spustenie hry z aplikácie, obnova po
reštarte, história s vyhľadávaním a poznámkami, štatistiky, časová os udalostí,
import z TrucksBooku, export a zálohy.

Chýba mapa a prehrávanie trasy, hoci súradnice sa už zbierajú, ďalej
achievementy a štatistiky za celé herné sedenie. Podrobnosti v
[`docs/roadmap.md`](docs/roadmap.md).

## Licencia

[MIT](LICENSE). Vendorovaný SDK klient aj plugin od RenCloud sú tiež MIT, takže
celý projekt je pod jednou licenciou.
