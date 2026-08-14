# Ako Waybill meria vzdialenosť a rýchlosť

Hra pracuje s dvomi sústavami jednotiek a nesmú sa miešať. Zámena jednej za
druhú je najčastejší zdroj nezmyselných čísel, tak nech je zapísané, čo znamená
ktorá.

## Dve sústavy

| Meranie | Jedna reálna jazda | Sústava |
|---|---|---|
| Odometer | 176,60 km | simulované km |
| Rýchlosť krát **herný** čas | 175,62 km | simulované km |
| `JobDelivered.DistanceKm` z hry | 176 km | simulované km |
| Pozícia vo svete | 13,08 km | world space |
| Rýchlosť krát **reálny** čas | 12,94 km | world space |

Mapa je zmenšená, na meraných trasách asi 13,5x, a herný čas beží zhruba 13x
rýchlejšie než reálny. Prvé tri riadky preto sedia navzájom a posledné dva tiež,
ale medzi skupinami je faktor kompresie.

Zásielka sa vykazuje v simulovaných km, teda v tom, čo hlási hra aj ponuka
zákazky, takže vedie odometer. Sčítavajú sa jeho prírastky medzi tikmi, s
odmietnutím záporných skokov a skokov nad prah, ktoré vznikajú pri teleporte
alebo pri načítaní pozície.

Pozícia vo svete sa ukladá zvlášť. Slúži na detekciu teleportu a do budúcna na
kreslenie trasy, nikdy na počítanie prejdenej vzdialenosti.

## Priemerná rýchlosť

Musí deliť simulované km herným časom stráveným jazdou.

* Delenie reálnym časom vykáže ako rýchlosť kompresiu času, teda okolo 770 km/h.
* Delenie celkovým herným časom započíta aj spánok a pauzy, čím rýchlosť podstrelí.

Sledovaný je preto samostatný čítač herných minút, ktorý beží len počas jazdy.
Importované zásielky ho nemajú, keďže za nimi nestojí telemetria, tak sa im
priemerná rýchlosť nepočíta.

## Jednotky pri ukladaní

Databáza ukladá vždy metricky a prevádza sa až pri zobrazení. História preto
nezávisí od toho, aké nastavenie platilo v čase jazdy, a prepnutie jednotiek
prekreslí aj staré zásielky.

## Herný čas

Herné hodiny majú rozlíšenie jednej minúty, takže krátke úseky sú hrubé. Na
odlíšenie pauzy od výpadku klienta to stačí: keď medzi dvomi tikmi ubehlo menej
herných minút, než by zodpovedalo reálnemu času, hra bola pozastavená, inak
nebežal Waybill.
