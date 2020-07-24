# Excel-Plugin

L�dt Smart-Tables auf Basis von Xml-Datenstr�men in ein Excel Formula.

![Men�](Imgs/Excel.Menu.png)

Zuerst muss eine Umgebung/Datenbankverbindung gew�hlt werden.

Danach k�nnen `Reporte` eingef�gt/geladen oder Smart-Table definiert werden (`Tabelle`).

## Tabelle definieren

Als erstes w�hlt man eine Basis-Tabelle aus, 

![Tabelle ausw�hlen](Imgs/Excel.Table1.png)

## Spalten definieren

Spalten werden in der mittleren Liste definiert. Dies werden per Drag&Drop in die 
R�ckgabe aufgenommen.

Sortierung, Name kann mittels des Kontextmen�s beeinflusst werden.

![Tabelle ausw�hlen](Imgs/Excel.Table2.png)

## Bedingungen

Die dritte Spalte beinh�lt die Filterbedingungen.

![Tabelle ausw�hlen](Imgs/Excel.Table3.png)

Neben Konstanten und anderen Felder k�nnen auch Excel-Zellen referenziert werden.

- Via Namen z.B. `$Feld`
- Oder Zelladresse z.B. `$C23` oder `$R23C3`
- Tabellen k�nnen auch angegebn werden, z.B. `$Tabelle1_C23`