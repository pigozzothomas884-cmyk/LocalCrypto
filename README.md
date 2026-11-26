# LocalCrypto

Applicazione desktop per Windows (system tray) in .NET 8 per cifrare cartelle locali e montarle come unità virtuale privata.

## Cosa fa

**LocalCrypto** permette di proteggere qualsiasi cartella locale (incluse cartelle sincronizzate come Google Drive) cifrando sia i contenuti che i nomi/percorsi dei file, per poi lavorarci comodamente come su un disco locale temporaneo (`V:`–`Z:`) senza condivisioni di rete.

### Funzionalità principali

- **Cifratura locale e in-place**: crea un nuovo vault oppure cifra una cartella esistente trasformando i file in blob `.lc` con metadati nel file `.localcrypto`.
- **Crittografia forte**:
  - **AES-256-GCM** per cifrare contenuti, percorsi relativi dei file e master key.
  - **PBKDF2-SHA512** (600.000 iterazioni) con salt casuale per derivare le chiavi da password e chiave di recupero.
- **Montaggio come disco locale**: monta il vault sbloccato su una lettera di unità (`V:`, `W:`, `X:`, `Y:`, `Z:`) tramite `DefineDosDevice` e apre Esplora File.
- **Sincronizzazione automatica**: monitora le modifiche nella cache locale tramite `FileSystemWatcher` e ri-cifra automaticamente i file modificati nella cartella protetta.
- **Chiave di recupero**: genera una recovery key per sbloccare il vault o reimpostare la password in caso di smarrimento.
- **Chiusura sicura**: allo smontaggio o all'uscita dall'app, sincronizza le modifiche, rimuove l'unità virtuale, azzera la master key in memoria ed elimina la cache temporanea in chiaro.

## Requisiti

- Windows
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (con supporto Windows Forms)

## Compilazione ed esecuzione

```powershell
dotnet build -c Release
dotnet run
```

## Licenza

Distribuito sotto licenza MIT. Vedi [LICENSE](LICENSE) per maggiori informazioni.
