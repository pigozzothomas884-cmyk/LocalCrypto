# LocalCrypto

Applicazione per Windows (system tray, .NET 8) che cifra cartelle locali e le monta come unità virtuale privata. Nessun server, nessun account, nessun aggiornamento forzato.

## Filosofia

- **Nessun server.** Tutto avviene sul tuo PC. Non esiste un backend che possa raccogliere dati, chiavi o statistiche.
- **Nessun aggiornamento forzato.** Se una versione ti funziona, tienila. Se vuoi aggiornare, scarichi la versione più recente dalla pagina [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases) e la installi tu, quando vuoi.
- **Nessun abbonamento.** Software libero con licenza MIT.
- **Vault portabile.** La cartella cifrata è autosufficiente: puoi metterla dove vuoi (disco, chiavetta, cartella sincronizzata su cloud) e sbloccarla da qualsiasi PC con LocalCrypto installato, usando la password o la chiave di recupero.

## Cosa fa

LocalCrypto protegge qualsiasi cartella locale (incluse quelle sincronizzate con servizi cloud) cifrando sia i contenuti sia i nomi e i percorsi dei file. Da sbloccata, la cartella si usa come un normale disco (`V:`–`Z:`), senza condivisioni di rete.

### Funzionalità

- **Cifratura in-place**: crea un nuovo vault oppure cifra una cartella esistente. I file diventano blob `.lc` e i metadati stanno nel file `.localcrypto`.
- **Crittografia**:
  - AES-256-GCM per contenuti, percorsi relativi e master key.
  - PBKDF2-SHA512 (600.000 iterazioni) con salt casuale per derivare le chiavi da password e chiave di recupero.
- **Montaggio come unità locale** tramite `DefineDosDevice`, con apertura automatica di Esplora File.
- **Sincronizzazione automatica**: le modifiche fatte nell'unità montata vengono ri-cifrate nella cartella protetta.
- **Chiave di recupero** per sbloccare il vault o reimpostare la password.
- **Chiusura sicura**: allo smontaggio o all'uscita l'app sincronizza, rimuove l'unità, azzera la master key in memoria ed elimina la cache temporanea in chiaro.

## Download e installazione

1. Vai alla pagina [Releases](https://github.com/pigozzothomas884-cmyk/LocalCrypto/releases).
2. Scarica l'installer della versione che preferisci.
3. *(Consigliato)* Verifica l'hash SHA-256 del file con quello indicato nella release:
   ```powershell
   Get-FileHash .\LocalCrypto-Setup.exe -Algorithm SHA256
   ```
4. Esegui l'installer.

> TODO: sostituisci il nome del file con quello reale e pubblica gli hash a ogni release.

## Compilare dai sorgenti

Requisiti: Windows e [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (con supporto Windows Forms).

```
dotnet build -c Release
dotnet run
```

## Modello di minaccia

**Da cosa protegge**

- Furto o smarrimento del disco, della chiavetta o del PC spento.
- Accesso di terzi alla cartella sincronizzata su un servizio cloud: chi la copia vede solo blob cifrati e nomi cifrati.

**Da cosa NON protegge**

- **Malware o persone con accesso al PC mentre il vault è montato.** Con il vault sbloccato i file sono accessibili in chiaro.
- **Keylogger** che catturano la password mentre la digiti.
- **Contenuto della RAM** durante l'uso.
- **Residui della cache temporanea.** Mentre il vault è montato, i file in uso risiedono in una cache locale in chiaro. In caso di crash o spegnimento improvviso la cache potrebbe non essere eliminata, e sui dischi SSD frammenti di file cancellati possono restare recuperabili.
- **Attacchi alla password.** Chi possiede la cartella del vault può provare a indovinare la password offline. PBKDF2 con 600.000 iterazioni rallenta l'attacco ma non lo impedisce: usa una passphrase lunga e unica.

LocalCrypto non è stato sottoposto a un audit di sicurezza indipendente. Per dati critici valuta anche strumenti già verificati dalla comunità.

## Avvertenze d'uso

- **Se perdi sia la password sia la chiave di recupero, i dati non sono recuperabili.** Non esiste nessuno che possa resettarli per te.
- **Usa il vault da un solo PC alla volta**, soprattutto se la cartella è sincronizzata su cloud: modifiche simultanee da due macchine possono generare conflitti.
- **Fai un backup** della cartella cifrata e conserva la chiave di recupero in un luogo separato.

## Licenza

Distribuito sotto licenza MIT. Vedi [LICENSE](LICENSE).
