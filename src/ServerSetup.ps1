<#
    Setup Server - Applicativi Aera
    Prepara il server per il controllo remoto da AeraControl.

    Incorporato come risorsa in Setup-AeraControl.exe, che lo estrae
    e lo esegue quando si sceglie il ruolo di server.

    Con -NonInterattivo non fa domande e non apre nessuna finestra:
    le risposte gliele passa l'installatore, che le ha gia' chieste
    nella sua, e ne raccoglie qui il resoconto riga per riga.
#>

param(
    # Utente proprietario delle attivita' pianificate. Vuoto = il
    # predefinito, Administrator di questa macchina.
    [string] $UtenteTask = "",

    # Se creare l'attivita' che apre il segnalatore a ogni accesso.
    [switch] $AvviaSegnalatore,

    # Nessuna domanda, nessun menu di prova, nessuna attesa di INVIO.
    [switch] $NonInterattivo
)

#--------------------------------------------------------------------
# CONFIGURAZIONE
#--------------------------------------------------------------------

# Numero unico del prodotto: lo stesso di AeraControl.cs, AeraTray.cs
# e SetupAera.cs. La nota estesa sta in AeraControl.cs.
#
# Stava a 0.1.0 mentre tutto il resto era a 1.6.0: questo file viveva
# solo nella cartella di lavoro, fuori dal controllo di versione, e
# nessuno se ne accorgeva. Ora sta in src/ e la compilazione controlla
# anche lui.
$VersioneServer = "1.6.5"

# Utente proprietario delle attivita' pianificate.
# Le finestre compaiono nella sessione di QUESTO utente: deve essere
# quello connesso in console, di norma Administrator con autologon.
if ($UtenteTask -and $UtenteTask.Trim().Length -gt 0) {
    $Utente = $UtenteTask.Trim()
} else {
    $Utente = "$env:COMPUTERNAME\Administrator"
}

$Applicativi = @(
    @{
        Titolo   = "Aera Service"
        Task     = "Aera_Service"
        Exe      = "C:\Aera\Aera_Service.exe"
        Processo = "Aera_Service"
    }
    @{
        Titolo   = "Aera Remote Function"
        Task     = "Aera_RemoteServer"
        Exe      = "C:\Aera\Remote_Function\AeraRemoteServer.exe"
        Processo = "AeraRemoteServer"
    }
    @{
        Titolo   = "Restaurant Pocket Sol"
        Task     = "Aera_RestaurantPocket"
        Exe      = "C:\Aera\RestaurantPocketSol\RestaurantPocketSol.exe"
        Processo = "RestaurantPocketSol"
    }
)

#--------------------------------------------------------------------
# FUNZIONI DI SUPPORTO
#--------------------------------------------------------------------

function Scrivi-Titolo($testo) {
    Write-Host ""
    Write-Host ("  " + $testo) -ForegroundColor Cyan
    Write-Host ("  " + ("-" * $testo.Length)) -ForegroundColor DarkCyan
}

function Scrivi-Ok($testo)      { Write-Host "  [OK] $testo" -ForegroundColor Green }
function Scrivi-Avviso($testo)  { Write-Host "  [!]  $testo" -ForegroundColor Yellow }
function Scrivi-Errore($testo)  { Write-Host "  [X]  $testo" -ForegroundColor Red }
function Scrivi-Info($testo)    { Write-Host "       $testo" -ForegroundColor Gray }

#--------------------------------------------------------------------
# CONTROLLO PRIVILEGI
#--------------------------------------------------------------------

$identita  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principale = New-Object Security.Principal.WindowsPrincipal($identita)

if (-not $principale.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Scrivi-Errore "Eseguire come Amministratore."
    if (-not $NonInterattivo) { Read-Host "`n  Premere INVIO per chiudere" }
    exit 1
}

if (-not $NonInterattivo) { Clear-Host }
Write-Host ""
Write-Host "  ==================================================" -ForegroundColor White
Write-Host "   Applicativi Aera - configurazione server" -ForegroundColor White
Write-Host "   versione $VersioneServer" -ForegroundColor DarkGray
Write-Host "  ==================================================" -ForegroundColor White

