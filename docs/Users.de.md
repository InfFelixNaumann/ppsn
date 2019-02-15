# Nutzer ppsn

Die Nutzer innerhalb des PPSn werden in der Hauptdatenbank verwaltet.

In dieser Datenbank muss die Tabelle `dbo.User` existieren, 
in der alle Nutzer gelistet werden. Die Authentifizerung erfolgt ebenfalls
gegen die Hauptdatenbank.

Es gibt zwei Nutzertypen, Datenbank-Nutzer und Domain-Nutzer.

## Anzeige der aktuellen Nutzer

### Datenbank

Der Dienst verarbeitet intern die R�ckgabe von `dbo.serverlogins`.

:::warn
�nderungen an einem Nutzer muss die Spalte `LoginVersion` erh�hen.
:::

### SimpleDebug

Die aktuellen Nutzer werden in der DE-Liste `tw_users` verwaltet.

Auflistung der Nutzer:
```
:use /ppsn
:listget tw_users
```

Aktualisierung kann durch `RefreshUsers` angeschoben werden.
