# Assets

| Súbor | Načo je |
|---|---|
| `logo.png` | Logo v plnom rozlíšení (1000x1000). Zdroj pre ikonu aplikácie. |
| `logo.psd` | Zdrojový súbor loga na úpravy. |

Ikona aplikácie `src/Waybill/waybill.ico` je z `logo.png` vygenerovaná a obsahuje
veľkosti 16, 32, 48, 64, 128 a 256 px, aby si Windows vybral podľa toho, kde ju
kreslí (panel úloh, plocha, alt+tab).

Keď sa logo zmení, ikonu treba vygenerovať znova:

```powershell
# z koreňa projektu, vyžaduje PowerShell
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile("assets\logo.png")
# ... viď históriu commitov, ikona sa skladá z PNG dlaždíc do jedného .ico
```

Jednoduchšie je použiť ktorýkoľvek online prevodník PNG na ICO, len treba dbať,
aby výsledok obsahoval všetky uvedené veľkosti.