#--------------------------------------------------------------------
# UTENTE PROPRIETARIO DEI TASK
#--------------------------------------------------------------------
# Gli applicativi si aprono nella sessione dell'utente proprietario del
# task: se quell'utente non ha una sessione interattiva, dal client i
# comandi risultano riusciti ma sul monitor non compare niente.
#
# Si cercano le sessioni interattive dai processi explorer.exe, uno per
# utente connesso: Win32_ComputerSystem.UserName vede solo la console
# fisica e resta vuoto quando si e' collegati in RDP.

$connessi = @()
try {
    $connessi = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop |
                  ForEach-Object {
                      $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner -ErrorAction SilentlyContinue
                      if ($o -and $o.User) { $o.Domain + "\" + $o.User }
                  } | Select-Object -Unique)
}
catch { $connessi = @() }

$altri = @($connessi | Where-Object { $_ -and $_ -ne $Utente })

if ($NonInterattivo) {
    # L'utente l'ha gia' scelto chi ha lanciato l'installazione: qui
    # si segnala soltanto se quella scelta e' incoerente con le
    # sessioni aperte, perche' e' il motivo numero uno per cui dal
    # client i comandi riescono ma sul monitor non compare niente.
    if ($connessi.Count -gt 0) {
        Scrivi-Info ("Sessioni aperte adesso: " + ($connessi -join ", "))
    }
    if ($connessi.Count -gt 0 -and ($connessi -notcontains $Utente)) {
        Scrivi-Avviso "$Utente non ha una sessione aperta in questo momento."
        Scrivi-Info "Serve l'autologon, o le finestre non compariranno."
    }
}
elseif ($altri.Count -gt 0) {
    Write-Host ""
    Write-Host "  Utenti con una sessione aperta adesso:" -ForegroundColor Yellow
    foreach ($u in $connessi) { Write-Host ("    - " + $u) -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "  Predefinito per i task : $Utente" -ForegroundColor Gray
    Write-Host "  Administrator non risulta connesso: se resta lui il" -ForegroundColor Gray
    Write-Host "  proprietario, serve l'autologon perche' le finestre" -ForegroundColor Gray
    Write-Host "  compaiano. Per una prova subito si puo' usare un" -ForegroundColor Gray
    Write-Host "  utente gia' connesso." -ForegroundColor Gray
    Write-Host ""
    $risposta = Read-Host ("  Intestare i task a '" + $altri[0] + "' invece che ad Administrator? [s/N]")
    if ($risposta -match '^[sS]') {
        $Utente = $altri[0]
        Write-Host ("  -> i task useranno " + $Utente) -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "   Server : $env:COMPUTERNAME" -ForegroundColor Gray
Write-Host "   Utente : $Utente" -ForegroundColor Gray

#--------------------------------------------------------------------
# 1. ATTIVITA' PIANIFICATE
#--------------------------------------------------------------------

Scrivi-Titolo "Attivita' pianificate"

$mancanti = @()

foreach ($app in $Applicativi) {

    if (-not (Test-Path $app.Exe)) {
        $mancanti += $app.Exe
    }

    $cartella = Split-Path $app.Exe -Parent

    try {
        # WorkingDirectory: molti applicativi cercano i propri file di
        # configurazione nella cartella dell'eseguibile. Senza questo
        # partono e si chiudono subito.
        $azione = New-ScheduledTaskAction -Execute $app.Exe -WorkingDirectory $cartella

        # LogonType Interactive = "Esegui solo se l'utente ha effettuato
        # l'accesso": senza questo la finestra finisce in sessione 0 e
        # resta invisibile sul monitor.
        $principal = New-ScheduledTaskPrincipal -UserId $Utente `
                                                -LogonType Interactive `
                                                -RunLevel Limited

        # ExecutionTimeLimit 0 -> nessun limite di durata
        # MultipleInstances IgnoreNew -> niente doppioni
        $impostazioni = New-ScheduledTaskSettingsSet `
                            -ExecutionTimeLimit ([TimeSpan]::Zero) `
                            -MultipleInstances IgnoreNew `
                            -AllowStartIfOnBatteries `
                            -DontStopIfGoingOnBatteries `
                            -StartWhenAvailable

        Register-ScheduledTask -TaskName  $app.Task `
                               -Action    $azione `
                               -Principal $principal `
                               -Settings  $impostazioni `
                               -Description "Avvio remoto - AeraControl" `
                               -Force | Out-Null

        Scrivi-Ok $app.Task
    }
    catch {
        Scrivi-Errore "$($app.Task): $($_.Exception.Message)"
    }
}

if ($mancanti.Count -gt 0) {
    Write-Host ""
    Scrivi-Avviso "Eseguibili non trovati sul disco:"
    foreach ($m in $mancanti) { Scrivi-Info $m }
    Scrivi-Info "I task sono stati creati lo stesso: correggere i percorsi"
    Scrivi-Info "in questo file se gli applicativi sono altrove."
}

#--------------------------------------------------------------------
# 2. CONTROLLO ACCOUNT UTENTE (UAC)
#--------------------------------------------------------------------

Scrivi-Titolo "Accesso remoto"

# Senza dominio, UAC svuota i privilegi di amministratore quando
# l'accesso arriva dalla rete: si otterrebbe "Accesso negato" anche
# con credenziali corrette.
try {
    $chiave = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
    Set-ItemProperty -Path $chiave -Name "LocalAccountTokenFilterPolicy" `
                     -Value 1 -Type DWord -ErrorAction Stop
    Scrivi-Ok "Restrizioni UAC di rete disattivate"
}
catch {
    Scrivi-Avviso "LocalAccountTokenFilterPolicy: $($_.Exception.Message)"
}

#--------------------------------------------------------------------
# 3. PROFILO DI RETE
#--------------------------------------------------------------------

# Con rete Pubblica Windows blocca condivisione e accesso remoto.
try {
    $pubbliche = @(Get-NetConnectionProfile -ErrorAction Stop |
                   Where-Object { $_.NetworkCategory -eq "Public" })

    if ($pubbliche.Count -eq 0) {
        Scrivi-Ok "Profilo di rete gia' corretto"
    }
    else {
        foreach ($p in $pubbliche) {
            try {
                Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex `
                                         -NetworkCategory Private -ErrorAction Stop
                Scrivi-Ok "Rete '$($p.Name)' impostata su Privata"
            }
            catch {
                Scrivi-Avviso "Rete '$($p.Name)': impostazione non riuscita"
                Scrivi-Info $_.Exception.Message
                Scrivi-Info "Impostarla a mano da Impostazioni > Rete."
            }
        }
    }
}
catch {
    Scrivi-Avviso "Profilo di rete non leggibile: $($_.Exception.Message)"
}

