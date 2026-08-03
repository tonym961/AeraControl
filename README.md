# AeraControl

Avvia e ferma gli applicativi relativi ad Aera Remote Functions che
stanno su un server o macchina Windows che funge da Server Remote
Functions, comandandoli da un client, con le finestre che restano
visibili sul monitor del server.

## Installazione

Portare **`Setup-AeraControl.exe`** sulla macchina ed eseguirlo con il
tasto destro, *Esegui come amministratore*. Chiede se il computer e' il
server o un client e fa il resto.

- **Server** — installa il segnalatore di stato, crea le attivita'
  pianificate e sistema UAC di rete, profilo di rete e firewall.
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

**Pulsante Palmari di AeraRestaurant** — normalmente apre gli
applicativi sul computer dove gira AeraRestaurant. Attivando l'apposita
opzione nella *Configurazione* della console, li fa invece partire sul
server. Da attivare solo sui client, mai sul server.

Riavvia sempre l'intero gruppo scelto in configurazione, non solo gli
applicativi che AeraRestaurant chiede.

Il proxy Orderman, se installato sul server, viene acceso per ultimo ma
non viene mai riavviato: fermarlo staccherebbe i palmari collegati. Per
riavviarlo c'e' il suo pulsante nella console.

## File

| File | Ruolo |
|---|---|
| `Setup-AeraControl.exe` | L'installatore: un file solo, non richiede altro |
| `releases/` | Una cartella per versione, con il suo installatore |
| `src/*.cs` | I sorgenti |

L'installatore si porta dentro gia' compilati sia la console
(`AeraControl.exe`) sia il segnalatore (`AeraTray.exe`): non c'e'
nient'altro da scaricare.

## Versioni

Client e server hanno numerazioni separate. Si leggono nella barra del
titolo della console e nel menu del segnalatore.

Ogni versione ha la sua cartella sotto `releases/`: per tornare
indietro basta prendere l'installatore da li' ed eseguirlo.

## Requisiti

Windows 10/11 o Windows Server, anche senza dominio. Sulle macchine di
destinazione non serve nient'altro: l'installatore porta dentro gli
eseguibili gia' compilati.
