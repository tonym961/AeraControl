# AeraControl

Avvia e ferma gli applicativi relativi ad Aera Remote Functions che
stanno su un server o macchina Windows che funge da Server Remote
Functions, comandandoli da un client, con le finestre che restano
visibili sul monitor del server.

## Installazione

Portare **`Setup-AeraControl.exe`** sulla macchina ed eseguirlo. Chiede
da solo i privilegi di amministratore, che gli servono per forza, e poi
chiede se il computer e' il server o un client: del resto si occupa lui,
in un'unica finestra, senza aprirne altre.

Rieseguirlo su una macchina che ha gia' una versione precedente la
aggiorna: riconosce quella installata, chiude cio' che sta girando e
riscrive tutto.

- **Server** — installa il segnalatore di stato, crea le attivita'
  pianificate e sistema UAC di rete, profilo di rete e firewall. Prima
  di procedere chiede l'utente proprietario delle attivita', quello
  nella cui sessione compariranno le finestre, e se aprire il
  segnalatore a ogni accesso.

  Spegne inoltre il **firewall su tutti i profili**, che serve perche' i
  palmari si comandino, e disattiva gli **avvisi di Windows**, che
  comparirebbero sopra le finestre dei palmari. Il firewall non basta
  spegnerlo una volta: un'attivita' pianificata lo ricontrolla ogni
  cinque minuti e lo rispegne se qualcosa lo riaccende. Le regole
  aperte restano registrate, quindi riaccendendolo tornano valide.
- **Client** — installa la console e crea i collegamenti. Poi si preme
  *Configura* e si indicano server, utente e password.

Tutto finisce in `C:\iotatau\AeraControl`.

Perche' le finestre compaiano sul monitor del server serve una sessione
sempre aperta: di norma l'autologon dell'utente proprietario delle
attivita'.

## Cosa fa

**Console (client)** — una riga per applicativo con stato, percorso e i
pulsanti Avvia / Riavvia / Ferma, piu' le stesse azioni su tutti.
Dalla finestra *Configurazione* si imposta anche l'apertura automatica
all'accesso a Windows e l'avvio degli applicativi appena connessi.

**Segnalatore (server)** — un pallino accanto all'orologio: verde tutti
attivi, giallo alcuni, rosso nessuno. Cliccandolo si apre il dettaglio,
con avvio e arresto di ciascuno.

Fa anche da guardiano: se trova fermo un applicativo sorvegliato lo
riavvia, cosi' dopo un riavvio di manutenzione il server torna su da
solo. E' acceso di serie e si spegne dalla spunta *Riavvia gli
applicativi sorvegliati se li trova fermi*, o dal menu dell'icona.

Se un applicativo non riparte, dopo tre tentativi il guardiano smette e
riprova un quarto d'ora dopo, invece di rilanciarlo all'infinito. Cio'
che si ferma con il pulsante *Ferma* resta fermo: e' una scelta, non un
guasto. Di quello che fa resta traccia in `guardiano.log`.

Sorveglia anche il firewall e lo dice nel dettaglio: acceso, i palmari
non si comandano piu' e non si capirebbe perche'. Se lo trova acceso
chiede all'attivita' `Aera_Firewall` di rispegnerlo, perche' il
segnalatore gira senza privilegi elevati e da solo non potrebbe.

Perche' funzioni, il segnalatore deve aprirsi da solo all'accesso a
Windows, con l'autologon sul server. Un servizio di Windows non
servirebbe: gira nella sessione 0, che non ha desktop, e le finestre
degli applicativi non comparirebbero su nessun monitor.

**Pulsante Palmari di AeraRestaurant** — normalmente apre gli
applicativi sul computer dove gira AeraRestaurant. Attivando l'apposita
opzione nella *Configurazione* della console, li fa invece partire sul
server. Da attivare solo sui client, mai sul server.

Riavvia sempre l'intero gruppo scelto in configurazione, non solo gli
applicativi che AeraRestaurant chiede.

Il proxy Orderman, se installato sul server, viene acceso per ultimo
quando lo trova spento. Se invece e' gia' acceso di norma lo lascia
stare, perche' fermarlo stacca i palmari collegati; chi ha bisogno che
riparta insieme agli altri accende *Riavvia anche il proxy Orderman*
nella *Configurazione*.

## File

| File | Ruolo |
|---|---|
| `Setup-AeraControl.exe` | L'installatore: un file solo, non richiede altro |
| `src/` | I sorgenti: i tre programmi, il PowerShell del server, il manifesto |
| `compila.cmd` | Rifa i tre eseguibili e reimpacchetta l'installatore |

L'installatore si porta dentro gia' compilati sia la console
(`AeraControl.exe`) sia il segnalatore (`AeraTray.exe`): non c'e'
nient'altro da scaricare.

## Versioni

Un numero solo per tutto: console, segnalatore e installatore escono
insieme e portano la stessa versione. Si legge nella barra del titolo
della console e nel menu del segnalatore, e le due devono coincidere.

Qui c'e' una versione sola, quella corrente, ed e' quella da
installare. Rieseguire l'installatore su una macchina che ne ha una
piu' vecchia la porta a questa.

## Requisiti

Windows 10/11 o Windows Server, anche senza dominio. Sulle macchine di
destinazione non serve nient'altro: l'installatore porta dentro gli
eseguibili gia' compilati.