#--------------------------------------------------------------------
# 4. FIREWALL
#--------------------------------------------------------------------

# Regole usate da schtasks, tasklist e taskkill via RPC su SMB.
$gruppi = @(
    "Condivisione file e stampanti"
    "File and Printer Sharing"
    "Gestione remota gruppo di lavoro"
    "Windows Remote Management"
    "Strumentazione gestione Windows (WMI)"
    "Windows Management Instrumentation (WMI)"
)

$abilitate = 0
foreach ($g in $gruppi) {
    try {
        Enable-NetFirewallRule -DisplayGroup $g -ErrorAction Stop
        $abilitate++
    }
    catch { }
}

if ($abilitate -gt 0) {
    Scrivi-Ok "Regole firewall abilitate ($abilitate gruppi)"
}
else {
    Scrivi-Avviso "Nessuna regola firewall modificata: verificare a mano"
    Scrivi-Info "Pannello di controllo > Windows Defender Firewall >"
    Scrivi-Info "Consenti app: Condivisione file e stampanti"
}

# Gestione remota attivita' pianificate: e' il canale RPC che
# "schtasks /s" dal client usa davvero (senza, le query e i comandi
# restano appesi fino al timeout). Il gruppo e' spento di fabbrica.
# Si usa il token @FirewallAPI.dll,-33252, identico in tutte le
# lingue di Windows: niente dipendenza dal nome localizzato.
try {
    Enable-NetFirewallRule -Group '@FirewallAPI.dll,-33252' -ErrorAction Stop
    Scrivi-Ok "Gestione remota attivita' pianificate abilitata"
}
catch {
    # Ripiego: ricerca per nome visualizzato (italiano o inglese)
    $regoleTask = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
                    Where-Object { $_.DisplayGroup -match 'pianificat|Scheduled Tasks' })
    if ($regoleTask.Count -gt 0) {
        $regoleTask | Enable-NetFirewallRule -ErrorAction SilentlyContinue
        Scrivi-Ok "Gestione remota attivita' pianificate abilitata"
    }
    else {
        Scrivi-Avviso "Gruppo 'Gestione remota attivita' pianificate' non trovato"
        Scrivi-Info "Abilitarlo a mano: Windows Defender Firewall > App consentite"
    }
}

#--------------------------------------------------------------------
# 5. ACCOUNT ADMINISTRATOR
#--------------------------------------------------------------------

Scrivi-Titolo "Account Administrator"

$problemiAccount = $false

try {
    $acc = Get-LocalUser -Name "Administrator" -ErrorAction Stop

    if ($acc.Enabled) {
        Scrivi-Ok "Account attivo"
    }
    else {
        Scrivi-Avviso "Account DISABILITATO"
        Scrivi-Info "Abilitarlo con:  Enable-LocalUser -Name Administrator"
        Scrivi-Info "e assegnare una password."
        $problemiAccount = $true
    }

    if ($acc.PasswordRequired -eq $false) {
        Scrivi-Avviso "Account senza password"
        Scrivi-Info "Windows rifiuta gli accessi di rete con password vuota."
        $problemiAccount = $true
    }
}
catch {
    Scrivi-Avviso "Account 'Administrator' non trovato"
    Scrivi-Info "Se e' stato rinominato, aggiornare la variabile Utente"
    Scrivi-Info "in questo file e il campo Utente in AeraControl."
    $problemiAccount = $true
}

#--------------------------------------------------------------------
# 6. AUTOLOGON
#--------------------------------------------------------------------

Scrivi-Titolo "Autologon"

try {
    $winlogon = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    $valori = Get-ItemProperty -Path $winlogon -ErrorAction Stop

    if ($valori.AutoAdminLogon -eq "1") {
        Scrivi-Ok "Configurato per l'utente '$($valori.DefaultUserName)'"
    }
    else {
        Scrivi-Avviso "Autologon NON configurato"
        Scrivi-Info "Senza un utente sempre loggato in console le finestre"
        Scrivi-Info "non compaiono sul monitor del server."
        Scrivi-Info ""
        Scrivi-Info "Configurarlo con Autologon.exe di Sysinternals:"
        Scrivi-Info "cifra la password come LSA secret invece di lasciarla"
        Scrivi-Info "in chiaro nel registro."
    }
}
catch {
    Scrivi-Avviso "Stato autologon non verificabile"
}

#--------------------------------------------------------------------
# 7. SEGNALATORE DI STATO (icona accanto all'orologio)
#--------------------------------------------------------------------

Scrivi-Titolo "Segnalatore di stato"

# Una cartella sola per tutto: console, segnalatore, stato e
# segnaposto. Prima erano sparsi fra Palmari e AeraTray.
$cartellaTray = "C:\iotatau\AeraControl"
$exeTray      = Join-Path $cartellaTray "AeraTray.exe"

# Qui prima si cercava %TEMP%\AeraTray.cs e lo si compilava con csc:
# era la strada dei vecchi .bat. Con Setup-AeraControl.exe quel
# sorgente non c'e', quindi il blocco veniva saltato in silenzio e con
# lui la creazione dell'attivita' Aera_Segnalatore. Risultato: su ogni
# server installato con l'installatore nuovo il segnalatore non si
# apriva piu' da solo dopo un riavvio.
#
# L'eseguibile ora lo installa l'installatore stesso, gia' compilato:
# qui basta trovarlo.
if (-not (Test-Path $exeTray)) {
    Scrivi-Errore "Segnalatore non trovato in $exeTray"
    Scrivi-Info "Andava copiato dall'installatore prima di questo passo."
}
else {
    try {
        # Se sta gia' girando va fermato prima di ripuntarci l'attivita'
        Get-Process AeraTray -ErrorAction SilentlyContinue |
            ForEach-Object { try { $_.Kill() } catch { } }
        Start-Sleep -Milliseconds 600

        Scrivi-Ok "Segnalatore in $exeTray"

        if ($NonInterattivo) {
            $autoTray = [bool]$AvviaSegnalatore
        }
        else {
            Write-Host ""
            Write-Host "  Il segnalatore puo' aprirsi da solo a ogni accesso di" -ForegroundColor Gray
            Write-Host "  $Utente, cosi' lo stato e' sempre sott'occhio." -ForegroundColor Gray
            $rispTray = Read-Host "  Avviarlo automaticamente all'accesso? [S/n]"
            $autoTray = -not ($rispTray -match '^[nN]')
        }

        if ($true) {
            $azTray = New-ScheduledTaskAction -Execute $exeTray -WorkingDirectory $cartellaTray
            $trTray = New-ScheduledTaskTrigger -AtLogOn -User $Utente
            $prTray = New-ScheduledTaskPrincipal -UserId $Utente `
                                                 -LogonType Interactive `
                                                 -RunLevel Limited
            $imTray = New-ScheduledTaskSettingsSet `
                          -ExecutionTimeLimit ([TimeSpan]::Zero) `
                          -MultipleInstances IgnoreNew `
                          -AllowStartIfOnBatteries `
                          -DontStopIfGoingOnBatteries

            Register-ScheduledTask -TaskName "Aera_Segnalatore" `
                                   -Action    $azTray `
                                   -Trigger   $trTray `
                                   -Principal $prTray `
                                   -Settings  $imTray `
                                   -Description "Segnalatore di stato - AeraControl" `
                                   -Force | Out-Null

            if ($autoTray) {
                Scrivi-Ok "Si aprira' a ogni accesso di $Utente"
            }
            else {
                # L'attivita' resta registrata ma spenta: per
                # riaccenderla bastera' rieseguire questo script,
                # oppure abilitarla dall'Utilita' di pianificazione.
                try {
                    Disable-ScheduledTask -TaskName "Aera_Segnalatore" -ErrorAction Stop | Out-Null
                    Scrivi-Info "Avvio automatico disattivato"
                }
                catch {
                    Scrivi-Avviso "Non sono riuscito a disattivare l'avvio automatico"
                }
            }

            # Avviato tramite l'attivita' per farlo girare come
            # l'utente scelto e non come l'amministratore che sta
            # eseguendo questo script.
            if ($autoTray) {
                try {
                    Start-ScheduledTask -TaskName "Aera_Segnalatore" -ErrorAction Stop
                    Scrivi-Ok "Segnalatore avviato"
                }
                catch {
                    Scrivi-Info "Partira' al prossimo accesso"
                }
            }

            Scrivi-Info ""
            Scrivi-Info "Windows 11 nasconde le icone nuove: per vederla sempre"
            Scrivi-Info "trascinarla fuori dalla freccia ^ della barra, oppure"
            Scrivi-Info "Impostazioni > Personalizzazione > Barra applicazioni >"
            Scrivi-Info "Altre icone nella barra delle applicazioni."
        }
    }
    catch {
        Scrivi-Avviso "Segnalatore: $($_.Exception.Message)"
    }
}

#--------------------------------------------------------------------
# MENU DI TEST
#--------------------------------------------------------------------

function Mostra-Stato {
    Write-Host ""
    Write-Host "  Stato attuale:" -ForegroundColor Cyan
    Write-Host ""

    foreach ($app in $Applicativi) {
        $p = Get-Process -Name $app.Processo -ErrorAction SilentlyContinue

        if ($p) {
            $primo = @($p)[0]
            $memoria = [math]::Round($primo.WorkingSet64 / 1MB, 1)
            Write-Host ("   [ATTIVO]  {0,-24}" -f $app.Titolo) `
                       -ForegroundColor Green -NoNewline
            Write-Host ("PID {0}  sessione {1}  {2} MB" -f `
                        $primo.Id, $primo.SessionId, $memoria) -ForegroundColor Gray
        }
        else {
            Write-Host ("   [fermo ]  {0,-24}" -f $app.Titolo) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

function Avvia-Applicativo($app) {
    try {
        Start-ScheduledTask -TaskName $app.Task -ErrorAction Stop
        Write-Host "  Avvio richiesto: $($app.Titolo)" -ForegroundColor Green
    }
    catch {
        Write-Host "  Errore su $($app.Titolo): $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Ferma-Applicativo($app) {
    $p = Get-Process -Name $app.Processo -ErrorAction SilentlyContinue
    if ($p) {
        Stop-Process -InputObject $p -Force -ErrorAction SilentlyContinue
        Write-Host "  Terminato: $($app.Titolo)" -ForegroundColor Yellow
    }
    else {
        Write-Host "  Gia' fermo: $($app.Titolo)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  ==================================================" -ForegroundColor White
Write-Host "   Configurazione completata" -ForegroundColor White
Write-Host "  ==================================================" -ForegroundColor White

if ($problemiAccount) {
    Write-Host ""
    Scrivi-Avviso "Ci sono segnalazioni sull'account Administrator:"
    Scrivi-Info "il client non riuscira' a connettersi finche' non"
    Scrivi-Info "vengono risolte."
}

# Il menu di prova serve solo quando questo script lo si lancia a mano.
# Dall'installatore le stesse azioni stanno nel segnalatore e nella
# console, e qui bloccherebbero soltanto l'attesa di una risposta che
# nessuno puo' dare.
$esci = $NonInterattivo

while (-not $esci) {

    Mostra-Stato

    Write-Host "  --------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "   Test in locale" -ForegroundColor Cyan
    Write-Host "  --------------------------------------------------" -ForegroundColor DarkGray

    for ($i = 0; $i -lt $Applicativi.Count; $i++) {
        Write-Host ("    [{0}] Avvia {1}" -f ($i + 1), $Applicativi[$i].Titolo)
    }

    Write-Host "    [A] Avvia tutti"
    Write-Host "    [F] Ferma tutti"
    Write-Host "    [R] Riavvia tutti"
    Write-Host "    [S] Aggiorna stato"
    Write-Host "    [T] Elenco attivita' pianificate create"
    Write-Host "    [0] Esci"
    Write-Host ""

    $scelta = (Read-Host "   Scelta").Trim().ToUpper()

    switch ($scelta) {

        "0" { $esci = $true }

        "A" {
            Write-Host ""
            foreach ($app in $Applicativi) {
                Avvia-Applicativo $app
                Start-Sleep -Milliseconds 1500
            }
            Write-Host "`n  Attendo l'apertura delle finestre..." -ForegroundColor Gray
            Start-Sleep -Seconds 3
        }

        "F" {
            Write-Host ""
            foreach ($app in $Applicativi) { Ferma-Applicativo $app }
            Start-Sleep -Seconds 2
        }

        "R" {
            Write-Host ""
            foreach ($app in $Applicativi) { Ferma-Applicativo $app }
            Start-Sleep -Seconds 3
            foreach ($app in $Applicativi) {
                Avvia-Applicativo $app
                Start-Sleep -Milliseconds 1500
            }
            Start-Sleep -Seconds 3
        }

        "S" { }

        "T" {
            Write-Host ""
            foreach ($app in $Applicativi) {
                $t = Get-ScheduledTask -TaskName $app.Task -ErrorAction SilentlyContinue
                if ($t) {
                    $info = Get-ScheduledTaskInfo -TaskName $app.Task -ErrorAction SilentlyContinue
                    Write-Host ("   {0}" -f $app.Task) -ForegroundColor White
                    Write-Host ("     eseguibile : {0}" -f $app.Exe) -ForegroundColor Gray
                    Write-Host ("     stato      : {0}" -f $t.State) -ForegroundColor Gray
                    if ($info) {
                        Write-Host ("     ultima esec: {0}  esito {1}" -f `
                                    $info.LastRunTime, $info.LastTaskResult) -ForegroundColor Gray
                    }
                }
                else {
                    Write-Host ("   {0}: NON presente" -f $app.Task) -ForegroundColor Red
                }
            }
            Write-Host ""
            Read-Host "   Premere INVIO per continuare" | Out-Null
        }

        default {
            $numero = 0
            if ([int]::TryParse($scelta, [ref]$numero)) {
                if ($numero -ge 1 -and $numero -le $Applicativi.Count) {
                    Write-Host ""
                    Avvia-Applicativo $Applicativi[$numero - 1]
                    Start-Sleep -Seconds 3
                }
            }
        }
    }
}

Write-Host ""
Write-Host "  --------------------------------------------------" -ForegroundColor DarkGray
Write-Host "   Promemoria" -ForegroundColor Cyan
Write-Host "  --------------------------------------------------" -ForegroundColor DarkGray
Write-Host ""
Write-Host "   Se le finestre non compaiono sul monitor del server," -ForegroundColor Gray
Write-Host "   verificare che l'autologon sia attivo e che nessuno si" -ForegroundColor Gray
Write-Host "   sia collegato in RDP normale: usare mstsc /admin, che si" -ForegroundColor Gray
Write-Host "   aggancia alla console invece di crearne una nuova." -ForegroundColor Gray
Write-Host ""
Write-Host "   Passo successivo: eseguire Setup-AeraControl.exe sui client." -ForegroundColor Gray
Write-Host ""

if (-not $NonInterattivo) { Read-Host "   Premere INVIO per chiudere" | Out-Null }
